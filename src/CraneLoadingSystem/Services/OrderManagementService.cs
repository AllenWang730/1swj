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
                // ★ 下发即确认接口（status=DISPATCHED），用命名参数绕过 ConfirmOrderCompleteAsync 新增的 status 参数
                await _erpService.ConfirmOrderCompleteAsync(orderNo, 0, craneId, status: "DISPATCHED", cancellationToken: cancellationToken);

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

            // 4. ★ Bug fix: 不再自动启动装料。仅完成单据下发，鹤位保持 Ready 状态。
            //    现场操作员需在鹤位卡片点【▶ 启动】按钮，弹窗确认车辆/鹤管/静电夹/阻车器/
            //    人员撤离等现场条件已就绪后，由 CranePositionCard.StartCommand 调用
            //    CraneManagerService.RemoteStartAsync（内部会强制校验8项安全联锁）。
            //    之前在此处直接 RemoteStartAsync 是重大安全漏洞——车辆未到位即装料可能溢料伤人。
            lock (_lockObj) ActiveOrders.Add(order);
            _db?.InsertOrderHistory(order);
            _db?.InsertOperationLog(new OperationLog
            {
                Time = DateTime.Now,
                Operator = "System",
                Action = "DispatchOrder",
                CraneId = craneId,
                OrderNo = orderNo,
                Detail = $"单据下发到鹤位 {crane.Name}，产品 {order.ProductName}，计划 {order.PlannedWeight:F0}kg；等待现场确认后启动"
            });
            Log.Information("[OrderMgr] 单据 {OrderNo} 已下发到鹤位 {CraneId}，状态=Ready，等待现场人员确认后启动装料",
                orderNo, craneId);
            return true;
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
            LoadingOrder? orderToCancel = null;
            lock (_lockObj)
            {
                orderToCancel = ActiveOrders.FirstOrDefault(o => o.OrderNo == orderNo);
                if (orderToCancel == null) return false;
                ActiveOrders.Remove(orderToCancel);
                if (!string.IsNullOrEmpty(orderToCancel.AssignedCraneId))
                {
                    var crane = _craneManager.GetCrane(orderToCancel.AssignedCraneId);
                    crane?.ResetCrane();
                }
                orderToCancel.Status = OrderStatus.Cancelled;
                CompletedOrders.Insert(0, orderToCancel);
            }

            // P2 fix: 取消单据后回传 SAP/ERP（原代码遗漏，导致 SAP 侧仍显示 Dispatched 状态，账实不符）
            try
            {
                if (orderToCancel.Source == OrderSource.SAP)
                    await _sapService.ReportExceptionAsync(orderToCancel.OrderNo, "CANCELLED", "单据取消");
                else if (orderToCancel.Source == OrderSource.ERP)
                    await _erpService.ReportExceptionAsync(orderToCancel.OrderNo, "CANCELLED", "单据取消");
            }
            catch (Exception exSap)
            {
                Log.Warning(exSap, "[OrderMgr] 取消单据 {OrderNo} 回传 SAP/ERP 失败（非致命）", orderNo);
            }

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

            // ★ Bug fix: 幂等守卫。自动完成路径（RefreshTimer→OnCraneCompleted）+ 手动 Reset
            // "保险"路径（已删）曾导致此方法被调两次，SAP/ERP 双重记账 + CompletedOrders 重复条目
            if (order.Status == OrderStatus.Completed)
            {
                Log.Information("[OrderMgr] 单据 {OrderNo} 已是 Completed 状态，跳过重复回传", order.OrderNo);
                return true;
            }

            order.ActualWeight = actualWeight;
            order.Status = OrderStatus.Completed;
            order.CompleteTime = endTime;

            // 回传SAP/ERP
            // ★ 正常完成路径：status 走默认 "COMPLETED"，用命名参数显式传 cancellationToken
            if (order.Source == OrderSource.SAP)
                await _sapService.ReportCompletionAsync(order.OrderNo, actualWeight, startTime, endTime, craneId, cancellationToken: cancellationToken);
            else if (order.Source == OrderSource.ERP)
                await _erpService.ConfirmOrderCompleteAsync(order.OrderNo, actualWeight, craneId, cancellationToken: cancellationToken);

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

    public async Task<bool> NotifyOrderAbortedAsync(string craneId, double actualWeight, DateTime startTime, DateTime endTime, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Warning("[OrderMgr] 鹤位 {CraneId} 异常中断，已装 {Weight:F2}kg，原因：{Reason}",
                craneId, actualWeight, reason);

            var crane = _craneManager.GetCrane(craneId);
            if (crane?.CurrentOrder == null)
            {
                Log.Warning("[OrderMgr] 异常中断通知找不到对应单据 {CraneId}", craneId);
                return false;
            }

            var order = crane.CurrentOrder;

            // 幂等守卫：异常中断回调仅在订单处于"活跃状态"时生效。
            // 已 Completed（正常完成）或已 Cancelled（重复异常回调）都直接跳过，避免覆盖 SAP/ERP 侧状态。
            if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed)
            {
                Log.Information("[OrderMgr] 单据 {OrderNo} 状态 {Status}，跳过重复异常回传",
                    order.OrderNo, order.Status);
                return true;
            }

            order.ActualWeight = actualWeight;
            order.Status = OrderStatus.Cancelled;  // 异常中断 → Cancelled（部分完成）
            order.CompleteTime = endTime;

            // 回传 SAP/ERP 部分完成量 + 中断原因
            // ★ Bug fix: status="ABORTED"。之前不传，SAP/ERP 侧收到 "COMPLETED" 误以为正常完成
            if (order.Source == OrderSource.SAP)
                await _sapService.ReportCompletionAsync(order.OrderNo, actualWeight, startTime, endTime, craneId, "ABORTED", cancellationToken);
            else if (order.Source == OrderSource.ERP)
                await _erpService.ConfirmOrderCompleteAsync(order.OrderNo, actualWeight, craneId, "ABORTED", cancellationToken);

            lock (_lockObj)
            {
                if (ActiveOrders.Contains(order))
                    ActiveOrders.Remove(order);
                CompletedOrders.Insert(0, order);
            }

            // ★ Bug fix: 必须清空 crane.CurrentOrder。否则异常中断后订单已 Cancelled 但鹤位仍持引用，
            //   用户若复位→重新启动，RefreshTimer 会再次触发 OnCraneCompleted(IsAborted=false)，
            //   NotifyOrderCompletedAsync 的幂等守卫只挡 Completed 不挡 Cancelled，会把订单从 Cancelled
            //   改回 Completed 并二次回传 SAP/ERP，覆盖第一次部分量，账实彻底混乱。
            //   正确做法：异常中断=订单作废，鹤位复位后必须重新下发新订单才能再次启动。
            crane.CurrentOrder = null;

            _db?.UpdateOrderStatus(order.OrderNo, OrderStatus.Cancelled.ToString(), actualWeight, endTime);
            _db?.InsertOperationLog(new OperationLog
            {
                Time = DateTime.Now,
                Operator = "System",
                Action = "OrderAborted",
                CraneId = craneId,
                OrderNo = order.OrderNo,
                Detail = $"异常中断：{reason}；已装 {actualWeight:F2}kg / 计划 {order.PlannedWeight:F2}kg"
            });
            Log.Information("[OrderMgr] 单据 {OrderNo} 异常中断处理结束（已转 Cancelled，部分量已回传，鹤位订单引用已清空）", order.OrderNo);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OrderMgr] 异常中断处理异常 {CraneId}", craneId);
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
