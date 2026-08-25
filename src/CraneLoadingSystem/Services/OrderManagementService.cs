using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CraneLoadingSystem.Models;
using Serilog;

namespace CraneLoadingSystem.Services;

/// <summary>
/// 订单管理服务实现
/// </summary>
public partial class OrderManagementService : ObservableObject, IOrderManagementService
{
    private readonly ISapService _sapService;
    private readonly IErpService _erpService;
    private readonly ICraneManagerService _craneManager;
    private readonly IDatabaseService? _db;

    public ObservableCollection<LoadingOrder> PendingOrders { get; } = new();
    public ObservableCollection<LoadingOrder> ActiveOrders { get; } = new();
    public ObservableCollection<LoadingOrder> CompletedOrders { get; } = new();

    private readonly object _lockObj = new();

    public OrderManagementService(ISapService sapService, IErpService erpService, ICraneManagerService craneManager, IDatabaseService? db = null)
    {
        _sapService = sapService;
        _erpService = erpService;
        _craneManager = craneManager;
        _db = db;
    }

    public async Task<int> RefreshOrdersFromSourceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("[OrderMgr] 开始从SAP/ERP刷新单据...");
            var sapOrders = await _sapService.GetLoadingOrdersAsync(true, null, cancellationToken);
            var erpOrders = await _erpService.GetPendingOrdersAsync(null, cancellationToken);

            int newCount = 0;
            lock (_lockObj)
            {
                foreach (var order in sapOrders.Concat(erpOrders))
                {
                    // 去重
                    if (PendingOrders.Any(o => o.OrderNo == order.OrderNo) ||
                        ActiveOrders.Any(o => o.OrderNo == order.OrderNo) ||
                        CompletedOrders.Any(o => o.OrderNo == order.OrderNo))
                        continue;
                    PendingOrders.Add(order);
                    newCount++;
                }
            }
            Log.Information("[OrderMgr] 刷新完成，新增 {New} 条，待下发总 {Pending} 条",
                newCount, PendingOrders.Count);
            return newCount;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OrderMgr] 刷新单据异常");
            return 0;
        }
    }

    public async Task<bool> DispatchOrderToCraneAsync(string orderNo, string craneId, CancellationToken cancellationToken = default)
    {
        try
        {
            LoadingOrder? order;
            lock (_lockObj)
            {
                order = PendingOrders.FirstOrDefault(o => o.OrderNo == orderNo);
                if (order == null)
                {
                    Log.Warning("[OrderMgr] 下发失败：找不到单据 {OrderNo}", orderNo);
                    return false;
                }
                PendingOrders.Remove(order);
            }

            // 1. 分配鹤位
            var crane = _craneManager.GetCrane(craneId);
            if (crane == null)
            {
                Log.Warning("[OrderMgr] 下发失败：找不到鹤位 {CraneId}", craneId);
                lock (_lockObj) PendingOrders.Add(order);
                return false;
            }

            if (crane.Status == CraneStatus.Loading || crane.Status == CraneStatus.Paused || crane.Status == CraneStatus.EmergencyStop)
            {
                Log.Warning("[OrderMgr] 下发失败：鹤位 {CraneId} 状态为 {Status}，无法分配", craneId, crane.Status);
                lock (_lockObj) PendingOrders.Add(order);
                return false;
            }

            // 2. 回传SAP/ERP下发状态
            if (order.Source == OrderSource.SAP)
                await _sapService.ReportDispatchStatusAsync(orderNo, craneId, cancellationToken);
            else if (order.Source == OrderSource.ERP)
                await _erpService.ConfirmOrderCompleteAsync(orderNo, 0, craneId, cancellationToken); // 有些ERP确认接口

            // 3. 设置鹤位单据
            order.AssignedCraneId = craneId;
            order.Status = OrderStatus.Dispatched;
            order.DispatchTime = DateTime.Now;
            crane.CurrentOrder = order;
            crane.Status = CraneStatus.Ready;
            crane.RealtimeData.LoadedWeight = 0;
            crane.RealtimeData.TotalFlow = 0;
            crane.RealtimeData.Progress = 0;
            crane.RealtimeData.ElapsedSeconds = 0;
            crane.RealtimeData.RemainingWeight = order.PlannedWeight;

            // 4. 启动下位机装料
            bool startOk = await _craneManager.RemoteStartAsync(craneId);
            if (startOk)
            {
                order.Status = OrderStatus.InProgress;
                lock (_lockObj) ActiveOrders.Add(order);
                _db?.InsertOrderHistory(order);
                _db?.InsertOperationLog(new OperationLog
                {
                    Time = DateTime.Now,
                    Operator = "System",
                    Action = "DispatchOrder",
                    CraneId = craneId,
                    OrderNo = orderNo,
                    Detail = $"单据下发到鹤位 {crane.Name}，产品 {order.ProductName}，计划 {order.PlannedWeight:F0}kg"
                });
                Log.Information("[OrderMgr] 单据 {OrderNo} 成功下发到鹤位 {CraneId} 并启动装料", orderNo, craneId);
                return true;
            }
            else
            {
                Log.Warning("[OrderMgr] 下位机启动失败，单据 {OrderNo} 回滚到待下发", orderNo);
                order.Status = OrderStatus.Created;
                order.AssignedCraneId = null;
                order.DispatchTime = null;
                crane.CurrentOrder = null;
                crane.Status = CraneStatus.Idle;
                lock (_lockObj) PendingOrders.Add(order);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OrderMgr] 下发单据异常 {OrderNo}", orderNo);
            return false;
        }
    }

    public async Task<bool> CancelDispatchAsync(string orderNo, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lockObj)
            {
                var order = ActiveOrders.FirstOrDefault(o => o.OrderNo == orderNo);
                if (order == null) return false;
                ActiveOrders.Remove(order);
                if (!string.IsNullOrEmpty(order.AssignedCraneId))
                {
                    var crane = _craneManager.GetCrane(order.AssignedCraneId);
                    crane?.ResetCrane();
                }
                order.Status = OrderStatus.Cancelled;
                CompletedOrders.Insert(0, order);
            }
            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OrderMgr] 取消单据异常 {OrderNo}", orderNo);
            return false;
        }
    }

    public async Task<bool> NotifyOrderCompletedAsync(string craneId, double actualWeight, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("[OrderMgr] 通知鹤位 {CraneId} 完成，实际重量 {Weight:F2}kg", craneId, actualWeight);

            var crane = _craneManager.GetCrane(craneId);
            if (crane?.CurrentOrder == null)
            {
                Log.Warning("[OrderMgr] 完成通知找不到对应单据 {CraneId}", craneId);
                return false;
            }

            var order = crane.CurrentOrder;
            order.ActualWeight = actualWeight;
            order.Status = OrderStatus.Completed;
            order.CompleteTime = endTime;

            // 回传SAP/ERP
            if (order.Source == OrderSource.SAP)
                await _sapService.ReportCompletionAsync(order.OrderNo, actualWeight, startTime, endTime, craneId, cancellationToken);
            else if (order.Source == OrderSource.ERP)
                await _erpService.ConfirmOrderCompleteAsync(order.OrderNo, actualWeight, craneId, cancellationToken);

            lock (_lockObj)
            {
                if (ActiveOrders.Contains(order))
                    ActiveOrders.Remove(order);
                CompletedOrders.Insert(0, order);
            }

            // 清理鹤位
            crane.Status = CraneStatus.Completed;
            _db?.UpdateOrderStatus(order.OrderNo, OrderStatus.Completed.ToString(), actualWeight, endTime);
            _db?.InsertOperationLog(new OperationLog
            {
                Time = DateTime.Now,
                Operator = "System",
                Action = "OrderCompleted",
                CraneId = craneId,
                OrderNo = order.OrderNo,
                Detail = $"装车完成，实际 {actualWeight:F2}kg"
            });
            Log.Information("[OrderMgr] 单据 {OrderNo} 完成处理结束", order.OrderNo);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OrderMgr] 完成处理异常 {CraneId}", craneId);
            return false;
        }
    }

    public LoadingOrder CreateManualOrder(string customerName, string vehicleNo, string productCode,
        string productName, double plannedWeight, double? tolerance = null)
    {
        var order = new LoadingOrder
        {
            OrderNo = $"MAN{DateTime.Now:yyyyMMddHHmmss}",
            Source = OrderSource.Manual,
            Status = OrderStatus.Created,
            CreateTime = DateTime.Now,
            CustomerName = customerName,
            VehicleNo = vehicleNo,
            ProductCode = productCode,
            ProductName = productName,
            PlannedWeight = plannedWeight,
            AllowedTolerance = tolerance ?? plannedWeight * 0.003
        };
        lock (_lockObj) PendingOrders.Add(order);
        Log.Information("[OrderMgr] 手工创建单据 {OrderNo}", order.OrderNo);
        return order;
    }

    public LoadingOrder? FindOrder(string orderNo)
    {
        lock (_lockObj)
        {
            return PendingOrders.FirstOrDefault(o => o.OrderNo == orderNo)
                   ?? ActiveOrders.FirstOrDefault(o => o.OrderNo == orderNo)
                   ?? CompletedOrders.FirstOrDefault(o => o.OrderNo == orderNo);
        }
    }
}
