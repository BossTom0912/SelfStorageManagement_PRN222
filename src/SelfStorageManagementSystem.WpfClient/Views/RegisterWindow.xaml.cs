using System.Windows;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class RegisterWindow : Window
{
    public RegisterWindow()
    {
        InitializeComponent();
    }

    private async void BtnRegister_Click(object sender, RoutedEventArgs e)
    {
        var fullName = txtFullName.Text.Trim();
        var email = txtEmail.Text.Trim();
        var password = txtPassword.Password;
        var confirmPassword = txtConfirmPassword.Password;
        var phone = txtPhone.Text.Trim();
        var identity = txtIdentity.Text.Trim();
        var address = txtAddress.Text.Trim();
        var emergencyName = txtEmergencyName.Text.Trim();
        var emergencyPhone = txtEmergencyPhone.Text.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            lblMessage.Text = "Full name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            lblMessage.Text = "Email address is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            lblMessage.Text = "Password must be at least 6 characters long.";
            return;
        }

        if (password != confirmPassword)
        {
            lblMessage.Text = "Password confirmation does not match.";
            return;
        }

        lblMessage.Foreground = System.Windows.Media.Brushes.Gray;
        lblMessage.Text = "Registering...";

        var model = new CustomerRegisterModel
        {
            Email = email,
            Password = password,
            FullName = fullName,
            PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone,
            IdentityNumber = string.IsNullOrWhiteSpace(identity) ? null : identity,
            Address = string.IsNullOrWhiteSpace(address) ? null : address,
            EmergencyContactName = string.IsNullOrWhiteSpace(emergencyName) ? null : emergencyName,
            EmergencyContactPhone = string.IsNullOrWhiteSpace(emergencyPhone) ? null : emergencyPhone
        };

        try
        {
            var result = await ApiClient.Instance.RegisterCustomerAsync(model);
            if (result.Success)
            {
                MessageBox.Show("Registration successful! You can now log in with your email.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            else
            {
                lblMessage.Foreground = System.Windows.Media.Brushes.Red;
                lblMessage.Text = result.Message;
            }
        }
        catch (Exception ex)
        {
            lblMessage.Foreground = System.Windows.Media.Brushes.Red;
            lblMessage.Text = "Error: " + ex.Message;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
