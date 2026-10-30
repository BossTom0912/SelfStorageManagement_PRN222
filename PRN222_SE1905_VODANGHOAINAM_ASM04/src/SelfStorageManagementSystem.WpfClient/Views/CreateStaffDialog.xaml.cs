using System.Windows;
using System.Windows.Controls;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class CreateStaffDialog : Window
{
    public bool IsSuccess { get; private set; }

    public CreateStaffDialog()
    {
        InitializeComponent();
        dpHireDate.SelectedDate = DateTime.Today;
        Loaded += CreateStaffDialog_Loaded;
    }

    private async void CreateStaffDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var facResult = await ApiClient.Instance.GetFacilitiesAsync();
            if (facResult.Success && facResult.Data != null)
            {
                var list = new List<FacilityLookupModel>
                {
                    new FacilityLookupModel { Id = 0, Code = "NONE", Name = "-- None / Assign Later --", City = "" }
                };
                list.AddRange(facResult.Data);
                cmbFacility.ItemsSource = list;
                cmbFacility.SelectedIndex = 0;
            }
        }
        catch
        {
            // Ignore lookup failure
        }
    }

    private async void BtnCreate_Click(object sender, RoutedEventArgs e)
    {
        var email = txtEmail.Text.Trim();
        var password = txtPassword.Password;
        var fullName = txtFullName.Text.Trim();
        var empCode = txtEmployeeCode.Text.Trim();
        var phone = txtPhone.Text.Trim();
        var hireDate = dpHireDate.SelectedDate ?? DateTime.Today;
        var role = (cmbRole.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "facility_staff";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(empCode))
        {
            lblMessage.Text = "Please fill in all required fields (*).";
            return;
        }

        long? facilityId = null;
        if (cmbFacility.SelectedValue is long fid && fid > 0)
        {
            facilityId = fid;
        }

        lblMessage.Text = "Creating...";

        var model = new CreateStaffModel
        {
            Email = email,
            Password = password,
            FullName = fullName,
            EmployeeCode = empCode,
            PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone,
            HireDate = DateOnly.FromDateTime(hireDate),
            RoleCode = role,
            InitialFacilityId = facilityId
        };

        try
        {
            var result = await ApiClient.Instance.CreateStaffAccountAsync(model);
            if (result.Success)
            {
                MessageBox.Show("Staff account created successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
