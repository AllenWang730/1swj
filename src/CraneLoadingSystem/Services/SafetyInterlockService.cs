using CraneLoadingSystem.Models;
using Serilog;

namespace CraneLoadingSystem.Services;

/// <summary>
/// 联锁校验结果
/// </summary>
public class InterlockCheckResult
{
    public bool AllSatisfied { get; init; }
    public IReadOnlyList<SafetyInterlockItem> FailedItems { get; init; } = Array.Empty<SafetyInterlockItem>();
    public string MissingDescription => FailedItems.Count == 0
        ? string.Empty
        : "缺失: " + string.Join("、", FailedItems.Select(f => f.Name));
}

/// <summary>
/// 运行中联锁破坏事件参数
/// </summary>
public class InterlockBreachEventArgs : EventArgs
{
    public required string CraneId { get; init; }
    public required SafetyInterlockKind Kind { get; init; }
    public required string ItemName { get; init; }
    public required string Reason { get; init; }
    public bool RequiresEmergencyStop { get; init; } = true;
}

/// <summary>
/// 安全联锁服务 - 8项联锁启动前校验 + 装车中实时监控
/// </summary>
public interface ISafetyInterlockService
{
    /// <summary>启动前8项联锁校验</summary>
    Task<InterlockCheckResult> CheckStartupAsync(CranePosition crane);

    /// <summary>装车中实时监控：将 PLC I/O 信号同步到鹤位的 SafetyInterlocks 集合，并检测破坏</summary>
    Task<InterlockBreachEventArgs?> MonitorRuntimeAsync(CranePosition crane);

    /// <summary>运行中联锁破坏事件（CraneManager 订阅以触发自动急停）</summary>
    event EventHandler<InterlockBreachEventArgs>? OnInterlockBreached;
}

/// <summary>
/// 安全联锁服务实现
/// </summary>
public class SafetyInterlockService : ISafetyInterlockService
{
    private readonly IPlcControlService _plc;

    public SafetyInterlockService(IPlcControlService plc)
    {
        _plc = plc;
    }

    public event EventHandler<InterlockBreachEventArgs>? OnInterlockBreached;

    public async Task<InterlockCheckResult> CheckStartupAsync(CranePosition crane)
    {
        var io = await _plc.ReadIoStatusAsync(crane.Id);
        if (io == null)
        {
            Log.Warning("[Safety] 鹤位 {Id} 启动校验失败：无法读取 I/O 状态", crane.Id);
            foreach (var item in crane.SafetyInterlocks)
            {
                item.SignalValue = false;
                item.StartupSatisfied = false;
                item.IsAlarming = true;
                item.IsFlashing = true;
            }
            crane.AllInterlocksSatisfied = false;
            crane.MissingInterlockNames = "PLC I/O 读取失败";
            return new InterlockCheckResult
            {
                AllSatisfied = false,
                FailedItems = crane.SafetyInterlocks.ToList()
            };
        }

        var failed = new List<SafetyInterlockItem>();
        foreach (var item in crane.SafetyInterlocks)
        {
            item.SignalValue = ReadSignal(io, item.Kind);
            item.StartupSatisfied = EvaluateStartup(item);
            item.IsAlarming = !item.StartupSatisfied;
            item.IsFlashing = !item.StartupSatisfied;
            if (!item.StartupSatisfied)
                failed.Add(item);
        }

        crane.AllInterlocksSatisfied = failed.Count == 0;
        crane.MissingInterlockNames = failed.Count == 0
            ? null
            : "缺失: " + string.Join("、", failed.Select(f => f.Name));

        Log.Information("[Safety] 鹤位 {Id} 启动校验：{Result} ({Count}项不满足)",
            crane.Id, failed.Count == 0 ? "通过" : "未通过", failed.Count);

        return new InterlockCheckResult
        {
            AllSatisfied = failed.Count == 0,
            FailedItems = failed
        };
    }

    public async Task<InterlockBreachEventArgs?> MonitorRuntimeAsync(CranePosition crane)
    {
        var io = await _plc.ReadIoStatusAsync(crane.Id);
        if (io == null) return null;

        var currentStatus = crane.Status;

        // ★ Bug fix: 急停按钮检查移到 Ready 早返之前。原代码 Ready 分支提前 return，
        //   导致 Ready 状态下现场按下急停按钮，系统不会自动反映为 EmergencyStop（虽然此状态
        //   无物料流动，安全性影响小，但状态显示与现场不同步、复位逻辑会混乱）。
        //   "任意时刻触发即全停" 应包含 Ready 状态。
        if (io.DiEmergencyStop)
        {
            var esItem = crane.SafetyInterlocks.First(i => i.Kind == SafetyInterlockKind.EmergencyStop);
            return RaiseBreach(crane.Id, esItem, "急停按钮被按下");
        }

        // Ready状态：只刷新启动前联锁显示，不触发急停
        if (currentStatus == CraneStatus.Ready)
        {
            var failed = new List<SafetyInterlockItem>();
            foreach (var item in crane.SafetyInterlocks)
            {
                item.SignalValue = ReadSignal(io, item.Kind);
                item.StartupSatisfied = EvaluateStartup(item);
                item.IsAlarming = !item.StartupSatisfied;
                item.IsFlashing = !item.StartupSatisfied;
                if (!item.StartupSatisfied) failed.Add(item);
            }
            crane.AllInterlocksSatisfied = failed.Count == 0;
            crane.MissingInterlockNames = failed.Count == 0
                ? null
                : "缺失: " + string.Join("、", failed.Select(f => f.Name));
            return null;
        }

        // 非装车中（Idle/Completed/Fault）：仅刷新信号，不触发急停
        bool inLoading = currentStatus is CraneStatus.Loading or CraneStatus.Paused;
        if (!inLoading)
        {
            foreach (var item in crane.SafetyInterlocks)
            {
                item.SignalValue = ReadSignal(io, item.Kind);
                item.IsAlarming = false;
                item.IsFlashing = false;
            }
            return null;
        }

        // 装车中（Loading/Paused）：8项联锁破坏检测→自动急停
        foreach (var item in crane.SafetyInterlocks)
        {
            item.SignalValue = ReadSignal(io, item.Kind);

            // 急停按钮：再次检查（防御性）
            if (item.Kind == SafetyInterlockKind.EmergencyStop && io.DiEmergencyStop)
                return RaiseBreach(crane.Id, item, "急停按钮被按下");

            // 溢油：装车中触发立即急停
            if (item.Kind == SafetyInterlockKind.OverflowAlarm && io.DiOverflowAlarm)
                return RaiseBreach(crane.Id, item, "溢油报警触发");

            // 静电夹/鹤管到位/阻车器/钥匙：装车中变 OFF 立即急停
            if (item.Mode == InterlockMonitorMode.MustOnAndHold && !item.SignalValue)
                return RaiseBreach(crane.Id, item, $"{item.Name}信号丢失");

            // 鹤管归位：装车中误归位（信号变true=异常）
            if (item.Kind == SafetyInterlockKind.ArmHomed && item.SignalValue)
                return RaiseBreach(crane.Id, item, "装车中鹤管误归位");
        }

        return null;
    }

    private InterlockBreachEventArgs RaiseBreach(string craneId, SafetyInterlockItem item, string reason)
    {
        item.IsAlarming = true;
        item.IsFlashing = true;
        var args = new InterlockBreachEventArgs
        {
            CraneId = craneId,
            Kind = item.Kind,
            ItemName = item.Name,
            Reason = reason,
            RequiresEmergencyStop = true
        };
        Log.Warning("[Safety] 🚨 鹤位 {Id} 联锁破坏：{Name} - {Reason}", craneId, item.Name, reason);
        OnInterlockBreached?.Invoke(this, args);
        return args;
    }

    private static bool ReadSignal(CraneIoStatus io, SafetyInterlockKind kind) => kind switch
    {
        SafetyInterlockKind.HumanStatic => io.DiHumanStatic,
        SafetyInterlockKind.StaticClamp => io.DiStaticClamp,
        SafetyInterlockKind.ArmConnected => io.DiArmConnected,
        SafetyInterlockKind.ArmHomed => io.DiArmHomed,
        SafetyInterlockKind.VehicleBlock => io.DiVehicleBlock,
        SafetyInterlockKind.KeyInterlock => io.DiKeyInterlock,
        SafetyInterlockKind.OverflowAlarm => io.DiOverflowAlarm,
        SafetyInterlockKind.EmergencyStop => io.DiEmergencyStop,
        _ => false
    };

    private static bool EvaluateStartup(SafetyInterlockItem item) => item.Mode switch
    {
        // 启动前都需满足（除急停外的报警型信号需为false=正常）
        InterlockMonitorMode.MustOffAndTriggerIsEmergency or InterlockMonitorMode.AlwaysTriggerEmergency
            => !item.SignalValue, // 报警型：未触发(true)即正常
        _ => item.SignalValue // 其余：信号ON=满足
    };
}
