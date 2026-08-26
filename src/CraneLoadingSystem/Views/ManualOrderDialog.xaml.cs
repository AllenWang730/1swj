using System;
using System.Windows;
using CraneLoadingSystem.Services;

namespace CraneLoadingSystem.Views;

/// <summary>
/// 手工创建单据对话框（用于 SAP/ERP 不可用或临时补单的场景）
/// </summary>
public partial class ManualOrderDialog : Window
{
    private readonly IOrderManagementService _orderMgr;
    /// <summary>创建成功的单据（确认后非空，取消为 null）</summary>
    public Models.LoadingOrder? CreatedOrder { get; private set; }

    /// <summary>构造函数，依赖注入 IOrderManagementService</summary>
    public ManualOrderDialog(IOrderManagementService orderMgr)
    {
        InitializeComponent();
        _orderMgr = orderMgr;
    }

    /// <summary>点击【确认】按钮：校验输入 → 调用 _orderMgr.CreateManualOrder → 关闭返回</summary>
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        // 必填项校验
        if (string.IsNullOrWhiteSpace(CustomerName.Text))
        { ShowError("请输入客户名称"); return; }
        if (string.IsNullOrWhiteSpace(VehicleNo.Text))
        { ShowError("请输入车牌号码"); return; }
        if (string.IsNullOrWhiteSpace(ProductName.Text))
        { ShowError("请输入产品名称"); return; }
        if (!double.TryParse(PlannedWeight.Text, out double weight) || weight <= 0)
        { ShowError("请输入有效的计划装载量"); return; }

        // 可选误差（未填或 <=0 视为不指定）
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

    /// <summary>点击【取消】按钮：返回 false 关闭对话框</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>弹出输入校验错误提示（不关闭对话框）</summary>
    private void ShowError(string msg) => MessageBox.Show(this, msg, "输入提示", MessageBoxButton.OK, MessageBoxImage.Warning);
}
