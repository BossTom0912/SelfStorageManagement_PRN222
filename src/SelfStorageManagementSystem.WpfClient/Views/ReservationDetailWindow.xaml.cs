using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class ReservationDetailWindow : Window
{
    private readonly long _reservationId;
    private ReservationDetailClientModel? _model;
    private DispatcherTimer? _countdownTimer;
    private DateTimeOffset _serverTimeSyncPoint;
    private DateTimeOffset _clientTimeSyncPoint;

    public ReservationDetailWindow(long reservationId)
    {
        InitializeComponent();
        _reservationId = reservationId;
        Loaded += ReservationDetailWindow_Loaded;
        Closed += ReservationDetailWindow_Closed;
    }

    public ReservationDetailWindow(ReservationDetailClientModel initialModel)
        : this(initialModel.Id)
    {
        _model = initialModel;
        BindModel(_model);
    }

    private async void ReservationDetailWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadReservationAsync();
    }

    private void ReservationDetailWindow_Closed(object? sender, EventArgs e)
    {
        StopCountdown();
    }

    private async Task LoadReservationAsync()
    {
        lblStatusMessage.Text = "Đang tải dữ liệu từ máy chủ...";
        btnRefresh.IsEnabled = false;

        var response = await ApiClient.Instance.GetReservationByIdAsync(_reservationId);

        btnRefresh.IsEnabled = true;

        if (!response.Success || response.Data == null)
        {
            lblStatusMessage.Text = $"Lỗi: {response.Message}";
            MessageBox.Show(response.Message, "Lỗi tải thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _model = response.Data;
        BindModel(_model);
        lblStatusMessage.Text = $"Cập nhật lúc: {DateTime.Now:HH:mm:ss}";
    }

    private void BindModel(ReservationDetailClientModel model)
    {
        txtHeaderCode.Text = model.ReservationCode;
        txtCustomerInfo.Text = $"Khách hàng: {model.CustomerName} ({model.CustomerEmail ?? "ID: " + model.CustomerId})";

        // Status badge
        lblStatusText.Text = model.StatusDisplayName;
        badgeStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(model.StatusColorHex));

        // Location & Unit Type
        txtFacilityName.Text = $"{model.FacilityName} ({model.FacilityCode})";
        txtFacilityAddress.Text = model.FacilityAddress;
        txtUnitTypeName.Text = $"{model.UnitTypeName} ({model.UnitTypeAreaM2:0.##} m²)" + (model.ClimateControlled ? " [Có điều hòa]" : "");
        txtRentalDates.Text = model.FormattedDateRange;

        // Pricing
        txtMonthlyRate.Text = $"{model.MonthlyRateSnapshot:N0} đ";
        txtDepositAmount.Text = $"{model.DepositSnapshot:N0} đ";
        txtBookingFee.Text = model.BookingFeeSnapshot > 0 ? $"{model.BookingFeeSnapshot:N0} đ" : "0 đ (Miễn phí)";
        txtDiscount.Text = model.DiscountSnapshot > 0 ? $"-{model.DiscountSnapshot:N0} đ" : "0 đ";
        txtQuotedTotal.Text = $"{model.QuotedTotal:N0} đ";

        txtPolicyNote.Text = model.PolicyNote;

        // Cancelled Info
        if (model.Status == "cancelled")
        {
            panelCancelInfo.Visibility = Visibility.Visible;
            txtCancellationDetails.Text = $"Thời điểm hủy: {model.CancelledAt?.ToLocalTime():dd/MM/yyyy HH:mm:ss}\nLý do: {model.CancellationReason ?? "Khách hàng yêu cầu hủy."}";
        }
        else
        {
            panelCancelInfo.Visibility = Visibility.Collapsed;
        }

        btnCancelReservation.IsEnabled = model.CanCancel;

        // Synchronize Server Time for Countdown
        _serverTimeSyncPoint = model.ServerNow;
        _clientTimeSyncPoint = DateTimeOffset.UtcNow;

        if (model.IsHoldActive)
        {
            panelCountdown.Visibility = Visibility.Visible;
            lblHoldUntilDeadline.Text = $"Hạn chót thanh toán: {model.FormattedHoldUntil}";
            StartCountdown();
        }
        else
        {
            StopCountdown();
            if (model.Status is "pending" or "awaiting_deposit" or "expired")
            {
                panelCountdown.Visibility = Visibility.Visible;
                panelCountdown.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242)); // Red-50
                panelCountdown.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165)); // Red-300
                txtCountdownTimer.Text = "00:00";
                txtCountdownTimer.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                lblCountdownDesc.Text = "Đơn giữ chỗ đã hết hạn tối đa 15 phút. Sức chứa đã được giải phóng về trạng thái khả dụng cho khách hàng khác (BR-RSV-01).";
                lblHoldUntilDeadline.Text = $"Đã hết hạn vào: {model.FormattedHoldUntil}";
            }
            else
            {
                panelCountdown.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void StartCountdown()
    {
        StopCountdown();

        UpdateCountdownDisplay();

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += CountdownTimer_Tick;
        _countdownTimer.Start();
    }

    private void StopCountdown()
    {
        if (_countdownTimer != null)
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= CountdownTimer_Tick;
            _countdownTimer = null;
        }
    }

    private async void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        if (_model == null) return;

        var elapsedSinceSync = DateTimeOffset.UtcNow - _clientTimeSyncPoint;
        var estimatedServerNow = _serverTimeSyncPoint + elapsedSinceSync;
        var remaining = _model.HoldUntil - estimatedServerNow;

        if (remaining <= TimeSpan.Zero)
        {
            StopCountdown();
            txtCountdownTimer.Text = "00:00";
            lblStatusMessage.Text = "Hết thời gian giữ chỗ! Đang cập nhật trạng thái từ máy chủ...";

            // Re-fetch from server to confirm expired status transition
            await LoadReservationAsync();
            return;
        }

        UpdateCountdownDisplay();
    }

    private void UpdateCountdownDisplay()
    {
        if (_model == null) return;

        var elapsedSinceSync = DateTimeOffset.UtcNow - _clientTimeSyncPoint;
        var estimatedServerNow = _serverTimeSyncPoint + elapsedSinceSync;
        var remaining = _model.HoldUntil - estimatedServerNow;

        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        txtCountdownTimer.Text = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadReservationAsync();
    }

    private async void BtnCancelReservation_Click(object sender, RoutedEventArgs e)
    {
        if (_model == null) return;

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn hủy đơn giữ chỗ {txtHeaderCode.Text} không?\n" +
            "Sau khi hủy, sức chứa sẽ được mở lại ngay lập tức cho khách hàng khác.",
            "Xác nhận Hủy Đơn",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        btnCancelReservation.IsEnabled = false;
        lblStatusMessage.Text = "Đang gửi yêu cầu hủy đơn...";

        var response = await ApiClient.Instance.CancelReservationAsync(_model.Id, "Khách hàng chủ động hủy trên ứng dụng.");

        if (!response.Success || response.Data == null)
        {
            lblStatusMessage.Text = $"Lỗi: {response.Message}";
            MessageBox.Show(response.Message, "Không thể hủy đơn", MessageBoxButton.OK, MessageBoxImage.Warning);
            btnCancelReservation.IsEnabled = true;
            return;
        }

        StopCountdown();
        _model = response.Data;
        BindModel(_model);

        MessageBox.Show("Đã hủy đơn giữ chỗ thành công.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
