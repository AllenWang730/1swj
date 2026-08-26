using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraneLoadingSystem.Models;
using CraneLoadingSystem.Services;

namespace CraneLoadingSystem.Views;

/// <summary>
/// 推荐标记转换器（简单演示：第一个可用的鹤位标为推荐）
/// </summary>
public class RecommendedFlagConverter : IValueConverter
{
    public static CranePosition? FirstRecommended { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string id && FirstRecommended != null && FirstRecommended.Id == id)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// 下发单据对话框ViewModel
/// </summary>
public partial class DispatchDialogViewModel : ObservableObject
{
    private readonly ICraneManagerService _craneMgr;
    private readonly IOrderManagementService _orderMgr;

    [ObservableProperty] private LoadingOrder? _selectedOrder;
    [ObservableProperty] private CranePosition? _selectedCrane;
    public ObservableCollection<CranePosition> AvailableCranes { get; } = new();

    public IRelayCommand RefreshAvailableCommand { get; }

    public DispatchDialogViewModel(ICraneManagerService craneMgr, IOrderManagementService orderMgr)
    {
        _craneMgr = craneMgr;
        _orderMgr = orderMgr;
        RefreshAvailableCommand = new RelayCommand(RefreshAvailableCranes);
    }

    public void LoadData(LoadingOrder order)
    {
        SelectedOrder = order;
        RefreshAvailableCranes();
    }

    /// <summary>刷新可用鹤位列表：按产品匹配，无匹配则兜底返回所有 Idle/Ready/Completed 鹤位</summary>
    private void RefreshAvailableCranes()
    {
        AvailableCranes.Clear();
        // 优先用产品名匹配（比产品编码更可靠）
        var productKey = SelectedOrder?.ProductName ?? SelectedOrder?.ProductCode ?? "";
        var list = _craneMgr.GetAvailableCranesForProduct(productKey).ToList();
        if (list.Count == 0)
            // 兜底：返回所有空闲/就绪/已完成的鹤位（不限制产品）
            list = _craneMgr.Cranes.Where(c => c.Status is CraneStatus.Idle or CraneStatus.Ready or CraneStatus.Completed).ToList();

        foreach (var c in list) AvailableCranes.Add(c);
        RecommendedFlagConverter.FirstRecommended = list.FirstOrDefault();
        if (SelectedCrane == null || !AvailableCranes.Contains(SelectedCrane))
            SelectedCrane = AvailableCranes.FirstOrDefault();
    }

    /// <summary>确认下发：调 OrderManager.DispatchOrderToCraneAsync 将单据分配到选中鹤位</summary>
    public async Task<bool> ConfirmDispatchAsync()
    {
        if (SelectedOrder == null || SelectedCrane == null) return false;
        return await _orderMgr.DispatchOrderToCraneAsync(SelectedOrder.OrderNo, SelectedCrane.Id);
    }
}

/// <summary>
/// DispatchOrderDialog.xaml 的交互逻辑
/// </summary>
public partial class DispatchOrderDialog : Window
{
    private readonly DispatchDialogViewModel _vm;

    public DispatchOrderDialog(DispatchDialogViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;
        Resources["RecommendedFlagConv"] = new RecommendedFlagConverter();
    }

    public void SetOrder(LoadingOrder order)
    {
        _vm.LoadData(order);
        Title = $"下发单据 {order.OrderNo} 到鹤位";
    }

    /// <summary>【确认】按钮：校验选择 → 调 ConfirmDispatchAsync → 成功提示现场需手动启动 / 失败按状态提示原因</summary>
    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedOrder == null)
        {
            MessageBox.Show(this, "没有选择单据", "提示");
            return;
        }
        if (_vm.SelectedCrane == null)
        {
            MessageBox.Show(this, "请选择目标鹤位", "提示");
            return;
        }

        try
        {
            IsEnabled = false;

            var ok = await _vm.ConfirmDispatchAsync();
            if (ok)
            {
                MessageBox.Show(this,
                    $"单据已成功下发到 [{_vm.SelectedCrane.Name}]。\n\n" +
                    "鹤位已进入【就绪 Ready】状态。\n" +
                    "请现场操作员到鹤位卡片点【▶ 启动】按钮，" +
                    "在弹出的安全确认对话框中确认车辆停稳、鹤管连接、静电夹接地、" +
                    "阻车器升起、人员撤离等事项后，系统将自动校验8项联锁并通过后启动装料。\n\n" +
                    "⚠ 未完成现场确认前，装料不会自动启动。",
                    "下发成功", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                var crane = _vm.SelectedCrane;
                var hint = crane.Status switch
                {
                    CraneStatus.Loading => "鹤位正在装车中，无法重复下发",
                    CraneStatus.Paused => "鹤位已暂停，请恢复或完成后再下发",
                    CraneStatus.EmergencyStop => "鹤位处于急停状态，请复位后再下发",
                    CraneStatus.Offline => "鹤位离线，请检查 PLC 连接",
                    _ => "可能原因：安全联锁未满足或鹤位状态不允许。请查看日志详情。"
                };
                MessageBox.Show(this, $"下发失败：{hint}", "失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "下发异常：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
