using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraneLoadingSystem.Models;
using CraneLoadingSystem.Services;
using Serilog;

namespace CraneLoadingSystem.Views;

/// <summary>
/// 秒转分显示转换器
/// </summary>
public class SecondsToMinutesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int sec)
        {
            int m = sec / 60;
            int s = sec % 60;
            return $"{m}:{s:00}";
        }
        if (value is double dsec)
        {
            int secInt = (int)dsec;
            return $"{secInt / 60}:{secInt % 60:00}";
        }
        return "0:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// 布尔转可见性转换器（默认：true→Visible，false→Collapsed；支持参数 Invert 反转）
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool show = invert ? !b : b;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// CranePositionCard.xaml 的交互逻辑
/// 每个鹤位一个独立卡片，可被置于主窗口的任意布局中
/// </summary>
public partial class CranePositionCard : System.Windows.Controls.UserControl, IDisposable
{
    // 声明XAML中使用的Converter资源（XAML中通过键访问）
    private static readonly SecondsToMinutesConverter _secToMinConv = new();
    private static readonly BoolToVisibilityConverter _boolToVisConv = new();

    public static readonly DependencyProperty CraneProperty =
        DependencyProperty.Register(
            nameof(Crane),
            typeof(CranePosition),
            typeof(CranePositionCard),
            new PropertyMetadata(null, OnCraneChanged));

    public CranePosition? Crane
    {
        get => (CranePosition?)GetValue(CraneProperty);
        set => SetValue(CraneProperty, value);
    }

    private static void OnCraneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CranePositionCard card && e.NewValue is CranePosition crane)
        {
            card.DataContext = crane;
            if (crane != null)
                crane.PropertyChanged += card.Crane_PropertyChanged;
            card.UpdateStatusText();
        }
    }

    private void Crane_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CranePosition.Status))
            UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        StatusText.Text = Crane?.Status switch
        {
            CraneStatus.Idle => "待机",
            CraneStatus.Ready => "就绪",
            CraneStatus.Loading => "装料中",
            CraneStatus.Paused => "已暂停",
            CraneStatus.Completed => "已完成",
            CraneStatus.Fault => "故障",
            CraneStatus.Offline => "离线",
            CraneStatus.EmergencyStop => "紧急停止",
            _ => "未知"
        };
    }

    // 控制命令（通过外部Manager服务）
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand EmergencyStopCommand { get; }

    private readonly ICraneManagerService? _craneManager;
    private readonly IOrderManagementService? _orderMgr;

    /// <summary>默认构造函数（XAML设计器需要）</summary>
    public CranePositionCard()
    {
        // 注册资源中的Converter
        Resources["SecToMinConv"] = _secToMinConv;
        Resources["BoolToVisConv"] = _boolToVisConv;

        InitializeComponent();

        StartCommand = new RelayCommand(async () => await ExecuteCraneAction(nameof(StartCommand)));
        StopCommand = new RelayCommand(async () => await ExecuteCraneAction(nameof(StopCommand)));
        PauseCommand = new RelayCommand(async () => await ExecuteCraneAction(nameof(PauseCommand)));
        ResetCommand = new RelayCommand(async () => await ExecuteCraneAction(nameof(ResetCommand)));
        EmergencyStopCommand = new RelayCommand(async () => await ExecuteCraneAction(nameof(EmergencyStopCommand)));
    }

    /// <summary>依赖注入构造函数（在运行时由容器创建）</summary>
    public CranePositionCard(ICraneManagerService craneManager, IOrderManagementService orderMgr) : this()
    {
        _craneManager = craneManager;
        _orderMgr = orderMgr;
    }

    private async System.Threading.Tasks.Task ExecuteCraneAction(string action)
    {
        if (_craneManager == null || Crane == null)
        {
            // 运行时若无DI管理器，给出提示便于调试
            Log.Warning("[CraneCard] 无法执行控制：管理器未注入或鹤位为空");
            MessageBox.Show("当前为设计/演示模式，DI未完整初始化。主窗口运行后可操作。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            string craneId = Crane.Id;
            bool ok;
            switch (action)
            {
                case nameof(StartCommand):
                    // 若鹤位未分配单据，则提示需先分配
                    if (Crane.CurrentOrder == null || Crane.Status == CraneStatus.Idle)
                    {
                        MessageBox.Show($"鹤位 {Crane.Name} 尚未分配单据，请先在单据管理中下发单据到鹤位。",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    ok = await _craneManager.RemoteStartAsync(craneId);
                    break;

                case nameof(StopCommand):
                    var result = MessageBox.Show($"确认停止鹤位 {Crane.Name} 的装料作业？",
                        "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes) return;
                    ok = await _craneManager.RemoteStopAsync(craneId);
                    // 完成后通知订单管理
                    if (ok && Crane.CurrentOrder != null)
                    {
                        await _orderMgr!.NotifyOrderCompletedAsync(craneId,
                            Crane.RealtimeData.LoadedWeight,
                            Crane.CurrentOrder.DispatchTime ?? DateTime.Now,
                            DateTime.Now);
                        Crane.ResetCrane();
                    }
                    break;

                case nameof(PauseCommand):
                    if (Crane.Status == CraneStatus.Paused)
                        ok = await _craneManager.RemoteResumeAsync(craneId);
                    else
                        ok = await _craneManager.RemotePauseAsync(craneId);
                    break;

                case nameof(ResetCommand):
                    var res = MessageBox.Show($"确认复位鹤位 {Crane.Name}，清除当前状态？",
                        "确认复位", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (res != MessageBoxResult.Yes) return;
                    if (Crane.Status == CraneStatus.Completed && Crane.CurrentOrder != null)
                    {
                        // 完成后走一次通知
                        await _orderMgr!.NotifyOrderCompletedAsync(craneId,
                            Crane.RealtimeData.LoadedWeight,
                            Crane.CurrentOrder.DispatchTime ?? DateTime.Now,
                            DateTime.Now);
                    }
                    ok = await _craneManager.EmergencyResetAsync(craneId);
                    Crane.ResetCrane();
                    break;

                case nameof(EmergencyStopCommand):
                    var r = MessageBox.Show($"⚠ 确认对鹤位 {Crane.Name} 执行紧急停止？\n这将立即切断阀门、停止泵！",
                        "紧急停止确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (r != MessageBoxResult.Yes) return;
                    ok = await _craneManager.EmergencyStopAsync(craneId);
                    break;

                default:
                    return;
            }

            if (!ok)
                Log.Warning("[CraneCard] 操作 {Action} @ {CraneId} 返回失败", action, craneId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CraneCard] 操作 {Action} 异常", action);
            MessageBox.Show("操作失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Dispose()
    {
        if (Crane != null)
            Crane.PropertyChanged -= Crane_PropertyChanged;
    }
}
