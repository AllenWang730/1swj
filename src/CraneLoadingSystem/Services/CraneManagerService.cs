using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CraneLoadingSystem.Models;
using Microsoft.Extensions.Options;
using Serilog;

namespace CraneLoadingSystem.Services;

/// <summary>
/// 鹤位管理器服务实现
/// </summary>
public class CraneManagerService : ICraneManagerService
{
    private readonly AppConfig _config;
    private readonly IPlcControlService _plc;
    private readonly ISafetyInterlockService _safety;
    private readonly IAlarmManagerService _alarm;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ObservableCollection<CranePosition> Cranes { get; } = new();

    public CraneManagerService(
        IOptions<AppConfig> config,
        IPlcControlService plc,
        ISafetyInterlockService safety,
        IAlarmManagerService alarm)
    {
        _config = config.Value;
        _plc = plc;
        _safety = safety;
        _alarm = alarm;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_config.AppSettings.DataRefreshIntervalMs)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;

        // 订阅联锁破坏事件 -> 自动急停
        _safety.OnInterlockBreached += OnInterlockBreached;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("[CraneMgr] 初始化鹤位管理器...");

        // 连接PLC
        await _plc.ConnectAsync(cancellationToken);

        // 初始化鹤位实例
        foreach (var cfg in _config.CranePositions)
        {
            var crane = new CranePosition
            {
                Config = cfg,
                RealtimeData = new CraneRealtimeData(),
                Status = CraneStatus.Idle,
                IsPlcConnected = _plc.IsConnected,
                LastUpdateTime = DateTime.Now
            };
            Cranes.Add(crane);
            Log.Information("[CraneMgr] 初始化鹤位 {Id} - {Name} 产品:{Product}", cfg.Id, cfg.Name, cfg.ProductName);
        }

        _refreshTimer.Start();
        Log.Information("[CraneMgr] 鹤位管理器初始化完成，共 {Count} 个鹤位", Cranes.Count);
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        // P0 fix: 用 SemaphoreSlim 保护整个刷新循环，防止与 OnInterlockBreached / EmergencyStopAsync 并发
        await _lock.WaitAsync();
        try
        {
            foreach (var crane in Cranes)
            {
                // P0 fix: 捕获局部引用，防止在 await 期间 CurrentOrder 被 NotifyOrderAbortedAsync 清空导致 NRE
                var order = crane.CurrentOrder;

                // 读取实时数据
                var data = await _plc.ReadRealtimeDataAsync(crane.Id);
                if (data != null)
                {
                    crane.RealtimeData.InstantFlow = data.InstantFlow;
                    crane.RealtimeData.TotalFlow = data.TotalFlow;
                    crane.RealtimeData.LoadedWeight = data.LoadedWeight;
                    crane.RealtimeData.RemainingWeight = data.RemainingWeight;
                    crane.RealtimeData.Progress = data.Progress;
                    crane.RealtimeData.InletPressure = data.InletPressure;
                    crane.RealtimeData.OutletPressure = data.OutletPressure;
                    crane.RealtimeData.Temperature = data.Temperature;
                    crane.RealtimeData.Density = data.Density;
                    crane.RealtimeData.ElapsedSeconds = data.ElapsedSeconds;
                    crane.RealtimeData.EstimatedRemainingSeconds = data.EstimatedRemainingSeconds;
                }

                // 读取PLC服务中的状态（仿真模式直接从PLC服务获取）
                if (_plc is PlcControlService realPlc)
                {
                    var s = realPlc.GetCraneStatus(crane.Id);
                    // 仅在 PLC 报告非 Offline 状态时更新（Offline = 初始/断连，不应覆盖业务状态）
                    if (s != CraneStatus.Offline)
                        crane.Status = s;

                    // 自动同步激活的单据
                    var active = realPlc.GetActiveOrder(crane.Id);
                    if (active != null && order == null)
                        crane.CurrentOrder = active;
                }

                crane.IsPlcConnected = _plc.IsConnected;
                crane.LastUpdateTime = DateTime.Now;

                // 装车中安全联锁实时监控（破坏则触发 OnInterlockBreached → 自动急停）
                if (crane.Status is CraneStatus.Loading or CraneStatus.Paused or CraneStatus.Ready)
                {
                    _ = await _safety.MonitorRuntimeAsync(crane);
                }

                // 检测状态变更 -> 完成处理（P0 fix: 使用局部 order 引用避免竞态 NRE）
                if (crane.Status == CraneStatus.Completed && order != null
                    && order.Status != OrderStatus.Completed)
                {
                    OnCraneCompleted?.Invoke(this, new CraneCompletedArgs
                    {
                        CraneId = crane.Id,
                        ActualWeight = crane.RealtimeData.LoadedWeight,
                        StartTime = order.DispatchTime ?? DateTime.Now,
                        EndTime = DateTime.Now
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CraneMgr] 刷新数据异常");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>鹤位完成事件，用于通知OrderManagementService</summary>
    public event EventHandler<CraneCompletedArgs>? OnCraneCompleted;

    public CranePosition? GetCrane(string craneId)
    {
        return Cranes.FirstOrDefault(c => c.Id == craneId);
    }

    public async Task<bool> RemoteStartAsync(string craneId)
    {
        var crane = GetCrane(craneId);
        if (crane?.CurrentOrder == null)
        {
            Log.Warning("[CraneMgr] 启动失败：鹤位 {Id} 未分配单据", craneId);
            return false;
        }

        // P1 fix: 现场模式禁用远程指令（PRD 安全要求）
        if (!crane.IsRemoteMode)
        {
            Log.Warning("[CraneMgr] 启动拒绝：鹤位 {Id} 处于现场模式，远程指令被禁用", craneId);
            crane.AlarmMessage = "现场模式：远程启动被禁用";
            return false;
        }

        // ★ 启动前强制校验8项安全联锁
        var check = await _safety.CheckStartupAsync(crane);
        if (!check.AllSatisfied)
        {
            Log.Warning("[CraneMgr] 启动被拒绝：鹤位 {Id} 安全联锁未通过 - {Desc}",
                craneId, check.MissingDescription);
            crane.AlarmMessage = $"启动被拒绝：{check.MissingDescription}";  // ★ 回写卡片报警文本
            await _alarm.RaiseAsync(
                craneId, crane.Name,
                AlarmLevel.Critical,
                "启动被拒绝：安全联锁未满足",
                check.MissingDescription);
            return false;
        }

        crane.AlarmMessage = null;  // 启动校验通过，清除卡片报警
        var started = await _plc.RemoteStartAsync(craneId, crane.CurrentOrder);
        // ★ Bug fix: 启动成功后立即把单据状态推进到 InProgress。
        //   之前单据从 Dispatched 直接跳到 Completed，OrderStatus.InProgress 枚举从未被使用，
        //   导致 SAP/ERP 侧无法区分"刚下发待启动"和"正在装料"，急停中断也无法表达"装料进行中异常终止"。
        if (started && crane.CurrentOrder.Status == OrderStatus.Dispatched)
        {
            crane.CurrentOrder.Status = OrderStatus.InProgress;
            Log.Information("[CraneMgr] 鹤位 {Id} 单据 {OrderNo} 状态 Dispatched→InProgress（装料已启动）",
                craneId, crane.CurrentOrder.OrderNo);
        }
        return started;
    }

    public async Task<bool> RemoteStopAsync(string craneId)
    {
        var crane = GetCrane(craneId);
        if (crane != null && !crane.IsRemoteMode)
        {
            Log.Warning("[CraneMgr] 停止拒绝：鹤位 {Id} 处于现场模式", craneId);
            return false;
        }
        var ok = await _plc.RemoteStopAsync(craneId);
        if (ok && crane != null)
        {
            crane.Status = CraneStatus.Completed;  // ★ 同步 UI 状态（依赖 RefreshTimer 同步会延迟）
        }
        return ok;
    }

    public async Task<bool> RemotePauseAsync(string craneId)
    {
        var crane = GetCrane(craneId);
        if (crane != null && !crane.IsRemoteMode)
        {
            Log.Warning("[CraneMgr] 暂停拒绝：鹤位 {Id} 处于现场模式", craneId);
            return false;
        }
        var ok = await _plc.RemotePauseAsync(craneId);
        // ★ 同步 UI 状态：暂停后立即反映到卡片，否则用户感知"按钮没反应"
        if (ok && crane != null && crane.Status == CraneStatus.Loading)
            crane.Status = CraneStatus.Paused;
        return ok;
    }

    public async Task<bool> RemoteResumeAsync(string craneId)
    {
        var crane = GetCrane(craneId);
        if (crane == null) return false;

        // P1 fix: 现场模式禁用远程恢复
        if (!crane.IsRemoteMode)
        {
            Log.Warning("[CraneMgr] 恢复拒绝：鹤位 {Id} 处于现场模式", craneId);
            return false;
        }

        // ★ Bug fix: 恢复装料等同启动，必须重新全检8项安全联锁
        // （暂停期间现场可能拆卸了静电夹/阻车器，恢复时未重新校验是安全漏洞）
        var check = await _safety.CheckStartupAsync(crane);
        if (!check.AllSatisfied)
        {
            Log.Warning("[CraneMgr] 恢复装料被拒绝：鹤位 {Id} 联锁未通过 - {Desc}",
                craneId, check.MissingDescription);
            crane.AlarmMessage = $"恢复装料被拒：{check.MissingDescription}";
            await _alarm.RaiseAsync(
                craneId, crane.Name,
                AlarmLevel.Critical,
                "恢复装料被拒绝：安全联锁未满足",
                check.MissingDescription);
            return false;
        }

        crane.AlarmMessage = null;
        return await _plc.RemoteResumeAsync(craneId);
    }

    public async Task<bool> EmergencyStopAsync(string craneId)
    {
        var crane = GetCrane(craneId);
        // P0 fix: 加锁防止与 RefreshTimer_Tick 并发修改 crane 状态
        await _lock.WaitAsync();
        try
        {
            var ok = await _plc.EmergencyStopAsync(craneId);
            // ★ Bug fix: 必须立即同步 UI 状态。否则 crane.Status 仍是 Loading，
            // 用户感知"急停按钮没生效"；复位按钮的 IsEnabled 触发器也不匹配 EmergencyStop 而无法点击
            if (ok && crane != null)
            {
                crane.Status = CraneStatus.EmergencyStop;
                crane.IsEmergencyStop = true;
                crane.AlarmMessage = "🚨 紧急停止已触发，请现场复位后重新全检8项联锁";

                // ★ Bug fix: 手动急停也要触发异常回传事件。
                //   仅对 InProgress 订单触发（Dispatched/Ready 状态下未装料，无需回传部分量）。
                var order = crane.CurrentOrder;
                if (order != null && order.Status == OrderStatus.InProgress)
                {
                    OnCraneCompleted?.Invoke(this, new CraneCompletedArgs
                    {
                        CraneId = craneId,
                        ActualWeight = crane.RealtimeData.LoadedWeight,
                        StartTime = order.DispatchTime ?? DateTime.Now,
                        EndTime = DateTime.Now,
                        IsAborted = true,
                        AbortReason = "手动急停"
                    });
                }
            }
            return ok;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> EmergencyResetAsync(string craneId)
    {
        var crane = GetCrane(craneId);
        if (crane == null) return false;

        // 1. 先复位 PLC 急停（PLC 侧清 DiEmergencyStop 信号）
        var ok = await _plc.EmergencyResetAsync(craneId);
        if (!ok) return false;

        // 2. 重新全检8项安全联锁（PRD要求：急停后恢复必须现场复位+软件复位+重新全检）
        var check = await _safety.CheckStartupAsync(crane);
        if (!check.AllSatisfied)
        {
            Log.Warning("[CraneMgr] 鹤位 {Id} 急停复位失败：8项联锁未全通过 - {Desc}",
                craneId, check.MissingDescription);
            crane.AlarmMessage = $"急停复位失败：{check.MissingDescription}";
            await _alarm.RaiseAsync(
                craneId, crane.Name,
                AlarmLevel.Warning,
                "急停复位失败：需现场确认所有联锁",
                check.MissingDescription);
            return false;
        }

        crane.IsEmergencyStop = false;
        crane.AlarmMessage = null;  // ★ 复位成功，清卡片报警文本
        crane.Status = CraneStatus.Idle;
        Log.Information("[CraneMgr] 鹤位 {Id} 急停复位成功（8项联锁全通过）", craneId);
        return true;
    }

    /// <summary>
    /// 全局强制复位所有鹤位（不走联锁校验，直接清零所有鹤位信息恢复空闲）。
    /// 用于现场维护/换班/系统重置场景。InProgress 订单会先触发异常中断回传 SAP/ERP。
    /// 与单卡 EmergencyResetAsync 的区别：本方法是强制操作，不依赖现场联锁状态。
    /// </summary>
    public async Task<bool> ResetAllAsync()
    {
        Log.Warning("[CraneMgr] ===== 全局强制复位触发（共 {Count} 个鹤位）=====", Cranes.Count);

        // P0 fix: 加锁防止与 RefreshTimer_Tick 并发
        await _lock.WaitAsync();
        try
        {
            int successCount = 0;
            // 逐个处理而非 Task.WhenAll，避免多鹤位并发修改 + OnCraneCompleted 并发触发
            foreach (var crane in Cranes)
            {
                try
                {
                    // 1. 如果有 InProgress 订单，先触发异常中断回传（避免数据丢失账实不符）
                    var order = crane.CurrentOrder;
                    if (order != null && order.Status == OrderStatus.InProgress)
                    {
                        Log.Information("[CraneMgr] 全局复位：鹤位 {Id} 有 InProgress 单据 {OrderNo}，触发异常回传（已装 {Weight:F2}kg）",
                            crane.Id, order.OrderNo, crane.RealtimeData.LoadedWeight);
                        OnCraneCompleted?.Invoke(this, new CraneCompletedArgs
                        {
                            CraneId = crane.Id,
                            ActualWeight = crane.RealtimeData.LoadedWeight,
                            StartTime = order.DispatchTime ?? DateTime.Now,
                            EndTime = DateTime.Now,
                            IsAborted = true,
                            AbortReason = "全局强制复位"
                        });
                    }

                    // 2. PLC 侧清急停 DI 信号（不校验联锁，强制路径）
                    await _plc.EmergencyResetAsync(crane.Id);

                    // 3. 清零所有鹤位信息（实时数据/报警/订单引用/联锁状态/状态→Idle）
                    crane.ResetCrane();

                // 4. 显式设回 Idle（ResetCrane 已设，但显式表达意图，并触发 UI 刷新）
                crane.Status = CraneStatus.Idle;
                crane.IsEmergencyStop = false;
                crane.AlarmMessage = null;
                crane.IsPlcConnected = _plc.IsConnected;
                crane.LastUpdateTime = DateTime.Now;

                Log.Information("[CraneMgr] 全局复位：鹤位 {Id} 已清零恢复空闲", crane.Id);
                successCount++;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CraneMgr] 全局复位：鹤位 {Id} 异常", crane.Id);
                crane.AlarmMessage = $"复位异常：{ex.Message}";
            }
        }

            Log.Information("[CraneMgr] 全局复位完成：成功 {Success}/{Total}",
                successCount, Cranes.Count);
            return successCount == Cranes.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    public IEnumerable<CranePosition> GetAvailableCranesForProduct(string productCode)
    {
        // 产品匹配策略：鹤位产品名与单据产品名/编码做双向包含匹配
        return Cranes.Where(c =>
            (c.Status == CraneStatus.Idle || c.Status == CraneStatus.Ready || c.Status == CraneStatus.Completed)
            && (string.IsNullOrEmpty(productCode)
                || c.Config.ProductName.Contains(productCode)
                || productCode.Contains(c.Config.ProductName)
                || c.Config.ProductName == productCode));
    }

    /// <summary>
    /// 联锁破坏事件处理：自动急停 + 报警 + 异常回传
    /// P0 fix: 本方法可能从 ThreadPool 线程（MonitorRuntimeAsync 内的 RaiseBreach）调用，
    ///   对 crane.Status / crane.AlarmMessage 等 UI 绑定属性的修改必须切回 UI 线程。
    /// </summary>
    private async void OnInterlockBreached(object? sender, InterlockBreachEventArgs e)
    {
        try
        {
            var crane = GetCrane(e.CraneId);
            if (crane == null) return;

            Log.Warning("[CraneMgr] 🚨 鹤位 {Id} 联锁破坏自动急停触发：{Name} - {Reason}",
                e.CraneId, e.ItemName, e.Reason);

            // 1. 立即下发急停指令（关阀+停泵）— PLC 指令本身不涉及 UI，可直接调用
            await _plc.EmergencyStopAsync(e.CraneId);

            // P0 fix: UI 绑定属性修改切回 Dispatcher 线程
            var order = crane.CurrentOrder;  // 捕获局部引用，防止竞态
            var loadedWeight = crane.RealtimeData.LoadedWeight;
            var dispatchTime = order?.DispatchTime ?? DateTime.Now;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                crane.Status = CraneStatus.EmergencyStop;
                crane.IsEmergencyStop = true;
                crane.AlarmMessage = $"🚨 联锁破坏：{e.ItemName} - {e.Reason}，已自动急停";
            });

            // 2. 记录 Critical 报警
            await _alarm.RaiseAsync(
                e.CraneId, crane.Name,
                AlarmLevel.Critical,
                $"安全联锁破坏：{e.ItemName}",
                $"原因: {e.Reason}；鹤位已自动急停，需现场复位后重新全检8项联锁");

            // 3. 异常中断事件回传（IsAborted=true 标记，使用局部 order 引用避免竞态 NRE）
            if (order != null)
            {
                OnCraneCompleted?.Invoke(this, new CraneCompletedArgs
                {
                    CraneId = e.CraneId,
                    ActualWeight = loadedWeight,
                    StartTime = dispatchTime,
                    EndTime = DateTime.Now,
                    IsAborted = true,
                    AbortReason = $"{e.ItemName}: {e.Reason}"
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CraneMgr] 处理联锁破坏事件异常");
        }
    }
}

public class CraneCompletedArgs : EventArgs
{
    public string CraneId { get; set; } = string.Empty;
    public double ActualWeight { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    /// <summary>是否为异常中断（急停/联锁破坏），false=正常完成</summary>
    public bool IsAborted { get; set; }

    /// <summary>异常原因（仅当 IsAborted=true 时有值）</summary>
    public string? AbortReason { get; set; }
}

/// <summary>
/// 鹤位扩展方法
/// </summary>
public static class CraneExtensions
{
    /// <summary>
    /// 复位鹤位状态（仅由 UI 主动触发，不影响 PLC 侧急停 DI 信号——后者由 EmergencyResetAsync 单独处理）
    /// </summary>
    public static void ResetCrane(this CranePosition crane)
    {
        crane.CurrentOrder = null;
        crane.Status = CraneStatus.Idle;
        crane.RealtimeData = new CraneRealtimeData();
        // ★ Bug fix: 之前只重置 3 项，导致 IsEmergencyStop/AlarmMessage 等残留，
        // 下次 RemoteStartAsync 时 PLC 侧仍处于急停未复位状态
        crane.IsEmergencyStop = false;
        crane.AlarmMessage = null;
        crane.MissingInterlockNames = null;
        crane.AllInterlocksSatisfied = true;
        // 重置联锁项显示状态
        foreach (var item in crane.SafetyInterlocks)
        {
            item.IsAlarming = false;
            item.IsFlashing = false;
        }
    }
}
