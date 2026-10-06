using System.Windows;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApiClient.Instance.SessionExpired += OnSessionExpired;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateDashboard();
    }

    private void PopulateDashboard()
    {
        var user = SessionStore.CurrentUser;
        if (user == null || !SessionStore.IsLoggedIn)
        {
            MessageBox.Show("Your session has expired. Please sign in again.", "Session Expired", MessageBoxButton.OK, MessageBoxImage.Warning);
            RedirectToLogin();
            return;
        }

        lblHeaderUser.Text = user.Email;
        txtProfileName.Text = user.DisplayName;
        txtProfileEmail.Text = user.Email;
        txtProfileRoles.Text = string.Join(", ", user.Roles);
        txtProfileStatus.Text = user.Status.ToUpperInvariant();

        lblRoleBadge.Text = user.Roles.FirstOrDefault() ?? "User";

        // Admin panel shortcut
        if (SessionStore.IsSystemAdministrator)
        {
            btnOpenAdmin.Visibility = Visibility.Visible;
        }
        else
        {
            btnOpenAdmin.Visibility = Visibility.Collapsed;
        }

        // Staff / Manager Facility Scope assignments
        if (user.ActiveAssignments.Count > 0)
        {
            panelAssignments.Visibility = Visibility.Visible;
            gridAssignments.ItemsSource = user.ActiveAssignments;
        }
        else
        {
            panelAssignments.Visibility = Visibility.Collapsed;
        }

        // Customer Welcome
        if (user.Roles.Contains("storage_customer"))
        {
            panelCustomer.Visibility = Visibility.Visible;
        }

        lblStatusFooter.Text = $"Signed in as {user.Email} | Token expires at: {SessionStore.TokenExpiresAt:yyyy-MM-dd HH:mm:ss UTC}";
    }

    private void BtnOpenAdmin_Click(object sender, RoutedEventArgs e)
    {
        var adminWindow = new AdminAccountsWindow();
        adminWindow.Owner = this;
        adminWindow.ShowDialog();
        // Refresh dashboard on return in case user's own status/roles changed
        _ = RefreshProfileAsync();
    }

    private void BtnBrowseCatalog_Click(object sender, RoutedEventArgs e)
    {
        var catalogWindow = new FacilityCatalogWindow();
        catalogWindow.Owner = this;
        catalogWindow.ShowDialog();
    }

    private async Task RefreshProfileAsync()
    {
        var meResult = await ApiClient.Instance.GetMeAsync();
        if (meResult.Success)
        {
            PopulateDashboard();
        }
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        ApiClient.Instance.Logout();
        RedirectToLogin();
    }

    private void OnSessionExpired()
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show("Session expired or token invalid. Returning to login.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            RedirectToLogin();
        });
    }

    private void RedirectToLogin()
    {
        ApiClient.Instance.SessionExpired -= OnSessionExpired;
        var loginWindow = new LoginWindow();
        loginWindow.Show();
        Close();
    }
}
