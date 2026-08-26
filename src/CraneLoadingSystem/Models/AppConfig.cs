using CommunityToolkit.Mvvm.ComponentModel;

namespace CraneLoadingSystem.Models;

/// <summary>
/// 系统配置根节点 - 与 appsettings.json 结构一一对应，启动时由 IOptions<AppConfig> 注入
/// </summary>
public class AppConfig
{
    /// <summary>应用通用设置（系统名/刷新间隔/仿真开关）</summary>
    public AppSettings AppSettings { get; set; } = new();
    /// <summary>SAP OData 接口配置</summary>
    public SapSettings SapSettings { get; set; } = new();
    /// <summary>ERP REST 接口配置</summary>
    public ErpSettings ErpSettings { get; set; } = new();
    /// <summary>PLC/下位机通讯配置</summary>
    public PlcSettings PlcSettings { get; set; } = new();
    /// <summary>RFID 读写器配置</summary>
    public RfidSettings RfidSettings { get; set; } = new();
    /// <summary>SQLite 数据库配置</summary>
    public DatabaseSettings Database { get; set; } = new();
    /// <summary>鹤位静态配置列表（与现场物理鹤位对应）</summary>
    public List<CranePositionConfig> CranePositions { get; set; } = new();
}

/// <summary>
/// 应用通用设置
/// </summary>
public class AppSettings
{
    /// <summary>系统名称（显示在标题栏与单实例检测的窗口标题）</summary>
    public string SystemName { get; set; } = "流体装卸鹤位上位机系统";
    /// <summary>默认鹤位数量（无配置时初始化用）</summary>
    public int DefaultCraneCount { get; set; } = 4;
    /// <summary>实时数据刷新间隔（毫秒），建议 500-1000</summary>
    public int DataRefreshIntervalMs { get; set; } = 1000;
    /// <summary>是否启用仿真模式（true=不连接真实 PLC/SAP/ERP）</summary>
    public bool EnableSimulation { get; set; } = true;
}

/// <summary>
/// SAP OData 接口配置
/// </summary>
public class SapSettings
{
    /// <summary>SAP OData 服务基地址</summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";
    /// <summary>OAuth 客户端 ID</summary>
    public string ClientId { get; set; } = "CRANE_LOADING";
    /// <summary>OAuth 客户端密钥</summary>
    public string ClientSecret { get; set; } = "";
    /// <summary>登录用户名</summary>
    public string UserName { get; set; } = "sapuser";
    /// <summary>登录密码</summary>
    public string Password { get; set; } = "";
    /// <summary>HTTP 超时（秒）</summary>
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>OData 服务路径（含实体集前缀）</summary>
    public string ODataServicePath { get; set; } = "/sap/opu/odata/sap/Z_CARGO_LOADING_SRV/";
}

/// <summary>
/// ERP REST 接口配置
/// </summary>
public class ErpSettings
{
    /// <summary>ERP REST API 基地址</summary>
    public string BaseUrl { get; set; } = "http://localhost:8080/api";
    /// <summary>API Key（请求头 X-API-Key）</summary>
    public string ApiKey { get; set; } = "demo-api-key";
    /// <summary>HTTP 超时（秒）</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// PLC/下位机通讯配置
/// </summary>
public class PlcSettings
{
    /// <summary>通讯模式: "TCP" 或 "RTU"</summary>
    public string Mode { get; set; } = "TCP";
    /// <summary>TCP 模式下 PLC IP 地址</summary>
    public string IpAddress { get; set; } = "127.0.0.1";
    /// <summary>TCP 模式下 PLC 端口（Modbus 默认 502）</summary>
    public int Port { get; set; } = 502;
    /// <summary>RTU 模式下串口名</summary>
    public string SerialPort { get; set; } = "COM1";
    /// <summary>RTU 模式下波特率</summary>
    public int BaudRate { get; set; } = 9600;
    /// <summary>单次请求超时（毫秒）</summary>
    public int TimeoutMs { get; set; } = 3000;
    /// <summary>失败重试次数</summary>
    public int Retries { get; set; } = 3;
    /// <summary>断线重连间隔（毫秒），指数退避</summary>
    public List<int> ReconnectIntervalMs { get; set; } = new() { 1000, 2000, 4000, 8000 };
}

/// <summary>
/// RFID 读写器配置（车辆识别）
/// </summary>
public class RfidSettings
{
    /// <summary>RFID 读写器串口名</summary>
    public string Port { get; set; } = "COM2";
    /// <summary>波特率</summary>
    public int BaudRate { get; set; } = 9600;
    /// <summary>轮询间隔（毫秒）</summary>
    public int PollingIntervalMs { get; set; } = 500;
}

/// <summary>
/// SQLite 数据库配置
/// </summary>
public class DatabaseSettings
{
    /// <summary>SQLite 连接字符串</summary>
    public string ConnectionString { get; set; } = "Data Source=crane_loading.db";
    /// <summary>历史数据保留天数（超过自动清理）</summary>
    public int RetentionDays { get; set; } = 90;
}