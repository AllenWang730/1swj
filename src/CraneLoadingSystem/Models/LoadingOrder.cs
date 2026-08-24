using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CraneLoadingSystem.Models;

/// <summary>
/// 单据来源系统
/// </summary>
public enum OrderSource
{
    SAP = 0,
    ERP = 1,
    Manual = 2
}

/// <summary>
/// 单据状态
/// </summary>
public enum OrderStatus
{
    Created = 0,
    Dispatched = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}

/// <summary>
/// 装料单据信息（从SAP/ERP获取）
/// </summary>
public partial class LoadingOrder : ObservableObject
{
    /// <summary>单据编号</summary>
    [ObservableProperty] private string _orderNo = string.Empty;

    /// <summary>来源系统</summary>
    [ObservableProperty] private OrderSource _source;

    /// <summary>单据状态</summary>
    [ObservableProperty] private OrderStatus _status;

    /// <summary>单据创建时间</summary>
    [ObservableProperty] private DateTime _createTime;

    /// <summary>下发时间</summary>
    [ObservableProperty] private DateTime? _dispatchTime;

    /// <summary>完成时间</summary>
    [ObservableProperty] private DateTime? _completeTime;

    /// <summary>客户名称</summary>
    [ObservableProperty] private string _customerName = string.Empty;

    /// <summary>客户编号</summary>
    [ObservableProperty] private string _customerCode = string.Empty;

    /// <summary>车牌号</summary>
    [ObservableProperty] private string _vehicleNo = string.Empty;

    /// <summary>司机姓名</summary>
    [ObservableProperty] private string _driverName = string.Empty;

    /// <summary>司机手机号</summary>
    [ObservableProperty] private string? _driverPhone;

    /// <summary>产品编码</summary>
    [ObservableProperty] private string _productCode = string.Empty;

    /// <summary>产品名称</summary>
    [ObservableProperty] private string _productName = string.Empty;

    /// <summary>计划装载量 (kg)</summary>
    [ObservableProperty] private double _plannedWeight;

    /// <summary>允许误差 (kg)</summary>
    [ObservableProperty] private double _allowedTolerance = 10;

    /// <summary>鹤位编号（分配后）</summary>
    [ObservableProperty] private string? _assignedCraneId;

    /// <summary>实际装载量 (kg)</summary>
    [ObservableProperty] private double _actualWeight;

    /// <summary>单价</summary>
    [ObservableProperty] private decimal _unitPrice;

    /// <summary>总金额</summary>
    [ObservableProperty] private decimal _totalAmount;

    /// <summary>合同编号</summary>
    [ObservableProperty] private string? _contractNo;

    /// <summary>批次号</summary>
    [ObservableProperty] private string? _batchNo;

    /// <summary>仓库/罐区</summary>
    [ObservableProperty] private string? _tankArea;

    /// <summary>备注</summary>
    [ObservableProperty] private string? _remarks;
}
