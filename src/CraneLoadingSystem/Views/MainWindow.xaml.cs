using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CraneLoadingSystem.Models;
using CraneLoadingSystem.Services;
using Serilog;

namespace CraneLoadingSystem.Views;

/// <summary>
/// 主窗口ViewModel
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private string _appTitle = "流体装卸鹤位上位机监控系统 v1.0";
    [ObservableProperty] private string _statusBarText = "系统启动中...";

    public ICraneManagerService CraneManager { get; }
    public IOrderManagementService OrderManager { get; }
    public ISapService SapService { get; }
    public IErpService ErpService { get; }
    public IServiceProvider ServiceProvider { get; }

    public MainWindowViewModel(ICraneManagerService craneManager, IOrderManagementService orderManager,
        ISapService sapService, IErpService erpService, IServiceProvider serviceProvider)
    {
        CraneManager = craneManager;
        OrderManager = orderManager;
        SapService = sapService;
        ErpService = erpService;
        ServiceProvider = serviceProvider;
    }
}

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly DateTime _startTime = DateTime.Now;
    private int _layoutMode = 0; // 0:Wrap 1:2列 2:1列

    public MainWindow(MainWindowViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (s, e) =>
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            var span = DateTime.Now - _startTime;
            RunTimeText.Text = $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
        };
        _clockTimer.Start();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += StatusTimer_Tick;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Log.Information("[Main] 窗口加载 - 开始初始化系统");
            _vm.StatusBarText = "正在连接PLC并初始化鹤位...";

            // 1. 初始化鹤位管理器
            await _vm.CraneManager.InitializeAsync();
            PlcConnEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x28, 0xA7, 0x45));
            PlcConnText.Text = "PLC 已连接";
            PlcConnText.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));

            // 自动订阅鹤位完成通知 -> 触发订单完成回传
            if (_vm.CraneManager is CraneManagerService cm)
            {
                cm.OnCraneCompleted += CraneMgr_OnCraneCompleted;
            }

            // 2. 测试SAP/ERP连通性
            _vm.StatusBarText = "正在检测SAP/ERP连接...";
            bool sapOk = await _vm.SapService.TestConnectionAsync();
            bool erpOk = await _vm.ErpService.TestConnectionAsync();
            UpdateSapStatus(sapOk, erpOk);

            // 3. 刷新单据
            _vm.StatusBarText = "正在从SAP/ERP同步单据...";
            int newCount = await _vm.OrderManager.RefreshOrdersFromSourceAsync();

            // 4. 构建鹤位卡片
            BuildCraneCards();

            // 5. 绑定订单Tab
            UpdateOrdersGridBinding(0);
            OrderTabs.SelectedIndex = 0;

            // 6. 启动状态轮询
            _statusTimer.Start();

            _vm.StatusBarText = $"系统就绪  ·  鹤位: {_vm.CraneManager.Cranes.Count}个  ·  新单据: {newCount}份  ·  仿真模式";
            Log.Information("[Main] 系统初始化完成，鹤位{Count}个，新单据{NewOrders}份",
                _vm.CraneManager.Cranes.Count, newCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Main] 初始化异常");
            MessageBox.Show(this, "系统初始化异常：\n" + ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            _vm.StatusBarText = "初始化失败：" + ex.Message;
        }
    }

    private async void CraneMgr_OnCraneCompleted(object? sender, CraneCompletedArgs e)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (e.IsAborted)
            {
                // ★ 异常中断（急停/联锁破坏）：不调用 NotifyOrderCompletedAsync（避免误标 Completed 回传 SAP/ERP），
                // 仅记录日志与操作日志。订单保持 InProgress 状态，由现场后续处理：
                //   - 排查故障 → 复位鹤位 → 重新启动（继续装料）
                //   - 或由人工调用 StopCommand / CancelDispatchAsync 终止单据
                Log.Warning("[Main] 捕获鹤位 {CraneId} 异常中断事件：{Reason}，实际装载 {Weight:F2}kg（订单保持 InProgress，待人工处理）",
                    e.CraneId, e.AbortReason ?? "(未知)", e.ActualWeight);
            }
            else
            {
                Log.Information("[Main] 捕获鹤位 {CraneId} 正常完成事件，实际装载 {Weight:F2}kg",
                    e.CraneId, e.ActualWeight);
                await _vm.OrderManager.NotifyOrderCompletedAsync(e.CraneId, e.ActualWeight, e.StartTime, e.EndTime);
            }
        });
    }

    private void UpdateSapStatus(bool sapOk, bool erpOk)
    {
        var color = sapOk && erpOk ? Colors.LimeGreen : (sapOk || erpOk ? Colors.Orange : Colors.Red);
        var msg = (sapOk ? "SAP✓ " : "SAP✗ ") + (erpOk ? "ERP✓" : "ERP✗");
        SapConnEllipse.Fill = new SolidColorBrush(color);
        SapConnText.Text = msg;
    }

    /// <summary>构建所有鹤位卡片到WrapPanel和Header</summary>
    private void BuildCraneCards()
    {
        CraneWrapPanel.Children.Clear();
        CraneHeaderPanel.Children.Clear();

        Log.Information("[Main] BuildCraneCards 开始，鹤位数: {Count}", _vm.CraneManager.Cranes.Count);

        if (_vm.CraneManager.Cranes.Count == 0)
        {
            CraneWrapPanel.Children.Add(new TextBlock
            {
                Text = "⚠ 未加载到任何鹤位数据，请检查配置文件和 PLC 连接",
                FontSize = 16,
                Foreground = Brushes.Red,
                Margin = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        foreach (var crane in _vm.CraneManager.Cranes)
        {
            // Header按钮
            var headerBtn = new RadioButton
            {
                Content = crane.Name,
                GroupName = "CraneHeader",
                Tag = crane.Id,
                Margin = new Thickness(4),
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Background = Brushes.White,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                IsChecked = true
            };
            headerBtn.Checked += (s, e) => HighlightCraneCard(crane.Id);
            CraneHeaderPanel.Children.Add(headerBtn);

            // 卡片
            var card = _vm.ServiceProvider.GetService(typeof(CranePositionCard)) as CranePositionCard
                       ?? new CranePositionCard(_vm.CraneManager, _vm.OrderManager);
            card.Crane = crane;
            card.Tag = crane.Id;

            // 卡片大小（按当前布局模式 _layoutMode 切换）
            switch (_layoutMode)
            {
                case 1: // 横向平铺：3列
                    card.Width = 360;
                    card.MinWidth = 320;
                    card.MinHeight = 480;
                    break;
                case 2: // 紧凑模式：4列
                    card.Width = 280;
                    card.MinWidth = 240;
                    card.MinHeight = 420;
                    break;
                default: // 流式布局：2列
                    card.Width = 480;
                    card.MinWidth = 420;
                    card.MinHeight = 520;
                    break;
            }
            card.Margin = new Thickness(4);
            CraneWrapPanel.Children.Add(card);

            Log.Information("[Main] 鹤位卡片已添加: {Id} - {Name}", crane.Id, crane.Name);
        }

        Log.Information("[Main] BuildCraneCards 完成，共添加 {Count} 张卡片", CraneWrapPanel.Children.Count);
    }

    private void HighlightCraneCard(string craneId)
    {
        foreach (var child in CraneWrapPanel.Children.OfType<CranePositionCard>())
        {
            var border = child.Content as Border;
            if (child.Tag?.ToString() == craneId)
            {
                if (border != null) border.BorderBrush = Brushes.DodgerBlue;
                child.BringIntoView();
            }
            else
            {
                if (border != null) border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            }
        }
    }

    private void UpdateOrdersGridBinding(int tabIndex)
    {
        ObservableCollection<LoadingOrder> src = tabIndex switch
        {
            0 => _vm.OrderManager.PendingOrders,
            1 => _vm.OrderManager.ActiveOrders,
            2 => _vm.OrderManager.CompletedOrders,
            _ => _vm.OrderManager.PendingOrders
        };
        OrdersGrid.ItemsSource = new CollectionViewSource { Source = src }.View;
        DispatchMenu.IsEnabled = tabIndex == 0;
        CancelMenu.IsEnabled = tabIndex <= 1;
    }

    private void OrderTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOrdersGridBinding(OrderTabs.SelectedIndex);
    }

    private async void RefreshOrders_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            int newCount = await _vm.OrderManager.RefreshOrdersFromSourceAsync();
            bool sap = await _vm.SapService.TestConnectionAsync();
            bool erp = await _vm.ErpService.TestConnectionAsync();
            UpdateSapStatus(sap, erp);
            _vm.StatusBarText = $"同步完成  ·  新增单据 {newCount} 份  ·  待下发 {_vm.OrderManager.PendingOrders.Count}";
            Log.Information("[Main] 手动刷新单据，新增{Count}", newCount);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "刷新失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void CreateManualOrder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = _vm.ServiceProvider.GetService(typeof(ManualOrderDialog)) as ManualOrderDialog
                      ?? new ManualOrderDialog(_vm.OrderManager);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && dlg.CreatedOrder != null)
            {
                OrderTabs.SelectedIndex = 0;
                _vm.StatusBarText = $"已手工创建单据 {dlg.CreatedOrder.OrderNo}";
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Main] 手工创建单据异常");
            MessageBox.Show(this, ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleLayout_Click(object sender, RoutedEventArgs e)
    {
        _layoutMode = (_layoutMode + 1) % 3;
        string modeName = _layoutMode switch
        {
            0 => "流式布局（自适应）",
            1 => "横向平铺",
            2 => "紧凑模式",
            _ => ""
        };
        BuildCraneCards();
        _vm.StatusBarText = "已切换到 " + modeName;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var info = $"系统: {_vm.AppTitle}\n\n" +
                   $"鹤位数: {_vm.CraneManager.Cranes.Count}\n" +
                   $"待下发单据: {_vm.OrderManager.PendingOrders.Count}\n" +
                   $"进行中单据: {_vm.OrderManager.ActiveOrders.Count}\n" +
                   $"已完成单据: {_vm.OrderManager.CompletedOrders.Count}\n" +
                   $"PLC连接: {_vm.CraneManager.Cranes.FirstOrDefault()?.IsPlcConnected}\n";
        MessageBox.Show(this, info, "系统信息", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void GlobalEmergency_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this, "⚠ 确认执行【全局紧急停止】？\n这将立即停止所有鹤位的所有装料作业！",
            "全局紧急停止确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            Log.Warning("[Main] ===== 全局紧急停止触发 =====");
            foreach (var crane in _vm.CraneManager.Cranes)
            {
                await _vm.CraneManager.EmergencyStopAsync(crane.Id);
            }
            _vm.StatusBarText = "⚠ 已执行全局紧急停止";
            MessageBox.Show(this, "全局紧急停止已执行。\n请排查原因后执行各鹤位【复位】操作。",
                "全局急停完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Main] 全局急停异常");
            MessageBox.Show(this, "执行异常：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DispatchOrderMenu_Click(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not LoadingOrder order) return;
        OpenDispatchDialog(order);
    }

    private void OrdersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not LoadingOrder order) return;
        if (OrderTabs.SelectedIndex == 0)
            OpenDispatchDialog(order);
        else
            ShowOrderDetail(order);
    }

    private void OpenDispatchDialog(LoadingOrder order)
    {
        try
        {
            var vm = _vm.ServiceProvider.GetService(typeof(DispatchDialogViewModel)) as DispatchDialogViewModel
                     ?? new DispatchDialogViewModel(_vm.CraneManager, _vm.OrderManager);
            var dlg = new DispatchOrderDialog(vm) { Owner = this };
            dlg.SetOrder(order);
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Main] 打开下发对话框异常");
            MessageBox.Show(this, "打开下发对话框失败：" + ex.Message, "错误");
        }
    }

    private void ShowOrderDetail(LoadingOrder order)
    {
        string detail = $"【单据详情】\n\n" +
                        $"单据号: {order.OrderNo}\n" +
                        $"来源: {order.Source}\n" +
                        $"状态: {order.Status}\n" +
                        $"创建时间: {order.CreateTime:yyyy-MM-dd HH:mm:ss}\n" +
                        $"下发时间: {order.DispatchTime:yyyy-MM-dd HH:mm:ss}\n" +
                        $"完成时间: {order.CompleteTime:yyyy-MM-dd HH:mm:ss}\n\n" +
                        $"客户: {order.CustomerName} ({order.CustomerCode})\n" +
                        $"车牌: {order.VehicleNo}\n" +
                        $"司机: {order.DriverName}  {order.DriverPhone}\n\n" +
                        $"产品: {order.ProductName} ({order.ProductCode})\n" +
                        $"计划量: {order.PlannedWeight:F2} kg\n" +
                        $"实际量: {order.ActualWeight:F2} kg\n" +
                        $"误差允许: ±{order.AllowedTolerance:F2} kg\n" +
                        $"鹤位: {order.AssignedCraneId}\n\n" +
                        $"单价: ¥{order.UnitPrice:F4}\n" +
                        $"金额: ¥{order.TotalAmount:F2}\n" +
                        $"合同号: {order.ContractNo}\n" +
                        $"批次号: {order.BatchNo}\n" +
                        $"罐区: {order.TankArea}\n" +
                        $"备注: {order.Remarks}";
        MessageBox.Show(this, detail, $"单据详情 - {order.OrderNo}", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void CancelOrderMenu_Click(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not LoadingOrder order) return;
        var r = MessageBox.Show(this, $"确认取消单据 {order.OrderNo}？", "确认",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        bool ok = await _vm.OrderManager.CancelDispatchAsync(order.OrderNo);
        if (!ok) MessageBox.Show(this, "取消失败", "提示");
    }

    private void StatusTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            int loading = _vm.CraneManager.Cranes.Count(c => c.Status == CraneStatus.Loading);
            int ready = _vm.CraneManager.Cranes.Count(c => c.Status == CraneStatus.Ready);
            int fault = _vm.CraneManager.Cranes.Count(c => c.Status == CraneStatus.Fault || c.Status == CraneStatus.EmergencyStop);
            StatusRight.Text = $"装料中:{loading}  就绪:{ready}  异常:{fault}  待处理:{_vm.OrderManager.PendingOrders.Count}";
        }
        catch { }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        var r = MessageBox.Show(this, "确认退出系统？", "退出确认",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        try
        {
            if (_vm.CraneManager is CraneManagerService cm)
                cm.OnCraneCompleted -= CraneMgr_OnCraneCompleted;
            _statusTimer.Stop();
            _clockTimer.Stop();
            Log.Information("[Main] 用户退出系统");
            Log.CloseAndFlush();
        }
        catch { }
    }
}
