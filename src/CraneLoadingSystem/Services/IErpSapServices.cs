using CraneLoadingSystem.Models;

namespace CraneLoadingSystem.Services;

/// <summary>
/// SAP对接服务接口
/// 负责从SAP获取装料单据、回传装料结果、库存查询等
/// </summary>
public interface ISapService
{
    /// <summary>测试SAP连接</summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>获取待处理装料单据列表</summary>
    /// <param name="onlyPending">仅取未下发的单据</param>
    /// <param name="fromDate">起始日期筛选</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<LoadingOrder>> GetLoadingOrdersAsync(bool onlyPending = true, DateTime? fromDate = null, CancellationToken cancellationToken = default);

    /// <summary>根据单据号获取单据详情</summary>
    Task<LoadingOrder?> GetOrderDetailAsync(string orderNo, CancellationToken cancellationToken = default);

    /// <summary>回传装料状态（下发开始）</summary>
    Task<bool> ReportDispatchStatusAsync(string orderNo, string craneId, CancellationToken cancellationToken = default);

    /// <summary>回传装料完成结果</summary>
    /// <param name="status">回传状态码：默认 "COMPLETED"；异常中断回传部分量时传 "ABORTED"</param>
    Task<bool> ReportCompletionAsync(string orderNo, double actualWeight, DateTime startTime, DateTime endTime,
        string craneId, string status = "COMPLETED", CancellationToken cancellationToken = default);

    /// <summary>回传装料异常</summary>
    Task<bool> ReportExceptionAsync(string orderNo, string errorCode, string errorMessage, CancellationToken cancellationToken = default);
}

/// <summary>
/// ERP对接服务接口
/// 通用ERP REST接口，可对接用友、金蝶等系统
/// </summary>
public interface IErpService
{
    /// <summary>测试ERP连接</summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>从ERP获取待装料单据</summary>
    Task<List<LoadingOrder>> GetPendingOrdersAsync(DateTime? fromDate = null, CancellationToken cancellationToken = default);

    /// <summary>从ERP获取客户信息</summary>
    Task<CustomerInfo?> GetCustomerInfoAsync(string customerCode, CancellationToken cancellationToken = default);

    /// <summary>从ERP获取产品/物料信息</summary>
    Task<ProductInfo?> GetProductInfoAsync(string productCode, CancellationToken cancellationToken = default);

    /// <summary>回传单号完成信息</summary>
    /// <param name="status">回传状态码：默认 "COMPLETED"；异常中断回传部分量时传 "ABORTED"</param>
    Task<bool> ConfirmOrderCompleteAsync(string orderNo, double actualWeight, string craneId,
        string status = "COMPLETED", CancellationToken cancellationToken = default);

    /// <summary>
    /// 回传单号异常/取消状态
    /// </summary>
    /// <param name="errorCode">异常码，例如 "CANCELLED"（操作员取消）、"ABORTED"（急停中断）、"DISPATCH_FAIL"（下发失败）</param>
    /// <param name="errorMessage">人类可读的异常描述</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> ReportExceptionAsync(string orderNo, string errorCode, string errorMessage, CancellationToken cancellationToken = default);
}

public class CustomerInfo
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? TaxNo { get; set; }
}

public class ProductInfo
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Specification { get; set; }
    public string? Unit { get; set; } = "kg";
    public decimal DefaultPrice { get; set; }
    public double StandardDensity { get; set; } = 750;
}
