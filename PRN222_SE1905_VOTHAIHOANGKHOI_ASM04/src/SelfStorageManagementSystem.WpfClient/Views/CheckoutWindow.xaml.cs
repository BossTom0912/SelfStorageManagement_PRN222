using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class CheckoutWindow : Window
{
    private readonly long _reservationId;
    private CheckoutQuoteClientModel? _quote;
    private CheckoutClientResponse? _checkoutResponse;
    private DispatcherTimer? _countdownTimer;
    private DispatcherTimer? _pollTimer;
    private string _idempotencyKey = Guid.NewGuid().ToString("N");
    private bool _isCompleted;

    public CheckoutWindow(long reservationId)
    {
        InitializeComponent();
        _reservationId = reservationId;
        Loaded += CheckoutWindow_Loaded;
        Closed += CheckoutWindow_Closed;
    }

    private async void CheckoutWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadQuoteAsync();
    }

    private void CheckoutWindow_Closed(object? sender, EventArgs e)
    {
        StopTimers();
    }

    private void StopTimers()
    {
        if (_countdownTimer != null)
        {
            _countdownTimer.Stop();
            _countdownTimer = null;
        }

        if (_pollTimer != null)
        {
            _pollTimer.Stop();
            _pollTimer = null;
        }
    }

    private async Task LoadQuoteAsync(string? promotionCode = null)
    {
        lblStatusBottom.Text = "Đang tải báo giá thanh toán...";
        btnCheckout.IsEnabled = false;
        btnApplyPromo.IsEnabled = false;

        var response = await ApiClient.Instance.GetCheckoutQuoteAsync(_reservationId, promotionCode);

        btnApplyPromo.IsEnabled = true;

        if (!response.Success || response.Data == null)
        {
            lblStatusBottom.Text = $"Lỗi tải báo giá: {response.Message}";
            MessageBox.Show(response.Message, "Không thể tải báo giá", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _quote = response.Data;
        BindQuote(_quote);
        lblStatusBottom.Text = "Sẵn sàng thanh toán.";
    }

    private void BindQuote(CheckoutQuoteClientModel quote)
    {
        txtReservationCode.Text = quote.ReservationCode;
        txtFacilityAndUnitType.Text = $"{quote.FacilityName} ({quote.UnitTypeName})";
        txtRentalPeriod.Text = $"{quote.StartDate:dd/MM/yyyy} -> {quote.EndDate:dd/MM/yyyy} ({quote.RentalMonths} tháng)";

        txtFirstMonthRent.Text = $"{quote.MonthlyRateSnapshot:N0} đ";
        txtDepositAmount.Text = $"{quote.DepositSnapshot:N0} đ";
        txtBookingFee.Text = quote.BookingFeeSnapshot > 0 ? $"{quote.BookingFeeSnapshot:N0} đ" : "0 đ (Miễn phí)";
        txtDiscountAmount.Text = quote.DiscountAmount > 0 ? $"-{quote.DiscountAmount:N0} đ" : "0 đ";
        txtTotalAmount.Text = $"{quote.QuotedTotal:N0} đ";

        lblPolicyVersionHeader.Text = $"Phiên bản điều khoản áp dụng: {quote.PolicyVersion}";
        txtPolicyTermsContent.Text = FormatPolicyTerms(quote.PolicyContentJson);

        if (!string.IsNullOrWhiteSpace(quote.PromotionCode))
        {
            txtPromotionInput.Text = quote.PromotionCode;
            lblPromoFeedback.Text = $"✓ Đã áp dụng mã {quote.PromotionCode}: {quote.PromotionName ?? ""} (-{quote.DiscountAmount:N0} đ)";
            lblPromoFeedback.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
        }
        else
        {
            lblPromoFeedback.Text = string.Empty;
        }

        if (quote.IsHoldActive)
        {
            StartCountdown(quote.HoldUntil);
        }
        else
        {
            txtHoldCountdown.Text = "00:00";
            txtHoldCountdown.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            lblStatusBottom.Text = "Đơn giữ chỗ đã hết hạn, không thể tiếp tục thanh toán.";
            btnCheckout.IsEnabled = false;
            return;
        }

        UpdateCheckoutButtonState();
    }

    private void StartCountdown(DateTimeOffset holdUntil)
    {
        if (_countdownTimer != null)
        {
            _countdownTimer.Stop();
            _countdownTimer = null;
        }

        UpdateCountdownDisplay(holdUntil);

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += (s, e) =>
        {
            var remaining = holdUntil - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _countdownTimer?.Stop();
                txtHoldCountdown.Text = "00:00";
                txtHoldCountdown.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                lblStatusBottom.Text = "Đơn giữ chỗ đã hết hạn thời gian.";
                btnCheckout.IsEnabled = false;
                return;
            }
            UpdateCountdownDisplay(holdUntil);
        };
        _countdownTimer.Start();
    }

    private void UpdateCountdownDisplay(DateTimeOffset holdUntil)
    {
        var remaining = holdUntil - DateTimeOffset.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        txtHoldCountdown.Text = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
    }

    private void RbGateway_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (panelWaitingPayment != null && btnSimulateSuccessNow != null)
        {
            btnSimulateSuccessNow.Visibility = (rbDemoGateway.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateCheckoutButtonState()
    {
        btnCheckout.IsEnabled = chkAcceptTerms.IsChecked == true && _quote != null && _quote.IsHoldActive && _checkoutResponse == null;
    }

    private void ChkAcceptTerms_CheckedChanged(object sender, RoutedEventArgs e)
    {
        UpdateCheckoutButtonState();
    }

    private async void BtnApplyPromo_Click(object sender, RoutedEventArgs e)
    {
        var code = txtPromotionInput.Text.Trim();
        await LoadQuoteAsync(code);
    }

    private async void BtnCheckout_Click(object sender, RoutedEventArgs e)
    {
        if (_quote == null) return;

        if (chkAcceptTerms.IsChecked != true)
        {
            MessageBox.Show("Vui lòng đồng ý với Điều khoản hợp đồng thuê kho trước khi thanh toán.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var method = rbVnpayGateway.IsChecked == true ? "vnpay" : "demo";

        btnCheckout.IsEnabled = false;
        btnApplyPromo.IsEnabled = false;
        txtPromotionInput.IsEnabled = false;
        lblStatusBottom.Text = "Đang khởi tạo phiên thanh toán...";

        var request = new CheckoutClientRequest
        {
            ReservationId = _reservationId,
            PromotionCode = string.IsNullOrWhiteSpace(_quote.PromotionCode) ? null : _quote.PromotionCode,
            AcceptedPolicyVersionId = _quote.PolicyVersionId,
            PaymentMethod = method
        };

        var response = await ApiClient.Instance.CheckoutAsync(request, _idempotencyKey);

        if (!response.Success || response.Data == null)
        {
            lblStatusBottom.Text = $"Lỗi thanh toán: {response.Message}";
            MessageBox.Show(response.Message, "Thanh toán không thành công", MessageBoxButton.OK, MessageBoxImage.Warning);
            _idempotencyKey = Guid.NewGuid().ToString("N"); // Refresh idempotency key to guide creating a new attempt
            btnCheckout.IsEnabled = true;
            btnApplyPromo.IsEnabled = true;
            txtPromotionInput.IsEnabled = true;
            return;
        }

        _checkoutResponse = response.Data;
        lblStatusBottom.Text = "Đã khởi tạo giao dịch thanh toán thành công.";

        panelWaitingPayment.Visibility = Visibility.Visible;
        btnSimulateSuccessNow.Visibility = (rbDemoGateway.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        lblPaymentAttemptInfo.Text = $"Giao dịch #{_checkoutResponse.PaymentId} ({_checkoutResponse.Provider}). Tổng tiền: {_checkoutResponse.Amount:N0} đ. Đang mở cổng thanh toán...";

        // Open checkout URL in default browser only if URL is present and attempt is payable
        if (!string.IsNullOrWhiteSpace(_checkoutResponse.CheckoutUrl))
        {
            OpenBrowser(_checkoutResponse.CheckoutUrl);
        }

        // Start polling payment status
        StartPaymentPolling(_checkoutResponse.PaymentId);
    }

    private void OpenBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể tự động mở trình duyệt: {ex.Message}\nVui lòng truy cập thủ công:\n{url}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnOpenBrowserAgain_Click(object sender, RoutedEventArgs e)
    {
        if (_checkoutResponse != null && !string.IsNullOrWhiteSpace(_checkoutResponse.CheckoutUrl))
        {
            OpenBrowser(_checkoutResponse.CheckoutUrl);
        }
    }

    private void StartPaymentPolling(long paymentId)
    {
        if (_pollTimer != null)
        {
            _pollTimer.Stop();
            _pollTimer = null;
        }

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _pollTimer.Tick += async (s, e) =>
        {
            var result = await ApiClient.Instance.GetPaymentByIdAsync(paymentId);
            if (!result.Success || result.Data == null) return;

            var payment = result.Data;
            if (payment.Status == "succeeded")
            {
                _pollTimer?.Stop();
                _pollTimer = null;
                OnPaymentSucceeded(payment);
            }
            else if (payment.Status == "failed")
            {
                _pollTimer?.Stop();
                _pollTimer = null;
                OnPaymentFailed(payment);
            }
        };
        _pollTimer.Start();
    }

    private async void BtnSimulateSuccessNow_Click(object sender, RoutedEventArgs e)
    {
        if (_checkoutResponse == null) return;

        btnSimulateSuccessNow.IsEnabled = false;
        lblStatusBottom.Text = "Đang gửi giả lập thanh toán thành công lên máy chủ...";

        var result = await ApiClient.Instance.CompleteDemoPaymentAsync(_checkoutResponse.PaymentId, isSuccess: true);

        btnSimulateSuccessNow.IsEnabled = true;

        if (!result.Success || result.Data == null)
        {
            MessageBox.Show(result.Message, "Lỗi giả lập thanh toán", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (result.Data.Status == "succeeded")
        {
            _pollTimer?.Stop();
            _pollTimer = null;
            OnPaymentSucceeded(result.Data);
        }
        else if (result.Data.Status == "failed")
        {
            _pollTimer?.Stop();
            _pollTimer = null;
            OnPaymentFailed(result.Data);
        }
    }

    private void OnPaymentSucceeded(PaymentDetailClientModel payment)
    {
        _isCompleted = true;
        StopTimers();

        if (payment.ReconciliationRequired)
        {
            txtReconReservationCode.Text = payment.ReservationCode ?? _quote?.ReservationCode ?? $"#{_reservationId}";
            txtReconPaymentId.Text = $"#{payment.PaymentId}";
            txtReconPaidAmount.Text = $"{payment.Amount:N0} đ";

            gridMainForm.Visibility = Visibility.Collapsed;
            panelSuccessOutcome.Visibility = Visibility.Collapsed;
            panelReconciliationOutcome.Visibility = Visibility.Visible;
            return;
        }

        txtSuccessReservationCode.Text = payment.ReservationCode ?? _quote?.ReservationCode ?? $"#{_reservationId}";
        txtSuccessInvoiceNo.Text = payment.TargetInvoiceId > 0 ? $"INV-{payment.TargetInvoiceId:D6}" : "Đã thanh toán";
        txtSuccessAgreementNo.Text = payment.AgreementNo ?? (payment.AgreementId.HasValue ? $"AGR-{payment.AgreementId.Value:D6}" : "Scheduled (Chờ bàn giao)");
        txtSuccessPaidAmount.Text = $"{payment.Amount:N0} đ";

        gridMainForm.Visibility = Visibility.Collapsed;
        panelReconciliationOutcome.Visibility = Visibility.Collapsed;
        panelSuccessOutcome.Visibility = Visibility.Visible;
    }

    private void OnPaymentFailed(PaymentDetailClientModel payment)
    {
        panelWaitingPayment.Visibility = Visibility.Collapsed;
        btnCheckout.IsEnabled = true;
        btnApplyPromo.IsEnabled = true;
        txtPromotionInput.IsEnabled = true;
        _checkoutResponse = null;
        _idempotencyKey = Guid.NewGuid().ToString("N");

        var reason = payment.FailureReason ?? "Giao dịch thanh toán bị hủy hoặc không thành công.";
        MessageBox.Show($"Giao dịch thất bại: {reason}\nBạn có thể thử thanh toán lại trong thời gian giữ chỗ.", "Thanh toán thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string FormatPolicyTerms(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "1. Khách hàng cam kết sử dụng ô kho đúng mục đích lưu trữ hợp pháp.\n" +
                   "2. Khoản tiền cọc tương đương 1 tháng tiền thuê sẽ được hoàn trả khi kết thúc hợp đồng theo quy định.\n" +
                   "3. Hợp đồng có hiệu lực sau khi thanh toán thành công và nhận bàn giao ô kho thực tế tại quầy (Check-in).";
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        sb.AppendLine($"• {prop.Name}:");
                        int idx = 1;
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            sb.AppendLine($"  {idx++}. {item.GetString() ?? item.ToString()}");
                        }
                    }
                    else
                    {
                        var val = prop.Value.GetString() ?? prop.Value.ToString();
                        sb.AppendLine($"• {prop.Name}: {val}");
                    }
                }
                var formatted = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(formatted)) return formatted;
            }
        }
        catch
        {
            // Fallback if not valid JSON
        }

        return json;
    }

    private void BtnSuccessDone_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _isCompleted;
        Close();
    }
}
