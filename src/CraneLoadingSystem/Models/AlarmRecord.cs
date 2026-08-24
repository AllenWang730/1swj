using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace CraneLoadingSystem.Models;

/// <summary>
/// 报警信息
/// </summary>
public partial class AlarmRecord : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private DateTime _time = DateTime.Now;
    [ObservableProperty] private string _craneId = string.Empty;
    [ObservableProperty] private string _craneName = string.Empty;
    [ObservableProperty] private AlarmLevel _level = AlarmLevel.Info;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string? _detail;
    [ObservableProperty] private bool _acknowledged;
    [ObservableProperty] private DateTime? _acknowledgedTime;
}

public enum AlarmLevel
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

/// <summary>
/// 操作日志
/// </summary>
public partial class OperationLog : ObservableObject
{
    [ObservableProperty] private long _id;
    [ObservableProperty] private DateTime _time = DateTime.Now;
    [ObservableProperty] private string _operator = "System";
    [ObservableProperty] private string _action = string.Empty;
    [ObservableProperty] private string _craneId = string.Empty;
    [ObservableProperty] private string? _orderNo;
    [ObservableProperty] private string? _detail;
    [ObservableProperty] private string _ip = string.Empty;
}
