using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CraneLoadingSystem.Models;

/// <summary>
/// 鹤位运行状态枚举
/// </summary>
public enum CraneStatus
{
    [Description("待机")]
    Idle = 0,

    [Description("就绪")]
    Ready = 1,

    [Description("装料中")]
    Loading = 2,

    [Description("暂停")]
    Paused = 3,

    [Description("完成")]
    Completed = 4,

    [Description("故障")]
    Fault = 5,

    [Description("离线")]
    Offline = 6,

    [Description("紧急停止")]
    EmergencyStop = 7
}

/// <summary>
/// 鹤位配置信息（静态属性，从 appsettings.json 加载）
/// </summary>
public partial class CranePositionConfig : ObservableObject
{
    /// <summary>鹤位编号（如 CP001）</summary>
    [ObservableProperty] private string _id = string.Empty;
    /// <summary>鹤位名称（如 "1#鹤位"）</summary>
    [ObservableProperty] private string _name = string.Empty;
    /// <summary>该鹤位支持的产品名/编号（用于下发匹配）</summary>
    [ObservableProperty] private string _productName = string.Empty;
    /// <summary>最大流量 (L/min)，用于估算剩余时间</summary>
    [ObservableProperty] private double _maxFlowRate;
    /// <summary>PLC Modbus 从站地址</summary>
    [ObservableProperty] private int _plcAddress;
    /// <summary>备注说明</summary>
    [ObservableProperty] private string? _description;
}

/// <summary>
/// 鹤位实时运行数据
/// </summary>
public partial class CraneRealtimeData : ObservableObject
{
    /// <summary>瞬时流量 (L/min)</summary>
    [ObservableProperty] private double _instantFlow;

    /// <summary>累计流量 (L)</summary>
    [ObservableProperty] private double _totalFlow;

    /// <summary>当前装载量 (kg)</summary>
    [ObservableProperty] private double _loadedWeight;

    /// <summary>剩余装载量 (kg)</summary>
    [ObservableProperty] private double _remainingWeight;

    /// <summary>入口压力 (MPa)</summary>
    [ObservableProperty] private double _inletPressure;

    /// <summary>出口压力 (MPa)</summary>
    [ObservableProperty] private double _outletPressure;

    /// <summary>温度 (℃)</summary>
    [ObservableProperty] private double _temperature;

    /// <summary>密度 (kg/m³)</summary>
    [ObservableProperty] private double _density;

    /// <summary>装载进度百分比</summary>
    [ObservableProperty] private double _progress;

    /// <summary>装载已用时间 (秒)</summary>
    [ObservableProperty] private int _elapsedSeconds;

    /// <summary>预计剩余时间 (秒)</summary>
    [ObservableProperty] private int _estimatedRemainingSeconds;

    /// <summary>数据最后更新时间</summary>
    [ObservableProperty] private DateTime? _lastUpdateTime;
}

/// <summary>
/// 鹤位完整状态（运行时实体，绑定 UI 卡片）
/// </summary>
public partial class CranePosition : ObservableObject
{
    /// <summary>静态配置</summary>
    [ObservableProperty] private CranePositionConfig _config = new();
    /// <summary>实时运行数据</summary>
    [ObservableProperty] private CraneRealtimeData _realtimeData = new();
    /// <summary>当前状态</summary>
    [ObservableProperty] private CraneStatus _status = CraneStatus.Offline;
    /// <summary>当前正在执行的单据（无则 null）</summary>
    [ObservableProperty] private LoadingOrder? _currentOrder;
    /// <summary>数据最后更新时间</summary>
    [ObservableProperty] private DateTime? _lastUpdateTime;
    /// <summary>当前报警提示信息（null/空字符串时不显示报警条）</summary>
    [ObservableProperty] private string? _alarmMessage;
    /// <summary>PLC 是否已连接</summary>
    [ObservableProperty] private bool _isPlcConnected;
    /// <summary>是否处于紧急停止状态</summary>
    [ObservableProperty] private bool _isEmergencyStop;
    /// <summary>是否为远程模式（false=现场模式，禁用远程指令）</summary>
    [ObservableProperty] private bool _isRemoteMode = true;

    /// <summary>
    /// 8项安全联锁状态集合（绑定UI图标矩阵）
    /// </summary>
    public ObservableCollection<SafetyInterlockItem> SafetyInterlocks { get; } =
        new ObservableCollection<SafetyInterlockItem>(SafetyInterlockState.CreateDefaultSet());

    /// <summary>启动前8项是否全部满足</summary>
    [ObservableProperty] private bool _allInterlocksSatisfied = true;

    /// <summary>当前缺失的联锁项名称（启动前提示用）</summary>
    [ObservableProperty] private string? _missingInterlockNames;

    /// <summary>鹤位ID快捷访问</summary>
    public string Id => Config.Id;

    /// <summary>鹤位名称快捷访问</summary>
    public string Name => Config.Name;
}
