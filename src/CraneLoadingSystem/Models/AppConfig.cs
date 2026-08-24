using CommunityToolkit.Mvvm.ComponentModel;

namespace CraneLoadingSystem.Models;

/// <summary>
/// 系统配置根节点
/// </summary>
public class AppConfig
{
    public AppSettings AppSettings { get; set; } = new();
    public SapSettings SapSettings { get; set; } = new();
    public ErpSettings ErpSettings { get; set; } = new();
    public PlcSettings PlcSettings { get; set; } = new();
    public RfidSettings RfidSettings { get; set; } = new();
    public DatabaseSettings Database { get; set; } = new();
    public List<CranePositionConfig> CranePositions { get; set; } = new();
}

public class AppSettings
{
    public string SystemName { get; set; } = "流体装卸鹤位上位机系统";
    public int DefaultCraneCount { get; set; } = 4;
    public int DataRefreshIntervalMs { get; set; } = 1000;
    public bool EnableSimulation { get; set; } = true;
}

public class SapSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public string ClientId { get; set; } = "CRANE_LOADING";
    public string ClientSecret { get; set; } = "";
    public string UserName { get; set; } = "sapuser";
    public string Password { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public string ODataServicePath { get; set; } = "/sap/opu/odata/sap/Z_CARGO_LOADING_SRV/";
}

public class ErpSettings
{
    public string BaseUrl { get; set; } = "http://localhost:8080/api";
    public string ApiKey { get; set; } = "demo-api-key";
    public int TimeoutSeconds { get; set; } = 30;
}

public class PlcSettings
{
    /// <summary>通讯模式: "TCP" 或 "RTU"</summary>
    public string Mode { get; set; } = "TCP";
    public string IpAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public string SerialPort { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int TimeoutMs { get; set; } = 3000;
    public int Retries { get; set; } = 3;
    /// <summary>断线重连间隔（毫秒），指数退避</summary>
    public List<int> ReconnectIntervalMs { get; set; } = new() { 1000, 2000, 4000, 8000 };
}

public class RfidSettings
{
    public string Port { get; set; } = "COM2";
    public int BaudRate { get; set; } = 9600;
    public int PollingIntervalMs { get; set; } = 500;
}

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = "Data Source=crane_loading.db";
    public int RetentionDays { get; set; } = 90;
}