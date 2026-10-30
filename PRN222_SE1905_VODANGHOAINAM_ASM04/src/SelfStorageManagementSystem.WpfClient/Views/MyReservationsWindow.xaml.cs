using System.Windows;
using System.Windows.Controls;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class MyReservationsWindow : Window
{
    private int _pageNumber = 1;
    private const int PageSize = 10;
    private int _totalPages = 1;

    public MyReservationsWindow()
    {
        InitializeComponent();
        Loaded += MyReservationsWindow_Loaded;
    }

    private async void MyReservationsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        lblStatusMessage.Text = "Đang tải...";
        btnPrevPage.IsEnabled = false;
        btnNextPage.IsEnabled = false;

        string? selectedStatus = null;
        if (cboStatusFilter.SelectedItem is ComboBoxItem item && item.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
        {
            selectedStatus = tag;
        }

        var response = await ApiClient.Instance.GetMyReservationsAsync(_pageNumber, PageSize, selectedStatus);

        if (!response.Success || response.Data == null)
        {
            lblStatusMessage.Text = $"Lỗi: {response.Message}";
            MessageBox.Show(response.Message, "Lỗi tải danh sách", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = response.Data;
        gridReservations.ItemsSource = result.Items;
        _totalPages = Math.Max(1, result.TotalPages);

        lblPageInfo.Text = $"Trang {result.PageNumber} / {_totalPages} ({result.TotalCount} đơn)";
        btnPrevPage.IsEnabled = result.HasPreviousPage;
        btnNextPage.IsEnabled = result.HasNextPage;
        lblStatusMessage.Text = $"Đã tải {result.Items.Count} đơn.";
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private async void CboStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _pageNumber = 1;
        await LoadDataAsync();
    }

    private async void BtnPrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pageNumber > 1)
        {
            _pageNumber--;
            await LoadDataAsync();
        }
    }

    private async void BtnNextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pageNumber < _totalPages)
        {
            _pageNumber++;
            await LoadDataAsync();
        }
    }

    private void BtnViewDetails_Click(object sender, RoutedEventArgs e)
    {
        if (gridReservations.SelectedItem is not ReservationListItemClientModel selected)
        {
            MessageBox.Show("Vui lòng chọn một đơn đặt chỗ trong danh sách.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var detailWin = new ReservationDetailWindow(selected.Id)
        {
            Owner = this
        };
        detailWin.ShowDialog();

        // Refresh list after closing detail
        _ = LoadDataAsync();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
