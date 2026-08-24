using CraneLoadingSystem.Models;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Net.Http.Headers;
using System.Text;

namespace CraneLoadingSystem.Services;

/// <summary>
/// SAP服务实现 - 基于SAP OData / REST Gateway
/// 生产环境需根据实际SAP OData服务配置URL格式与认证方式
/// </summary>
public class SapService : ISapService
{
    private readonly AppConfig _config;
    private readonly HttpClient _httpClient;

    public SapService(IOptions<AppConfig> config, HttpClient httpClient)
    {
        _config = config.Value;
        _httpClient = httpClient;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_config.SapSettings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.SapSettings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Basic认证 (SAP常见方式)
        if (!string.IsNullOrWhiteSpace(_config.SapSettings.UserName))
        {
            var cred = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{_config.SapSettings.UserName}:{_config.SapSettings.Password}"));
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", cred);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.AppSettings.EnableSimulation)
            {
                await Task.Delay(100, cancellationToken);
                Log.Information("[SAP] 仿真模式：连接测试成功");
                return true;
            }
            var response = await _httpClient.GetAsync(_config.SapSettings.ODataServicePath + "$metadata", cancellationToken);
            Log.Information("[SAP] 连接测试: {Code}", response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SAP] 连接测试失败");
            return false;
        }
    }

    public async Task<List<LoadingOrder>> GetLoadingOrdersAsync(bool onlyPending = true, DateTime? fromDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.AppSettings.EnableSimulation)
                return GenerateMockOrders();

            string query = _config.SapSettings.ODataServicePath
                           + "LoadingOrderSet?$filter=Status eq 'CREATED'"
                           + (fromDate.HasValue ? $" and CreateDate ge datetime'{fromDate.Value:yyyy-MM-ddTHH:mm:ss}'" : "");
            var response = await _httpClient.GetAsync(query, cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JObject.Parse(content);
            var results = json["d"]?["results"] as JArray ?? new JArray();

            var list = new List<LoadingOrder>();
            foreach (var item in results)
            {
                list.Add(ParseSapOrder(item));
            }
            Log.Information("[SAP] 获取单据 {Count} 条", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SAP] 获取单据失败");
            return _config.AppSettings.EnableSimulation ? GenerateMockOrders() : new List<LoadingOrder>();
        }
    }

    public async Task<LoadingOrder?> GetOrderDetailAsync(string orderNo, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_config.AppSettings.EnableSimulation)
            {
                var mock = GenerateMockOrders().FirstOrDefault(o => o.OrderNo == orderNo);
                return await Task.FromResult(mock);
            }

            var url = _config.SapSettings.ODataServicePath + $"LoadingOrderSet('{Uri.EscapeDataString(orderNo)}')";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JObject.Parse(content);
            return ParseSapOrder(json["d"] as JObject);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SAP] 获取单据详情失败 {OrderNo}", orderNo);
            return null;
        }
    }

    public async Task<bool> ReportDispatchStatusAsync(string orderNo, string craneId, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("[SAP] 回传下发状态: {OrderNo} -> {CraneId}", orderNo, craneId);
            if (_config.AppSettings.EnableSimulation)
            {
                await Task.Delay(100, cancellationToken);
                return true;
            }

            var payload = new
            {
                OrderNo = orderNo,
                CraneId = craneId,
                Status = "DISPATCHED",
                DispatchTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };
            var httpContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(
                _config.SapSettings.ODataServicePath + "ReportDispatchStatus", httpContent, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SAP] 回传下发状态失败 {OrderNo}", orderNo);
            return _config.AppSettings.EnableSimulation;
        }
    }

    public async Task<bool> ReportCompletionAsync(string orderNo, double actualWeight, DateTime startTime, DateTime endTime,
        string craneId, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("[SAP] 回传完成: {OrderNo}, 实际={Weight}kg, 鹤位={CraneId}", orderNo, actualWeight, craneId);
            if (_config.AppSettings.EnableSimulation)
            {
                await Task.Delay(100, cancellationToken);
                return true;
            }

            var payload = new
            {
                OrderNo = orderNo,
                ActualWeight = actualWeight,
                StartTime = startTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                EndTime = endTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                CraneId = craneId,
                Status = "COMPLETED"
            };
            var httpContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(
                _config.SapSettings.ODataServicePath + "ReportCompletion", httpContent, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SAP] 回传完成失败 {OrderNo}", orderNo);
            return _config.AppSettings.EnableSimulation;
        }
    }

    public async Task<bool> ReportExceptionAsync(string orderNo, string errorCode, string errorMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Warning("[SAP] 回传异常: {OrderNo}, {Code}:{Msg}", orderNo, errorCode, errorMessage);
            if (_config.AppSettings.EnableSimulation)
            {
                await Task.Delay(100, cancellationToken);
                return true;
            }

            var payload = new { OrderNo = orderNo, ErrorCode = errorCode, ErrorMessage = errorMessage };
            var httpContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(
                _config.SapSettings.ODataServicePath + "ReportException", httpContent, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SAP] 回传异常失败 {OrderNo}", orderNo);
            return _config.AppSettings.EnableSimulation;
        }
    }

    #region 辅助方法

    private static LoadingOrder ParseSapOrder(JToken? item)
    {
        if (item == null) return new LoadingOrder();
        return new LoadingOrder
        {
            OrderNo = item["OrderNo"]?.ToString() ?? item["Orderid"]?.ToString() ?? "",
            CustomerCode = item["CustomerCode"]?.ToString() ?? item["Kunnr"]?.ToString() ?? "",
            CustomerName = item["CustomerName"]?.ToString() ?? item["Name1"]?.ToString() ?? "",
            VehicleNo = item["VehicleNo"]?.ToString() ?? item["PlateNo"]?.ToString() ?? "",
            DriverName = item["DriverName"]?.ToString() ?? "",
            DriverPhone = item["DriverPhone"]?.ToString(),
            ProductCode = item["ProductCode"]?.ToString() ?? item["Matnr"]?.ToString() ?? "",
            ProductName = item["ProductName"]?.ToString() ?? item["Maktx"]?.ToString() ?? "",
            PlannedWeight = Convert.ToDouble(item["PlannedWeight"] ?? item["Meng"] ?? 0),
            AllowedTolerance = Convert.ToDouble(item["AllowedTolerance"] ?? 10),
            UnitPrice = Convert.ToDecimal(item["UnitPrice"] ?? 0),
            TotalAmount = Convert.ToDecimal(item["TotalAmount"] ?? 0),
            ContractNo = item["ContractNo"]?.ToString(),
            BatchNo = item["BatchNo"]?.ToString() ?? item["Charg"]?.ToString(),
            TankArea = item["TankArea"]?.ToString(),
            Remarks = item["Remarks"]?.ToString() ?? item["Remark"]?.ToString(),
            CreateTime = DateTime.TryParse(item["CreateTime"]?.ToString() ?? item["Erdat"]?.ToString(), out var dt) ? dt : DateTime.Now,
            Source = OrderSource.SAP,
            Status = OrderStatus.Created
        };
    }

    private static List<LoadingOrder> GenerateMockOrders()
    {
        var rnd = new Random(42);
        var products = new[]
        {
            new { Code = "P001", Name = "92#车用汽油", Price = 7.85m, Density = 750 },
            new { Code = "P002", Name = "0#车用柴油", Price = 7.20m, Density = 840 },
            new { Code = "P003", Name = "3#喷气燃料", Price = 6.90m, Density = 790 },
            new { Code = "P004", Name = "液化石油气LPG", Price = 5.20m, Density = 580 }
        };
        var customers = new[]
        {
            new { Code = "C10001", Name = "中石油华东销售分公司" },
            new { Code = "C10002", Name = "中石化青岛物流中心" },
            new { Code = "C10003", Name = "中海油山东运输公司" },
            new { Code = "C10004", Name = "山东京博物流股份有限公司" },
            new { Code = "C10005", Name = "青岛港联化物流有限公司" }
        };
        var plates = new[] { "鲁B12345", "鲁B67890", "鲁U23456", "鲁U89012", "鲁B34567", "鲁B90123" };
        var drivers = new[] { "张三", "李四", "王五", "赵六", "陈七", "刘八" };

        var orders = new List<LoadingOrder>();
        DateTime today = DateTime.Today;

        for (int i = 1; i <= 8; i++)
        {
            var prod = products[i % products.Length];
            var cust = customers[i % customers.Length];
            double weight = (15000 + rnd.Next(30) * 1000);
            orders.Add(new LoadingOrder
            {
                OrderNo = $"SAP{DateTime.Now:yyyyMMdd}{i:0000}",
                Source = OrderSource.SAP,
                Status = OrderStatus.Created,
                CreateTime = today.AddHours(7 + i),
                CustomerCode = cust.Code,
                CustomerName = cust.Name,
                VehicleNo = plates[i % plates.Length],
                DriverName = drivers[i % drivers.Length],
                DriverPhone = "138" + rnd.Next(10000000, 99999999).ToString(),
                ProductCode = prod.Code,
                ProductName = prod.Name,
                PlannedWeight = weight,
                AllowedTolerance = weight * 0.003,
                UnitPrice = prod.Price,
                TotalAmount = (decimal)weight * prod.Price,
                ContractNo = $"HT{DateTime.Today:yyyyMMdd}-{rnd.Next(100, 999)}",
                BatchNo = $"B{DateTime.Today:MMdd}-{i:00}",
                TankArea = $"TANK-{(i % 4) + 1}区",
                Remarks = i % 3 == 0 ? "需防静电接地检查" : null
            });
        }
        return orders;
    }

    #endregion
}
