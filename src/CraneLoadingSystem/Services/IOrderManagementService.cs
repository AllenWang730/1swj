using System.Collections.ObjectModel;
using CraneLoadingSystem.Models;

namespace CraneLoadingSystem.Services;

/// <summary>
/// 订单管理服务 - 负责单据的获取、分配、下发、完成回传等业务流程
/// </summary>
public interface IOrderManagementService
{
    /// <summary>待下发单据队列</summary>
    ObservableCollection<LoadingOrder> PendingOrders { get; }

    /// <summary>进行中单据</summary>
    ObservableCollection<LoadingOrder> ActiveOrders { get; }

    /// <summary>已完成单据</summary>
    ObservableCollection<LoadingOrder> CompletedOrders { get; }

    /// <summary>从SAP+ERP刷新单据列表</summary>
    Task<int> RefreshOrdersFromSourceAsync(CancellationToken cancellationToken = default);

    /// <summary>将单据分配到指定鹤位（下发工作量）</summary>
    Task<bool> DispatchOrderToCraneAsync(string orderNo, string craneId, CancellationToken cancellationToken = default);

    /// <summary>取消单据分配</summary>
    Task<bool> CancelDispatchAsync(string orderNo, CancellationToken cancellationToken = default);

    /// <summary>通知完成（鹤位完成后自动调用）</summary>
    Task<bool> NotifyOrderCompletedAsync(string craneId, double actualWeight, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default);

    /// <summary>通知异常中断（急停/联锁破坏，回传部分完成量到 SAP/ERP）</summary>
    Task<bool> NotifyOrderAbortedAsync(string craneId, double actualWeight, DateTime startTime, DateTime endTime, string reason, CancellationToken cancellationToken = default);

    /// <summary>手工创建单据（应急/补录）</summary>
    LoadingOrder CreateManualOrder(string customerName, string vehicleNo, string productCode,
        string productName, double plannedWeight, double? tolerance = null);

    /// <summary>根据单据号获取订单</summary>
    LoadingOrder? FindOrder(string orderNo);
}
