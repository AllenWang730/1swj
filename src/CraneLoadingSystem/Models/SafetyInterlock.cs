using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CraneLoadingSystem.Models;

/// <summary>
/// 安全联锁项标识 - 对应 PRD 中 DI00001~DI00008
/// </summary>
public enum SafetyInterlockKind
{
    [Description("人体静电释放")] HumanStatic = 1,      // DI00001
    [Description("静电夹连接")] StaticClamp = 2,         // DI00002
    [Description("鹤管连接到位")] ArmConnected = 3,       // DI00003
    [Description("鹤管归位")] ArmHomed = 4,               // DI00004
    [Description("阻车器升起")] VehicleBlock = 5,          // DI00005
    [Description("钥匙联锁")] KeyInterlock = 6,           // DI00006
    [Description("溢油报警")] OverflowAlarm = 7,          // DI00007 (报警型: ON=触发)
    [Description("急停按钮")] EmergencyStop = 8           // DI00008 (报警型: ON=触发)
}

/// <summary>
/// 联锁项运行时监控策略
/// </summary>
public enum InterlockMonitorMode
{
    /// <summary>仅在启动前校验（如人体静电）</summary>
    StartupOnly,
    /// <summary>启动前需 ON，运行中变 OFF 立即急停（如静电夹/鹤管到位/阻车器/钥匙）</summary>
    MustOnAndHold,
    /// <summary>启动前需 ON，运行中变 ON 视为异常急停（如鹤管归位误动作）</summary>
    MustOnButAbnormalIfOnInRun,
    /// <summary>启动前需 OFF(正常)，运行中触发 ON 立即急停（溢油）</summary>
    MustOffAndTriggerIsEmergency,
    /// <summary>任意时刻触发即全局急停（急停按钮，最高优先级）</summary>
    AlwaysTriggerEmergency
}

/// <summary>
/// 单项安全联锁状态
/// </summary>
public partial class SafetyInterlockItem : ObservableObject
{
    public SafetyInterlockKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SignalAddress { get; init; } = string.Empty; // 例: DI00002
    public InterlockMonitorMode Mode { get; init; }

    /// <summary>PLC当前信号原始值(true=ON)</summary>
    [ObservableProperty] private bool _signalValue;

    /// <summary>本项是否满足启动条件</summary>
    [ObservableProperty] private bool _startupSatisfied;

    /// <summary>当前是否处于报警状态（运行中破坏）</summary>
    [ObservableProperty] private bool _isAlarming;

    /// <summary>是否需要红色闪烁显示</summary>
    [ObservableProperty] private bool _isFlashing;

    public string Icon => StartupSatisfied && !IsAlarming ? "✓" : "✗";
}

/// <summary>
/// 鹤位完整安全联锁状态（8项）
/// </summary>
public class SafetyInterlockState
{
    /// <summary>按PRD默认8项联锁定义</summary>
    public static List<SafetyInterlockItem> CreateDefaultSet() => new()
    {
        new SafetyInterlockItem
        {
            Kind = SafetyInterlockKind.HumanStatic, Name = "人体静电", SignalAddress = "DI00001",
            Mode = InterlockMonitorMode.StartupOnly
        },
        new SafetyInterlockItem
        {
            Kind = SafetyInterlockKind.StaticClamp, Name = "静电夹", SignalAddress = "DI00002",
            Mode = InterlockMonitorMode.MustOnAndHold
        },
        new SafetyInterlockItem
        {
            Kind = SafetyInterlockKind.ArmConnected, Name = "到位", SignalAddress = "DI00003",
            Mode = InterlockMonitorMode.MustOnAndHold
        },
        new SafetyInterlockItem
        {
            Kind = SafetyInterlockKind.ArmHomed, Name = "归位", SignalAddress = "DI00004",
            Mode = InterlockMonitorMode.MustOnButAbnormalIfOnInRun
        },
        new SafetyInterlockItem
        {
            Kind = SafetyInterlockKind.VehicleBlock, Name = "阻车器", SignalAddress = "DI00005",
            Mode = InterlockMonitorMode.MustOnAndHold
        },
        new SafetyInterlockItem
        {
            Kind = SafetyInterlockKind.KeyInterlock, Name = "钥匙", SignalAddress = "DI00006",
            Mode = InterlockMonitorMode.MustOnAndHold
        },
        new SafetyInterlockItem
        {
            Kind = SafetyInterlockKind.OverflowAlarm, Name = "溢油", SignalAddress = "DI00007",
            Mode = InterlockMonitorMode.MustOffAndTriggerIsEmergency
        },
        new SafetyInterlockItem
        {
            Kind = SafetyInterlockKind.EmergencyStop, Name = "急停", SignalAddress = "DI00008",
            Mode = InterlockMonitorMode.AlwaysTriggerEmergency
        }
    };
}
