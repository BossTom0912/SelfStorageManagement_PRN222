using System.Windows;
using System.Windows.Controls;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class AssignFacilityDialog : Window
{
    private readonly UserAccountModel _user;
    public bool IsSuccess { get; private set; }

    public AssignFacilityDialog(UserAccountModel user)
    {
        InitializeComponent();
        _user = user;
        lblEmployeeInfo.Text = $"Employee: {user.Email} ({user.FullName ?? "N/A"}, Code: {user.EmployeeCode ?? "N/A"})";
        dpStartsAt.SelectedDate = DateTime.Today;
        Loaded += AssignFacilityDialog_Loaded;
    }

    private async void AssignFacilityDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await ApiClient.Instance.GetFacilitiesAsync();
            if (result.Success && result.Data != null)
            {
                cmbFacility.ItemsSource = result.Data;
                if (result.Data.Count > 0)
                {
                    cmbFacility.SelectedIndex = 0;
                }
            }
        }
        catch (Exception ex)
        {
            lblMessage.Text = "Error loading facilities: " + ex.Message;
        }
    }

    private async void BtnAssign_Click(object sender, RoutedEventArgs e)
    {
        if (cmbFacility.SelectedValue is not long facilityId || facilityId <= 0)
        {
            lblMessage.Text = "Please select a target facility.";
            return;
        }

        var role = (cmbAssignmentRole.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "facility_staff";
        var startDate = dpStartsAt.SelectedDate ?? DateTime.Today;
        DateTimeOffset startsAt = new DateTimeOffset(startDate.ToUniversalTime());

        DateTimeOffset? endsAt = null;
        if (dpEndsAt.SelectedDate.HasValue)
        {
            endsAt = new DateTimeOffset(dpEndsAt.SelectedDate.Value.ToUniversalTime().AddDays(1).AddSeconds(-1));
            if (endsAt <= startsAt)
            {
                lblMessage.Text = "EndsAt must be after StartsAt.";
                return;
            }
        }

        lblMessage.Text = "Assigning facility...";

        try
        {
            var result = await ApiClient.Instance.AssignFacilityAsync(_user.Id, facilityId, role, startsAt, endsAt);
            if (result.Success)
            {
                MessageBox.Show("Facility assigned successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
