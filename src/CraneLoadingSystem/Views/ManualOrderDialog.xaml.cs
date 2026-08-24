using System;
using System.Windows;
using CraneLoadingSystem.Services;

namespace CraneLoadingSystem.Views;

/// <summary>
/// 手工创建单据对话框
/// </summary>
public partial class ManualOrderDialog : Window
{
    private readonly IOrderManagementService _orderMgr;
    public Models.LoadingOrder? CreatedOrder { get; private set; }

    public ManualOrderDialog(IOrderManagementService orderMgr)
    {
        InitializeComponent();
        _orderMgr = orderMgr;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CustomerName.Text))
        { ShowError("请输入客户名称"); return; }
        if (string.IsNullOrWhiteSpace(VehicleNo.Text))
        { ShowError("请输入车牌号码"); return; }
        if (string.IsNullOrWhiteSpace(ProductName.Text))
        { ShowError("请输入产品名称"); return; }
        if (!double.TryParse(PlannedWeight.Text, out double weight) || weight <= 0)
        { ShowError("请输入有效的计划装载量"); return; }

        double? tol = null;
        if (double.TryParse(Tolerance.Text, out var t) && t > 0) tol = t;

        try
        {
            CreatedOrder = _orderMgr.CreateManualOrder(
                CustomerName.Text.Trim(),
                VehicleNo.Text.Trim(),
                ProductCode.Text.Trim(),
                ProductName.Text.Trim(),
                weight, tol);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "创建失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string msg) => MessageBox.Show(this, msg, "输入提示", MessageBoxButton.OK, MessageBoxImage.Warning);
}
