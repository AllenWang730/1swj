using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace CraneLoadingSystem.Models;

/// <summary>
/// 报警信息（一条记录对应一次报警事件，含确认状态）
/// </summary>
public partial class AlarmRecord : ObservableObject
{
    /// <summary>报警自增主键（内存自增，重启后重新计数）</summary>
    [ObservableProperty] private long _id;
    /// <summary>报警发生时间</summary>
    [ObservableProperty] private DateTime _time = DateTime.Now;
    /// <summary>鹤位编号</summary>
    [ObservableProperty] private string _craneId = string.Empty;
    /// <summary>鹤位名称（便于 UI 展示）</summary>
    [ObservableProperty] private string _craneName = string.Empty;
    /// <summary>报警级别</summary>
    [ObservableProperty] private AlarmLevel _level = AlarmLevel.Info;
    /// <summary>报警简要信息</summary>
    [ObservableProperty] private string _message = string.Empty;
    /// <summary>报警详细描述（可为空）</summary>
    [ObservableProperty] private string? _detail;
    /// <summary>是否已被操作员确认</summary>
    [ObservableProperty] private bool _acknowledged;
    /// <summary>确认时间（未确认为 null）</summary>
    [ObservableProperty] private DateTime? _acknowledgedTime;
}

/// <summary>
/// 报警级别（递增严重度）
/// </summary>
public enum AlarmLevel
{
    /// <summary>信息（普通提示）</summary>
    Info = 0,
    /// <summary>警告（需关注但不影响生产）</summary>
    Warning = 1,
    /// <summary>错误（影响单步操作）</summary>
    Error = 2,
    /// <summary>严重（联锁破坏/急停，需立即处理）</summary>
    Critical = 3
}

/// <summary>
/// 操作日志（所有人工/系统操作的审计记录）
/// </summary>
public partial class OperationLog : ObservableObject
{
    /// <summary>日志自增主键</summary>
    [ObservableProperty] private long _id;
    /// <summary>操作发生时间</summary>
    [ObservableProperty] private DateTime _time = DateTime.Now;
    /// <summary>操作员名（系统操作为 "System"）</summary>
    [ObservableProperty] private string _operator = "System";
    /// <summary>动作代码（OrderDispatched/Start/Pause/Resume/EmergencyStop/Reset/OrderCompleted/OrderAborted 等）</summary>
    [ObservableProperty] private string _action = string.Empty;
    /// <summary>涉及鹤位编号</summary>
    [ObservableProperty] private string _craneId = string.Empty;
    /// <summary>涉及单据编号</summary>
    [ObservableProperty] private string? _orderNo;
    /// <summary>详情文本</summary>
    [ObservableProperty] private string? _detail;
    /// <summary>操作来源 IP（远程操作时记录）</summary>
    [ObservableProperty] private string _ip = string.Empty;
}
