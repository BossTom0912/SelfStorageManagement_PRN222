using System.Windows;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class ManageRolesDialog : Window
{
    private readonly UserAccountModel _user;
    public bool IsSuccess { get; private set; }

    public ManageRolesDialog(UserAccountModel user)
    {
        InitializeComponent();
        _user = user;
        lblUserInfo.Text = $"User: {user.Email} (ID: {user.Id}, Name: {user.FullName ?? "N/A"})";

        chkCustomer.IsChecked = user.Roles.Contains("storage_customer");
        chkStaff.IsChecked = user.Roles.Contains("facility_staff");
        chkManager.IsChecked = user.Roles.Contains("facility_manager");
        chkBOM.IsChecked = user.Roles.Contains("business_operations_manager");
        chkAdmin.IsChecked = user.Roles.Contains("system_administrator");
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var roles = new List<string>();
        if (chkCustomer.IsChecked == true) roles.Add("storage_customer");
        if (chkStaff.IsChecked == true) roles.Add("facility_staff");
        if (chkManager.IsChecked == true) roles.Add("facility_manager");
        if (chkBOM.IsChecked == true) roles.Add("business_operations_manager");
        if (chkAdmin.IsChecked == true) roles.Add("system_administrator");

        if (roles.Count == 0)
        {
            lblMessage.Text = "At least one role must be selected.";
            return;
        }

        lblMessage.Text = "Saving...";

        try
        {
            var result = await ApiClient.Instance.ManageUserRolesAsync(_user.Id, roles);
            if (result.Success)
            {
                MessageBox.Show("User roles updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                IsSuccess = true;
                DialogResult = true;
                Close();
            }
            else
            {
                lblMessage.Text = result.Message;
            }
        }
        catch (Exception ex)
        {
            lblMessage.Text = "Error: " + ex.Message;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
