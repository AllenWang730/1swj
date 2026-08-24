using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CraneLoadingSystem.Models;
using CraneLoadingSystem.Services;
using CraneLoadingSystem.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace CraneLoadingSystem;

/// <summary>
/// App.xaml 交互逻辑 - 负责依赖注入、日志、全局异常、单实例启动
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private const string UniqueMutexName = "CraneLoadingSystem-{8F2A40B6-5D1C-4E8F-9A7B-1C2D3E4F5A6B}";

    public static IServiceProvider Services { get; private set; } = null!;
    public static AppConfig AppConfig { get; private set; } = null!;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // === 1. 单实例限制 ===
        EnsureSingleInstance();

        try
        {
            // === 2. 构建配置（可选：无配置文件也能运行，使用模型默认值） ===
            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            IConfiguration configuration = configBuilder.Build();

            AppConfig = new AppConfig();
            configuration.Bind(AppConfig);

            // 若未配置鹤位且无配置文件，使用默认4个鹤位
            if (AppConfig.CranePositions.Count == 0)
            {
                AppConfig.CranePositions = new List<CranePositionConfig>
                {
                    new() { Id = "CP001", Name = "1#鹤位", ProductName = "汽油",   MaxFlowRate = 300, PlcAddress = 1 },
                    new() { Id = "CP002", Name = "2#鹤位", ProductName = "柴油",   MaxFlowRate = 280, PlcAddress = 2 },
                    new() { Id = "CP003", Name = "3#鹤位", ProductName = "煤油",   MaxFlowRate = 250, PlcAddress = 3 },
                    new() { Id = "CP004", Name = "4#鹤位", ProductName = "液化气", MaxFlowRate = 200, PlcAddress = 4 }
                };
            }

            // === 3. 初始化日志（不依赖配置文件中的 Serilog 段） ===
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    path: "logs/log-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            Log.Information("========== 流体装卸鹤位上位机系统启动 ==========");
            Log.Information("[App] 运行目录: {Dir}", Directory.GetCurrentDirectory());
            Log.Information("[App] 系统名: {Name}, 鹤位数: {Count}, 仿真: {Sim}",
                AppConfig.AppSettings.SystemName, AppConfig.CranePositions.Count, AppConfig.AppSettings.EnableSimulation);
            Log.Information("[App] PLC地址: {Ip}:{Port}, 模式: {Mode}",
                AppConfig.PlcSettings.IpAddress, AppConfig.PlcSettings.Port, AppConfig.PlcSettings.Mode);

            // === 4. 依赖注入容器 ===
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection, configuration);
            Services = serviceCollection.BuildServiceProvider();

            Log.Information("[App] 依赖注入容器构建完成");

            // === 5. 显示主窗口 ===
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow(mainVm);
            MainWindow = mainWindow;
            mainWindow.Show();

            Log.Information("[App] 主窗口已显示，启动流程完成");
        }
        catch (Exception ex)
        {
            // 尝试记录日志（可能日志尚未初始化）
            try { Log.Fatal(ex, "[App] 启动失败"); } catch { }
            MessageBox.Show(
                "系统启动失败：\n\n" + ex.Message + "\n\n详细错误已记录到 logs/ 目录。",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            try { Log.CloseAndFlush(); } catch { }
            Shutdown(-1);
        }
    }

    /// <summary>
    /// 注册所有服务
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // 配置选项
        services.Configure<AppConfig>(configuration);

        // 配置HttpClient
        services.AddHttpClient<ISapService, SapService>();
        services.AddHttpClient<IErpService, ErpService>();

        // 单例：核心服务
        services.AddSingleton<IPlcControlService, PlcControlService>();
        services.AddSingleton<IAlarmManagerService, AlarmManagerService>();
        services.AddSingleton<ISafetyInterlockService, SafetyInterlockService>();
        services.AddSingleton<ICraneManagerService, CraneManagerService>();
        services.AddSingleton<IOrderManagementService, OrderManagementService>();
        services.AddSingleton<ISapService, SapService>();
        services.AddSingleton<IErpService, ErpService>();

        // View / ViewModel（每次解析创建新实例，支持多开对话框）
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DispatchDialogViewModel>();
        services.AddTransient<CranePositionCard>();
        services.AddTransient<DispatchOrderDialog>();
        services.AddTransient<ManualOrderDialog>();
    }

    /// <summary>
    /// 单实例互斥锁
    /// </summary>
    private static void EnsureSingleInstance()
    {
        bool createdNew;
        _singleInstanceMutex = new Mutex(true, UniqueMutexName, out createdNew);
        if (!createdNew)
        {
            // 尝试激活已经在运行的实例
            try
            {
                [DllImport("user32.dll")]
                static extern bool SetForegroundWindow(IntPtr hWnd);
                [DllImport("user32.dll")]
                static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

                var hWnd = FindWindow(null, "流体装卸鹤位上位机监控系统 v1.0");
                if (hWnd != IntPtr.Zero) SetForegroundWindow(hWnd);
            }
            catch { /* ignore */ }

            MessageBox.Show("系统已在运行中！请查看任务栏或通知区域。",
                "重复启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// 全局UI线程未捕获异常处理
    /// </summary>
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "[App] UI未捕获异常");
        var result = MessageBox.Show(
            $"系统发生未处理异常：\n\n{e.Exception.Message}\n\n是否继续运行？\n\n（详细错误见日志 logs/）",
            "严重错误",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Error);

        if (result == MessageBoxResult.Yes)
        {
            e.Handled = true;
        }
        else if (result == MessageBoxResult.No)
        {
            e.Handled = false;
            try { Log.CloseAndFlush(); } catch { }
            Shutdown(-2);
        }
        else
        {
            e.Handled = true;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Log.Information("[App] 程序退出，Code={Code}", e.ApplicationExitCode);
            Log.CloseAndFlush();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { /* ignore */ }
        base.OnExit(e);
    }
}