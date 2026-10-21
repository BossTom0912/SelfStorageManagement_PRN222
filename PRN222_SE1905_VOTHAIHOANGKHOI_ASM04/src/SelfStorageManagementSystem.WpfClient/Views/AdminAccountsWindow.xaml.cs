using System.Windows;
using System.Windows.Controls;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class AdminAccountsWindow : Window
{
    private int _currentPage = 1;
    private const int PageSize = 10;
    private int _totalPages = 1;
    private UserAccountModel? _selectedUser;

    public AdminAccountsWindow()
    {
        InitializeComponent();
        Loaded += AdminAccountsWindow_Loaded;
    }

    private void AdminAccountsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _ = LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        var searchTerm = txtSearch.Text.Trim();

        string? status = null;
        if (cmbStatusFilter.SelectedItem is ComboBoxItem statusItem && statusItem.Content.ToString() != "All Statuses")
        {
            status = statusItem.Content.ToString();
        }

        string? role = null;
        if (cmbRoleFilter.SelectedItem is ComboBoxItem roleItem && roleItem.Content.ToString() != "All Roles")
        {
            role = roleItem.Content.ToString();
        }

        try
        {
            var result = await ApiClient.Instance.GetAccountsAsync(searchTerm, status, role, _currentPage, PageSize);
            if (result.Success && result.Data != null)
            {
                gridAccounts.ItemsSource = result.Data.Items;
                _totalPages = Math.Max(1, result.Data.TotalPages);
                lblPageInfo.Text = $"Page {_currentPage} of {_totalPages} (Total: {result.Data.TotalCount})";
                btnPrevPage.IsEnabled = result.Data.HasPreviousPage;
                btnNextPage.IsEnabled = result.Data.HasNextPage;

                // Re-select if previously selected
                if (_selectedUser != null)
                {
                    var updated = result.Data.Items.FirstOrDefault(u => u.Id == _selectedUser.Id);
                    if (updated != null)
                    {
                        gridAccounts.SelectedItem = updated;
                    }
                }
            }
            else
            {
                MessageBox.Show("Failed to load accounts: " + result.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error loading accounts: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GridAccounts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedUser = gridAccounts.SelectedItem as UserAccountModel;
        if (_selectedUser == null)
        {
            lblSelectedUser.Text = "No account selected";
            gridEmpAssignments.ItemsSource = null;
            return;
        }

        lblSelectedUser.Text = $"{_selectedUser.Email} (ID: {_selectedUser.Id}, {_selectedUser.Status.ToUpperInvariant()})";

        // Sync change status combo
        foreach (ComboBoxItem item in cmbChangeStatus.Items)
        {
            if (item.Content.ToString() == _selectedUser.Status)
            {
                cmbChangeStatus.SelectedItem = item;
                break;
            }
        }

        // Show assignments
        gridEmpAssignments.ItemsSource = _selectedUser.FacilityAssignments;
    }

    private async void BtnUpdateStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null)
        {
            MessageBox.Show("Please select an account first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newStatus = (cmbChangeStatus.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrEmpty(newStatus))
        {
            return;
        }

        if (newStatus == _selectedUser.Status)
        {
            MessageBox.Show($"Account is already in '{newStatus}' status.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"Are you sure you want to change status of '{_selectedUser.Email}' to '{newStatus}'?",
            "Confirm Status Change", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var result = await ApiClient.Instance.UpdateUserStatusAsync(_selectedUser.Id, newStatus);
            if (result.Success)
            {
                MessageBox.Show("User status updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadAccountsAsync();
            }
            else
            {
                MessageBox.Show("Failed: " + result.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnOpenManageRoles_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null)
        {
            MessageBox.Show("Please select an account first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new ManageRolesDialog(_selectedUser);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.IsSuccess)
        {
            await LoadAccountsAsync();
        }
    }

    private async void BtnOpenCreateStaff_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateStaffDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.IsSuccess)
        {
            await LoadAccountsAsync();
        }
    }

    private async void BtnOpenAssignFacility_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null)
        {
            MessageBox.Show("Please select an account first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(_selectedUser.EmployeeCode))
        {
            MessageBox.Show("This account does not have an employee profile. Facility assignments can only be created for staff or managers.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new AssignFacilityDialog(_selectedUser);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.IsSuccess)
        {
            await LoadAccountsAsync();
        }
    }

    private async void BtnTerminateAssignment_Click(object sender, RoutedEventArgs e)
    {
        var assignment = gridEmpAssignments.SelectedItem as FacilityAssignmentModel;
        if (assignment == null)
        {
            MessageBox.Show("Please select an assignment from the table on the right.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show($"Terminate assignment ID {assignment.Id} at facility '{assignment.FacilityName}'?",
            "Confirm Termination", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var result = await ApiClient.Instance.TerminateFacilityAssignmentAsync(assignment.Id);
            if (result.Success)
            {
                MessageBox.Show("Assignment terminated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadAccountsAsync();
            }
            else
            {
                MessageBox.Show("Failed: " + result.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        _currentPage = 1;
        _ = LoadAccountsAsync();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        txtSearch.Text = string.Empty;
        cmbStatusFilter.SelectedIndex = 0;
        cmbRoleFilter.SelectedIndex = 0;
        _currentPage = 1;
        _ = LoadAccountsAsync();
    }

    private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            _ = LoadAccountsAsync();
        }
    }

    private void BtnNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < _totalPages)
        {
            _currentPage++;
            _ = LoadAccountsAsync();
        }
    }
}
