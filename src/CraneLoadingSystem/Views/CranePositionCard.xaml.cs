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
        if (d is CranePositionCard card)
        {
            // 退订旧鹤位的 PropertyChanged，避免内存泄漏与重复回调。
            // 原实现仅订阅新值、从不退订旧值，导致鹤位切换或置 null 时旧实例仍被卡片
            // 持有引用（泄漏），且旧鹤位状态变化仍会触发本卡片的 Crane_PropertyChanged。
            if (e.OldValue is CranePosition oldCrane)
                oldCrane.PropertyChanged -= card.Crane_PropertyChanged;

            if (e.NewValue is CranePosition newCrane)
            {
                card.DataContext = newCrane;
                newCrane.PropertyChanged += card.Crane_PropertyChanged;
            }
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

    /// <summary>
    /// 鹤位控制按钮统一执行入口。按 action 分流到 5 个分支：
    /// StartCommand（启动，含装车前人员二次确认）/ StopCommand（停止，回传实际量）
    /// / PauseCommand（暂停或恢复，恢复前重新校验联锁）/ ResetCommand（急停复位，成功才清状态）
    /// / EmergencyStopCommand（紧急停止）。失败均有 UI 弹窗反馈。
    /// </summary>
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
                    if (Crane.CurrentOrder == null)
                    {
                        MessageBox.Show($"鹤位 {Crane.Name} 尚未分配单据，请先在单据管理中下发单据到鹤位。",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (Crane.Status != CraneStatus.Ready && Crane.Status != CraneStatus.Idle)
                    {
                        MessageBox.Show($"鹤位 {Crane.Name} 当前状态为 {Crane.Status}，无法启动。仅 Ready/Idle 状态可启动。",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    // ★ Bug fix: 装车前必须由现场人员二次确认（车辆停稳、鹤管连接、静电夹接地、
                    // 阻车器升起、人员撤离至安全区）——之前下发即自动启动是重大安全漏洞
                    var startConfirm = MessageBox.Show(
                        $"【装车前安全确认】鹤位 {Crane.Name}\n\n" +
                        "请现场操作员确认以下事项已完成：\n" +
                        "  ✓ 槽车停稳并刹车\n" +
                        "  ✓ 鹤管已连接并密封\n" +
                        "  ✓ 静电夹已接地\n" +
                        "  ✓ 阻车器已升起\n" +
                        "  ✓ 现场人员已撤离至安全区\n" +
                        "  ✓ 8项安全联锁全部满足\n\n" +
                        "确认后系统将再次自动校验联锁，通过后立即启动装料。\n\n" +
                        "是否确认启动？",
                        "装车前人员确认", MessageBoxButton.YesNo, MessageBoxImage.Question,
                        MessageBoxResult.No);
                    if (startConfirm != MessageBoxResult.Yes) return;

                    ok = await _craneManager.RemoteStartAsync(craneId);
                    if (!ok)
                    {
                        // RemoteStartAsync 内部已校验联锁并报警，此处仅提示用户
                        MessageBox.Show($"启动失败：{Crane.AlarmMessage ?? "请查看报警信息与日志"}",
                            "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    break;

                case nameof(StopCommand):
                    var result = MessageBox.Show($"确认停止鹤位 {Crane.Name} 的装料作业？\n（停止后将以当前装载量作为实际完成量回传 SAP/ERP）",
                        "确认停止", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                    {
                        // 恢复前需人员再次确认（暂停期间现场可能变更）
                        var resumeConfirm = MessageBox.Show(
                            $"确认恢复鹤位 {Crane.Name} 的装料作业？\n系统将重新校验8项安全联锁。",
                            "恢复确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (resumeConfirm != MessageBoxResult.Yes) return;
                        ok = await _craneManager.RemoteResumeAsync(craneId);
                        // ★ Bug fix: 恢复失败必须弹窗反馈，否则用户感知"按钮没反应"
                        // RemoteResumeAsync 内部已校验联锁并写 crane.AlarmMessage，此处兜底提示
                        if (!ok)
                        {
                            MessageBox.Show($"恢复失败：{Crane.AlarmMessage ?? "安全联锁未满足，请现场排查后重试"}",
                                "恢复失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                    else
                    {
                        ok = await _craneManager.RemotePauseAsync(craneId);
                        if (!ok)
                            MessageBox.Show($"暂停失败：{Crane.AlarmMessage ?? "请查看日志或现场确认设备状态"}",
                                "暂停失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    break;

                case nameof(ResetCommand):
                    var res = MessageBox.Show(
                        $"确认复位鹤位 {Crane.Name}？\n\n" +
                        "复位前请确保：\n" +
                        "  ✓ 现场急停按钮已物理释放\n" +
                        "  ✓ 故障/报警原因已排查\n" +
                        "  ✓ 8项安全联锁现场已确认\n\n" +
                        "复位将重新全检8项联锁，通过后状态恢复为待机。",
                        "确认复位", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (res != MessageBoxResult.Yes) return;
                    // ★ Bug fix: 删除原"完成后走一次 NotifyOrderCompletedAsync 保险调用"。
                    //   原逻辑企图补救 RefreshTimer 漏报，但实测产生两个问题：
                    //   (1) 与自动完成路径并发，曾触发 SAP/ERP 双重记账
                    //       （现已在 NotifyOrderCompletedAsync 内加幂等守卫兜底，但仍是冗余调用）
                    //   (2) 把"复位"和"完成回传"两件事耦合在一起，意图模糊
                    //   正常完成回传唯一入口是 RefreshTimer→OnCraneCompleted→MainWindow.CraneMgr_OnCraneCompleted；
                    //   若需补偿，应统一在事件订阅侧处理，而非在 UI 按钮里偷偷再调一次。
                    ok = await _craneManager.EmergencyResetAsync(craneId);
                    // ★ Bug fix: 之前无条件执行 ResetCrane()，即使复位失败也会清状态，
                    // 导致 IsEmergencyStop 残留 + UI 显示 Idle 但 PLC 仍急停
                    if (ok)
                        Crane.ResetCrane();
                    else
                        MessageBox.Show($"复位失败：{Crane.AlarmMessage ?? "请现场确认急停按钮已释放、联锁已恢复"}",
                            "复位失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;

                case nameof(EmergencyStopCommand):
                    var r = MessageBox.Show($"⚠ 确认对鹤位 {Crane.Name} 执行紧急停止？\n这将立即切断阀门、停止泵！",
                        "紧急停止确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (r != MessageBoxResult.Yes) return;
                    ok = await _craneManager.EmergencyStopAsync(craneId);
                    // ★ CraneManagerService.EmergencyStopAsync 已同步更新 crane.Status 和 IsEmergencyStop
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
