using CraneLoadingSystem.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Net.Http.Headers;
using System.Text;

namespace CraneLoadingSystem.Services;

/// <summary>
/// ERP服务实现（通用REST API）
/// </summary>
public class ErpService : IErpService
{
    private readonly AppConfig _config;
    private readonly HttpClient _httpClient;

    public ErpService(IOptions<AppConfig> config, HttpClient httpClient)
    {
        _config = config.Value;
        _httpClient = httpClient;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_config.ErpSettings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.ErpSettings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_config.ErpSettings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _config.ErpSettings.ApiKey);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.AppSettings.EnableSimulation)
            {
                await Task.Delay(80, cancellationToken);
                Log.Information("[ERP] 仿真模式：连接测试成功");
                return true;
            }
            var response = await _httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ERP] 连接测试失败");
            return false;
        }
    }

    public async Task<List<LoadingOrder>> GetPendingOrdersAsync(DateTime? fromDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.AppSettings.EnableSimulation)
            {
                return GenerateMockErpOrders();
            }

            string url = $"/api/orders/pending" + (fromDate.HasValue ? $"?from={fromDate.Value:yyyy-MM-dd}" : "");
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonConvert.DeserializeObject<List<LoadingOrder>>(content) ?? new List<LoadingOrder>();
            foreach (var o in list) o.Source = OrderSource.ERP;
            Log.Information("[ERP] 获取待下发单据 {Count} 条", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ERP] 获取待下发单据失败");
            return _config.AppSettings.EnableSimulation ? GenerateMockErpOrders() : new List<LoadingOrder>();
        }
    }

    public async Task<CustomerInfo?> GetCustomerInfoAsync(string customerCode, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.AppSettings.EnableSimulation)
            {
                return await Task.FromResult(new CustomerInfo
                {
                    Code = customerCode,
                    Name = "客户_" + customerCode,
                    ContactPerson = "联系人",
                    Phone = "13800000000"
                });
            }

            var response = await _httpClient.GetAsync($"/api/customers/{customerCode}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonConvert.DeserializeObject<CustomerInfo>(content);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ERP] 获取客户信息失败 {Code}", customerCode);
            return null;
        }
    }

    public async Task<ProductInfo?> GetProductInfoAsync(string productCode, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.AppSettings.EnableSimulation)
            {
                return await Task.FromResult(new ProductInfo
                {
                    Code = productCode,
                    Name = "产品_" + productCode,
                    Unit = "kg",
                    StandardDensity = 750
                });
            }

            var response = await _httpClient.GetAsync($"/api/products/{productCode}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonConvert.DeserializeObject<ProductInfo>(content);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ERP] 获取产品信息失败 {Code}", productCode);
            return null;
        }
    }

    public async Task<bool> ConfirmOrderCompleteAsync(string orderNo, double actualWeight, string craneId,
        string status = "COMPLETED", CancellationToken cancellationToken = default)
    {
        try
        {
            // ★ Bug fix: status 由调用方传入。正常完成 "COMPLETED"，急停中断回传部分量 "ABORTED"。
            //   之前 ERP 侧 payload 没带 status 字段，无法区分正常完成与异常中断。
            Log.Information("[ERP] 回传完成单据: {OrderNo}, 实际={Weight}kg, 鹤位={CraneId}, 状态={Status}",
                orderNo, actualWeight, craneId, status);
            if (_config.AppSettings.EnableSimulation)
            {
                await Task.Delay(80, cancellationToken);
                return true;
            }

            var payload = new
            {
                orderNo,
                actualWeight,
                craneId,
                status,
                completedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/api/orders/complete", content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ERP] 回传完成失败 {OrderNo}", orderNo);
            return _config.AppSettings.EnableSimulation;
        }
    }

    private static List<LoadingOrder> GenerateMockErpOrders()
    {
        var rnd = new Random(99);
        var orders = new List<LoadingOrder>();
        DateTime today = DateTime.Today;
        string[] products = { "P001", "P002", "P003" };
        string[] productNames = { "92#汽油", "0#柴油", "航空煤油" };

        for (int i = 1; i <= 5; i++)
        {
            int idx = (i - 1) % products.Length;
            double weight = 18000 + rnd.Next(1, 20) * 1000;
            orders.Add(new LoadingOrder
            {
                OrderNo = $"ERP{DateTime.Now:yyyyMMdd}{i + 100:0000}",
                Source = OrderSource.ERP,
                Status = OrderStatus.Created,
                CreateTime = today.AddHours(8 + i * 0.5),
                CustomerCode = "ERP-C" + (100 + i),
                CustomerName = "ERP客户_" + i,
                VehicleNo = "鲁A" + rnd.Next(10000, 99999),
                DriverName = "司机" + i,
                ProductCode = products[idx],
                ProductName = productNames[idx],
                PlannedWeight = weight,
                AllowedTolerance = 50,
                UnitPrice = 7 + (decimal)rnd.NextDouble() * 1.5m,
                ContractNo = $"ERP-CN{i:000}"
            });
        }
        return orders;
    }
}
