using System.Windows;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class ConfirmReservationDialog : Window
{
    private readonly long _facilityId;
    private readonly long _unitTypeId;
    private readonly DateOnly _startDate;
    private readonly DateOnly _endDate;

    public ReservationDetailClientModel? CreatedReservation { get; private set; }

    public ConfirmReservationDialog(
        FacilityCatalogModel facility,
        FacilityUnitTypeCatalogModel unitType,
        DateOnly startDate,
        DateOnly endDate)
    {
        InitializeComponent();

        _facilityId = facility.Id;
        _unitTypeId = unitType.UnitTypeId;
        _startDate = startDate;
        _endDate = endDate;

        txtFacilityName.Text = $"{facility.Name} ({facility.Code})";
        txtFacilityAddress.Text = $"{facility.AddressLine}, {facility.District}, {facility.City}";
        txtUnitTypeName.Text = $"{unitType.Name} ({unitType.AreaM2:0.##} m²)" + (unitType.ClimateControlled ? " - Có điều hòa" : "");

        var months = (endDate.Year - startDate.Year) * 12 + (endDate.Month - startDate.Month);
        if (endDate.Day < startDate.Day) months--;
        months = Math.Max(1, months);

        txtRentalDates.Text = $"{startDate:dd/MM/yyyy} ➜ {endDate:dd/MM/yyyy} ({months} tháng)";
        txtMonthlyRate.Text = $"{unitType.MonthlyRate:N0} đ / tháng";
        txtDepositAmount.Text = $"{unitType.DepositAmount:N0} đ (100% 1 tháng)";
        txtBookingFee.Text = unitType.BookingFee > 0 ? $"{unitType.BookingFee:N0} đ" : "Miễn phí (0 đ)";

        var totalQuoted = unitType.DepositAmount + unitType.MonthlyRate + unitType.BookingFee;
        txtQuotedTotal.Text = $"{totalQuoted:N0} đ";
    }

    private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        btnConfirm.IsEnabled = false;
        btnCancel.IsEnabled = false;
        lblProcessingStatus.Text = "Đang gửi yêu cầu và thiết lập khóa giữ chỗ...";

        var request = new CreateReservationClientRequest
        {
            FacilityId = _facilityId,
            UnitTypeId = _unitTypeId,
            RentalStartDate = _startDate,
            RentalEndDate = _endDate
        };

        var response = await ApiClient.Instance.CreateReservationHoldAsync(request);

        if (!response.Success || response.Data == null)
        {
            lblProcessingStatus.Text = string.Empty;
            btnConfirm.IsEnabled = true;
            btnCancel.IsEnabled = true;

            MessageBox.Show(
                response.Message,
                "Không thể tạo đơn giữ chỗ",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        CreatedReservation = response.Data;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
