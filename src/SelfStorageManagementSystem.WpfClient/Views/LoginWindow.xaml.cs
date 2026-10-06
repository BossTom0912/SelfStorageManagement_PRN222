using System.Windows;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class LoginWindow : Window
{
    private const string DemoPassword = "Storage@Demo2026!";

    public LoginWindow()
    {
        InitializeComponent();
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        var email = txtEmail.Text.Trim();
        var password = txtPassword.Password;
        var apiUrl = txtApiUrl.Text.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            lblMessage.Text = "Please enter both email and password.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(apiUrl))
        {
            ApiClient.Instance.BaseUrl = apiUrl;
        }

        btnLogin.IsEnabled = false;
        btnLogin.Content = "Signing in...";
        lblMessage.Text = string.Empty;

        try
        {
            var loginResult = await ApiClient.Instance.LoginAsync(email, password);
            if (!loginResult.Success || loginResult.Data == null)
            {
                lblMessage.Text = loginResult.Message;
                return;
            }

            // Fetch current user details & assignments
            var meResult = await ApiClient.Instance.GetMeAsync();
            if (!meResult.Success)
            {
                lblMessage.Text = "Failed to load user profile: " + meResult.Message;
                return;
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }
        catch (Exception ex)
        {
            lblMessage.Text = "Connection error: " + ex.Message;
        }
        finally
        {
            btnLogin.IsEnabled = true;
            btnLogin.Content = "Sign In";
        }
    }

    private void BtnRegister_Click(object sender, RoutedEventArgs e)
    {
        var registerWindow = new RegisterWindow();
        registerWindow.Owner = this;
        registerWindow.ShowDialog();
    }

    private void FillAdmin_Click(object sender, RoutedEventArgs e)
    {
        txtEmail.Text = "administrator@example.test";
        txtPassword.Password = DemoPassword;
        lblMessage.Text = string.Empty;
    }

    private void FillManager_Click(object sender, RoutedEventArgs e)
    {
        txtEmail.Text = "manager.hcm@example.test";
        txtPassword.Password = DemoPassword;
        lblMessage.Text = string.Empty;
    }

    private void FillStaff_Click(object sender, RoutedEventArgs e)
    {
        txtEmail.Text = "staff.hcm@example.test";
        txtPassword.Password = DemoPassword;
        lblMessage.Text = string.Empty;
    }

    private void FillCustomer_Click(object sender, RoutedEventArgs e)
    {
        txtEmail.Text = "customer.one@example.test";
        txtPassword.Password = DemoPassword;
        lblMessage.Text = string.Empty;
    }
}
