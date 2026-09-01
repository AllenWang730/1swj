using CraneLoadingSystem.Models;
using Microsoft.Extensions.Options;
using Serilog;
using System.Collections.Concurrent;

namespace CraneLoadingSystem.Services;

/// <summary>
/// PLC控制服务 - 模拟实现（带仿真模式）
/// 实际生产环境应使用S7.Net、Modbus或OPC UA等真实通讯库
/// </summary>
public class PlcControlService : IPlcControlService
{
    private readonly AppConfig _config;
    private readonly ConcurrentDictionary<string, CraneRealtimeData> _simulationData = new();
    private readonly ConcurrentDictionary<string, CraneIoStatus> _simulationIo = new();
    private readonly ConcurrentDictionary<string, LoadingOrder?> _activeOrders = new();
    private readonly ConcurrentDictionary<string, CraneStatus> _craneStatus = new();
    private Timer? _simulationTimer;

    public bool IsConnected { get; private set; }

    public PlcControlService(IOptions<AppConfig> config)
    {
        _config = config.Value;
    }

    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 初始化所有鹤位仿真数据
            foreach (var crane in _config.CranePositions)
            {
                _simulationData[crane.Id] = new CraneRealtimeData();
                _simulationIo[crane.Id] = new CraneIoStatus
                {
                    IsCranePositioned = true,
                    IsClampConnected = true,
                    IsTankCoverOpen = true,
                    // 仿真模式：8项联锁全部预设为正常（模拟司机已完成全部准备工作）
                    DiHumanStatic = true,
                    DiStaticClamp = true,
                    DiArmConnected = true,
                    DiArmHomed = true,         // 归位：未装车时true=已归位
                    DiVehicleBlock = true,
                    DiKeyInterlock = true,
                    DiOverflowAlarm = false,   // 正常
                    DiEmergencyStop = false    // 正常
                };
                _craneStatus[crane.Id] = CraneStatus.Idle;
                _activeOrders[crane.Id] = null;
            }

            if (_config.AppSettings.EnableSimulation)
            {
                // 仿真模式下启动定时器模拟数据更新
                _simulationTimer = new Timer(SimulationUpdateCallback, null, 500,
                    _config.AppSettings.DataRefreshIntervalMs);
                Log.Information("[PlcService] 仿真模式已启动，鹤位数: {Count}", _config.CranePositions.Count);
            }
            else
            {
                // TODO: 真实环境下建立PLC连接
                Log.Information("[PlcService] 正在连接 PLC {Ip}:{Port}...",
                    _config.PlcSettings.IpAddress, _config.PlcSettings.Port);
            }

            IsConnected = true;
            Log.Information("[PlcService] PLC服务连接成功");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlcService] 连接PLC失败");
            IsConnected = false;
            return Task.FromResult(false);
        }
    }

    public Task DisconnectAsync()
    {
        _simulationTimer?.Dispose();
        _simulationTimer = null;
        IsConnected = false;
        Log.Information("[PlcService] PLC服务已断开");
        return Task.CompletedTask;
    }

    public Task<bool> RemoteStartAsync(string craneId, LoadingOrder order, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsConnected)
            {
                Log.Warning("[PlcService] 远程启动失败：PLC未连接");
                return Task.FromResult(false);
            }

            Log.Information("[PlcService] 远程启动鹤位 {CraneId}，单据: {OrderNo}, 定量: {Weight}kg",
                craneId, order.OrderNo, order.PlannedWeight);

            // 写参数 + 启动
            WriteCraneParamsInternal(craneId, order.PlannedWeight, order.AllowedTolerance);
            _activeOrders[craneId] = order;
            _craneStatus[craneId] = CraneStatus.Loading;

            // 仿真：启动时初始化I/O
            if (_config.AppSettings.EnableSimulation && _simulationIo.TryGetValue(craneId, out var io))
            {
                io.IsCranePositioned = true;
                io.IsClampConnected = true;
                io.IsTankCoverOpen = true;
                io.IsInletValveOpen = true;
                io.IsOutletValveOpen = true;
                io.IsPumpRunning = true;
                // 仿真：装车启动时各项安全联锁DI信号初始化为正常状态
                io.DiHumanStatic = true;     // DI00001 已释放
                io.DiStaticClamp = true;    // DI00002 已连接
                io.DiArmConnected = true;   // DI00003 已到位
                io.DiArmHomed = false;       // DI00004 未归位（装车中应false）
                io.DiVehicleBlock = true;   // DI00005 已升起
                io.DiKeyInterlock = true;   // DI00006 已到位
                io.DiOverflowAlarm = false; // DI00007 正常
                io.DiEmergencyStop = false; // DI00008 正常
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlcService] 远程启动鹤位 {CraneId} 异常", craneId);
            return Task.FromResult(false);
        }
    }

    public Task<bool> RemoteStopAsync(string craneId, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("[PlcService] 远程停止鹤位 {CraneId}", craneId);
            _craneStatus[craneId] = CraneStatus.Completed;
            if (_simulationIo.TryGetValue(craneId, out var io))
            {
                io.IsPumpRunning = false;
                io.IsInletValveOpen = false;
                io.IsOutletValveOpen = false;
            }
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlcService] 远程停止鹤位 {CraneId} 异常", craneId);
            return Task.FromResult(false);
        }
    }

    public Task<bool> RemotePauseAsync(string craneId, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("[PlcService] 远程暂停鹤位 {CraneId}", craneId);
            // P0 fix: 使用 AddOrUpdate 保证原子性（原 read-check-write 在并发下可能被覆盖）
            var updated = _craneStatus.AddOrUpdate(
                craneId,
                _ => CraneStatus.Paused,       // key 不存在时 → Paused
                (_, cur) => cur == CraneStatus.Loading ? CraneStatus.Paused : cur);
            if (_simulationIo.TryGetValue(craneId, out var io))
                io.IsPumpRunning = false;
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlcService] 暂停鹤位 {CraneId} 异常", craneId);
            return Task.FromResult(false);
        }
    }

    public Task<bool> RemoteResumeAsync(string craneId, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("[PlcService] 远程恢复鹤位 {CraneId}", craneId);
            // P0 fix: 原子操作
            _craneStatus.AddOrUpdate(
                craneId,
                _ => CraneStatus.Loading,
                (_, cur) => cur == CraneStatus.Paused ? CraneStatus.Loading : cur);
            if (_simulationIo.TryGetValue(craneId, out var io))
                io.IsPumpRunning = true;
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlcService] 恢复鹤位 {CraneId} 异常", craneId);
            return Task.FromResult(false);
        }
    }

    public Task<bool> EmergencyStopAsync(string craneId, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Warning("[PlcService] ===== 紧急停止触发 {CraneId} =====", craneId);
            _craneStatus[craneId] = CraneStatus.EmergencyStop;
            if (_simulationIo.TryGetValue(craneId, out var io))
            {
                io.IsPumpRunning = false;
                io.IsInletValveOpen = false;
                io.IsOutletValveOpen = false;
            }
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlcService] 紧急停止指令异常 {CraneId}", craneId);
            return Task.FromResult(false);
        }
    }

    public Task<bool> EmergencyResetAsync(string craneId, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("[PlcService] 紧急停止复位 {CraneId}", craneId);
            // P1 fix: 用 TryGetValue 替代索引器 getter，避免 craneId 不存在时抛 KeyNotFoundException
            if (_craneStatus.TryGetValue(craneId, out var curStatus) && curStatus == CraneStatus.EmergencyStop)
                _craneStatus[craneId] = CraneStatus.Idle;
            // ★ 必须同步清除急停 DI 信号，否则后续 CheckStartupAsync 会因 EmergencyStop 联锁仍触发而拒绝复位
            // （现场急停按钮物理释放后由硬件回写 DI=false，仿真模式此处直接清零）
            if (_simulationIo.TryGetValue(craneId, out var io))
            {
                io.DiEmergencyStop = false;
                io.DiOverflowAlarm = false;  // 同步清溢油报警（若曾触发）
                // 复位后阀门/泵保持关闭，等待 RemoteStartAsync 重新打开
                io.IsPumpRunning = false;
                io.IsInletValveOpen = false;
                io.IsOutletValveOpen = false;
            }
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlcService] 急停复位 {CraneId} 异常", craneId);
            return Task.FromResult(false);
        }
    }

    public Task<CraneRealtimeData?> ReadRealtimeDataAsync(string craneId, CancellationToken cancellationToken = default)
    {
        if (_simulationData.TryGetValue(craneId, out var data))
            return Task.FromResult<CraneRealtimeData?>(data);
        return Task.FromResult<CraneRealtimeData?>(null);
    }

    public Task<bool> WriteCraneParamsAsync(string craneId, double targetWeight, double tolerance, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(WriteCraneParamsInternal(craneId, targetWeight, tolerance));
    }

    private bool WriteCraneParamsInternal(string craneId, double targetWeight, double tolerance)
    {
        // 仿真：写入参数记录到日志
        Log.Debug("[PlcService] 写参数->{CraneId}: 定量={Weight}kg, 误差={Tol}kg", craneId, targetWeight, tolerance);
        return true;
    }

    public Task<CraneIoStatus?> ReadIoStatusAsync(string craneId, CancellationToken cancellationToken = default)
    {
        if (_simulationIo.TryGetValue(craneId, out var io))
            return Task.FromResult<CraneIoStatus?>(io);
        return Task.FromResult<CraneIoStatus?>(null);
    }

    /// <summary>仿真模式：获取某鹤位当前状态</summary>
    public CraneStatus GetCraneStatus(string craneId) => _craneStatus.TryGetValue(craneId, out var s) ? s : CraneStatus.Offline;

    public LoadingOrder? GetActiveOrder(string craneId) => _activeOrders.TryGetValue(craneId, out var o) ? o : null;

    /// <summary>仿真测试注入：手动设置某鹤位某 DI 信号（用于演示联锁破坏→自动急停）</summary>
    public void SetSimulationSignal(string craneId, SafetyInterlockKind kind, bool value)
    {
        if (!_simulationIo.TryGetValue(craneId, out var io)) return;
        switch (kind)
        {
            case SafetyInterlockKind.HumanStatic: io.DiHumanStatic = value; break;
            case SafetyInterlockKind.StaticClamp: io.DiStaticClamp = value; break;
            case SafetyInterlockKind.ArmConnected: io.DiArmConnected = value; break;
            case SafetyInterlockKind.ArmHomed: io.DiArmHomed = value; break;
            case SafetyInterlockKind.VehicleBlock: io.DiVehicleBlock = value; break;
            case SafetyInterlockKind.KeyInterlock: io.DiKeyInterlock = value; break;
            case SafetyInterlockKind.OverflowAlarm: io.DiOverflowAlarm = value; break;
            case SafetyInterlockKind.EmergencyStop: io.DiEmergencyStop = value; break;
        }
        Log.Information("[PlcService] 仿真信号注入 {Crane} {Kind}={Val}", craneId, kind, value);
    }

    /// <summary>仿真测试注入：让某鹤位所有安全联锁一次性置为正常状态</summary>
    public void SetAllSimulationSignalsReady(string craneId)
    {
        if (_simulationIo.TryGetValue(craneId, out var io))
        {
            io.DiHumanStatic = true;
            io.DiStaticClamp = true;
            io.DiArmConnected = true;
            io.DiArmHomed = false;
            io.DiVehicleBlock = true;
            io.DiKeyInterlock = true;
            io.DiOverflowAlarm = false;
            io.DiEmergencyStop = false;
        }
    }

    private void SimulationUpdateCallback(object? state)
    {
        // 仿真数据生成
        foreach (var craneId in _simulationData.Keys.ToList())
        {
            var data = _simulationData[craneId];
            var status = _craneStatus[craneId];
            var order = _activeOrders.TryGetValue(craneId, out var o) ? o : null;

            if (status == CraneStatus.Loading && order != null)
            {
                var craneCfg = _config.CranePositions.FirstOrDefault(c => c.Id == craneId);
                var flowRate = craneCfg?.MaxFlowRate ?? 200; // L/min
                // P1 fix: 根据产品名称查密度表（原硬编码 750 导致柴油等密度偏差 >10%）
                var density = order.ProductCode switch
                {
                    "P001" or "92#" or "92#车用汽油" or "汽油" => 745,
                    "P002" or "95#" or "95#车用汽油" => 752,
                    "P003" or "0#" or "0#车用柴油" or "柴油" => 840,
                    "P004" or "LPG" or "液化气" => 580,
                    _ => 750  // 默认值
                };
                double kgPerTick = (flowRate / 60.0) * (_config.AppSettings.DataRefreshIntervalMs / 1000.0) * (density / 1000.0);
                kgPerTick *= 0.95 + Random.Shared.NextDouble() * 0.1; // 随机误差

                data.InstantFlow = flowRate * (0.9 + Random.Shared.NextDouble() * 0.2);
                data.LoadedWeight = Math.Min(order.PlannedWeight, data.LoadedWeight + kgPerTick);
                data.TotalFlow += (flowRate / 60.0) * (_config.AppSettings.DataRefreshIntervalMs / 1000.0);
                data.RemainingWeight = Math.Max(0, order.PlannedWeight - data.LoadedWeight);
                data.Progress = Math.Min(100, data.LoadedWeight / order.PlannedWeight * 100.0);
                data.InletPressure = 0.45 + Random.Shared.NextDouble() * 0.1;
                data.OutletPressure = 0.35 + Random.Shared.NextDouble() * 0.08;
                data.Temperature = 22 + Random.Shared.NextDouble() * 3;
                data.Density = density;
                data.ElapsedSeconds += _config.AppSettings.DataRefreshIntervalMs / 1000;
                data.EstimatedRemainingSeconds = data.InstantFlow > 0
                    ? (int)(data.RemainingWeight / (data.InstantFlow * (density / 1000.0) / 60.0))
                    : 0;
                data.EstimatedRemainingSeconds = Math.Max(0, data.EstimatedRemainingSeconds);
                data.LastUpdateTime = DateTime.Now;

                // 达到定量自动完成
                if (data.LoadedWeight >= order.PlannedWeight - order.AllowedTolerance * 0.2)
                {
                    _craneStatus[craneId] = CraneStatus.Completed;
                    data.InstantFlow = 0;
                    if (_simulationIo.TryGetValue(craneId, out var io))
                    {
                        io.IsPumpRunning = false;
                        io.IsInletValveOpen = false;
                        io.IsOutletValveOpen = false;
                    }
                    Log.Information("[PlcService] 鹤位 {CraneId} 装料完成，实际: {Weight:F2}kg / 计划: {Plan:F2}kg",
                        craneId, data.LoadedWeight, order.PlannedWeight);
                }
            }
            else if (status == CraneStatus.Idle || status == CraneStatus.Ready || status == CraneStatus.Offline)
            {
                data.InstantFlow = 0;
                data.Progress = 0;
                data.ElapsedSeconds = 0;
                data.EstimatedRemainingSeconds = 0;
                data.InletPressure = 0;
                data.OutletPressure = 0;
            }
        }
    }
}
