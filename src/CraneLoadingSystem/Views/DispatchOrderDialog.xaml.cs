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

    private void RefreshAvailableCranes()
    {
        AvailableCranes.Clear();
        var productCode = SelectedOrder?.ProductCode ?? SelectedOrder?.ProductName ?? "";
        var list = _craneMgr.GetAvailableCranesForProduct(productCode).ToList();
        if (list.Count == 0)
            list = _craneMgr.Cranes.Where(c => c.Status is CraneStatus.Idle or CraneStatus.Ready or CraneStatus.Completed).ToList();

        foreach (var c in list) AvailableCranes.Add(c);
        RecommendedFlagConverter.FirstRecommended = list.FirstOrDefault();
        if (SelectedCrane == null || !AvailableCranes.Contains(SelectedCrane))
            SelectedCrane = AvailableCranes.FirstOrDefault();
    }

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

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
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
            // 使用同步等待避免DialogResult过早丢失Context
            var ok = _vm.ConfirmDispatchAsync().GetAwaiter().GetResult();
            if (ok)
            {
                MessageBox.Show(this, $"单据已成功下发到 [{_vm.SelectedCrane.Name}]",
                    "下发成功", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(this, "下发失败，请查看日志详情。", "失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "下发异常：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
