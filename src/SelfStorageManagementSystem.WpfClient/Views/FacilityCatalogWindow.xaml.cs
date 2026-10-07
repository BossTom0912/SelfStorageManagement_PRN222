using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;

namespace SelfStorageManagementSystem.WpfClient.Views;

public partial class FacilityCatalogWindow : Window
{
    public sealed record AppliedFilterSnapshot(
        DateOnly StartDate,
        DateOnly EndDate,
        decimal? MaxPrice,
        decimal? MinAreaM2,
        decimal? MaxAreaM2,
        bool? ClimateControlled);

    private int _facilityPage = 1;
    private const int FacilityPageSize = 10;
    private int _unitPage = 1;
    private const int UnitPageSize = 10;

    // Sequence tokens to guard against stale async responses (Bug 4)
    private int _catalogContextVersion = 0;
    private int _loadedUnitTypesContextVersion = -1;
    private int _loadedAvailableUnitsContextVersion = -1;
    private int _loadedFloorMapContextVersion = -1;

    private int _facilityRequestVersion = 0;
    private int _unitTypesRequestVersion = 0;
    private int _availableUnitsRequestVersion = 0;
    private int _floorMapRequestVersion = 0;

    private bool _isInitializing = true;
    private bool _hasUnappliedFilterChanges = false;
    private FacilityCatalogModel? _selectedFacility;
    private FacilityUnitTypeCatalogModel? _selectedUnitType;
    private UnitMapItemModel? _selectedMapItem;
    private FacilityFloorMapModel? _cachedFloorMap;
    private AppliedFilterSnapshot? _appliedFilter;

    public FacilityCatalogWindow()
    {
        InitializeComponent();
        InitializeDates();
        UpdateUserSessionHeader();
        _isInitializing = false;

        txtFilterMaxPrice.TextChanged += (_, _) => OnFilterInputChanged();
        txtFilterMinArea.TextChanged += (_, _) => OnFilterInputChanged();
        txtFilterMaxArea.TextChanged += (_, _) => OnFilterInputChanged();
        chkFilterClimate.Checked += (_, _) => OnFilterInputChanged();
        chkFilterClimate.Unchecked += (_, _) => OnFilterInputChanged();

        Loaded += async (_, _) => await LoadFacilitiesAsync();
    }

    private void InitializeDates()
    {
        var today = DateTime.Today;
        var start = today.AddDays(1);
        var end = today.AddDays(1).AddMonths(1);
        dpStartDate.SelectedDate = start;
        dpEndDate.SelectedDate = end;
        _appliedFilter = new AppliedFilterSnapshot(
            DateOnly.FromDateTime(start),
            DateOnly.FromDateTime(end),
            null, null, null, null);
    }

    private void UpdateUserSessionHeader()
    {
        if (SessionStore.IsAuthenticated && SessionStore.CurrentUser != null)
        {
            var user = SessionStore.CurrentUser;
            var roles = string.Join(", ", user.Roles);
            lblUserSession.Text = $"{user.FullName} ({roles})";
        }
        else
        {
            lblUserSession.Text = "Khách vãng lai (Public Catalog)";
        }
    }

    private void OnFilterInputChanged()
    {
        if (_isInitializing) return;

        _hasUnappliedFilterChanges = true;
        ResetAllSelections();
        if (_selectedFacility != null)
        {
            lblStatusMessage.Text = "Bộ lọc đã thay đổi (chưa áp dụng). Vui lòng bấm 'Áp dụng lọc' để cập nhật dữ liệu và tiếp tục.";
        }
    }

    public static (bool IsValid, decimal? Value, string? ErrorMessage) ParseVndPrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (true, null, null);
        }

        var trimmed = text.Trim();

        // Check if thousand separators are used (Vietnamese '.' or standard ',')
        string cleanString = trimmed;
        if (trimmed.Contains('.'))
        {
            var parts = trimmed.Split('.');
            bool isGrouping = parts.Length > 1 
                && parts[0].Length >= 1 && parts[0].Length <= 3 
                && parts.All(p => p.Length > 0 && p.All(char.IsDigit)) 
                && parts.Skip(1).All(p => p.Length == 3);

            if (isGrouping)
            {
                cleanString = string.Concat(parts);
            }
            else
            {
                return (false, null, "Giá tối đa không đúng định dạng số tiền VND (ví dụ: 1500000 hoặc 1.500.000).");
            }
        }
        else if (trimmed.Contains(','))
        {
            var parts = trimmed.Split(',');
            bool isGrouping = parts.Length > 1 
                && parts[0].Length >= 1 && parts[0].Length <= 3 
                && parts.All(p => p.Length > 0 && p.All(char.IsDigit)) 
                && parts.Skip(1).All(p => p.Length == 3);

            if (isGrouping)
            {
                cleanString = string.Concat(parts);
            }
            else
            {
                return (false, null, "Giá tối đa không đúng định dạng số tiền VND (ví dụ: 1500000 hoặc 1,500,000).");
            }
        }

        if (!long.TryParse(cleanString, out var val) || val < 0)
        {
            return (false, null, "Giá tối đa phải là số nguyên không âm (ví dụ: 1500000 hoặc 1.500.000).");
        }

        return (true, (decimal)val, null);
    }

    public static (bool IsValid, decimal? Value, string? ErrorMessage) ParseArea(string? text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (true, null, null);
        }

        var trimmed = text.Trim().Replace(',', '.');

        if (!decimal.TryParse(trimmed, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var val) || val < 0)
        {
            return (false, null, $"{fieldName} phải là số không âm hợp lệ (ví dụ: 2.5 hoặc 10).");
        }

        return (true, val, null);
    }

    private void ResetMapSelection()
    {
        _selectedMapItem = null;
        if (lblMapSelectionTitle != null) lblMapSelectionTitle.Text = "Click vào một ô trên sơ đồ để xem thông tin chi tiết.";
        if (lblMapSelectionDesc != null) lblMapSelectionDesc.Text = "Theo quy tắc BR-RSV-03: Ô trên sơ đồ dùng để tham khảo vị trí và loại kho; nút đặt chỗ sẽ ghi nhận theo Loại Kho.";
        if (btnProceedFromMap != null) btnProceedFromMap.IsEnabled = false;
    }

    private void ResetAllSelections()
    {
        _selectedUnitType = null;
        if (gridUnitTypes != null) gridUnitTypes.SelectedItem = null;
        if (lblSelectedTypeSummary != null) lblSelectedTypeSummary.Text = "Chưa chọn loại kho nào.";
        if (btnProceedWithType != null) btnProceedWithType.IsEnabled = false;

        ResetMapSelection();
    }

    private async Task LoadFacilitiesAsync()
    {
        var currentVersion = ++_facilityRequestVersion;

        lblFacilityLoading.Visibility = Visibility.Visible;
        lblStatusMessage.Text = "Đang tải danh sách cơ sở kho...";

        var city = txtFilterCity.Text?.Trim();
        var district = txtFilterDistrict.Text?.Trim();
        var search = txtFilterSearch.Text?.Trim();

        var response = await ApiClient.Instance.GetCatalogFacilitiesAsync(
            string.IsNullOrWhiteSpace(city) ? null : city,
            string.IsNullOrWhiteSpace(district) ? null : district,
            string.IsNullOrWhiteSpace(search) ? null : search,
            _facilityPage,
            FacilityPageSize);

        if (currentVersion != _facilityRequestVersion) return;

        lblFacilityLoading.Visibility = Visibility.Collapsed;

        if (!response.Success || response.Data == null)
        {
            lblStatusMessage.Text = $"Lỗi tải cơ sở: {response.Message}";
            MessageBox.Show(response.Message, "Lỗi tải cơ sở", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = response.Data;
        lstFacilities.ItemsSource = result.Items;
        lblFacilityPageInfo.Text = $"Trang {result.PageNumber} / {Math.Max(1, result.TotalPages)} ({result.TotalCount} cơ sở)";
        btnFacilityPrev.IsEnabled = result.HasPreviousPage;
        btnFacilityNext.IsEnabled = result.HasNextPage;
        lblStatusMessage.Text = $"Đã tải {result.Items.Count} cơ sở.";
    }

    private async void BtnSearchFacilities_Click(object sender, RoutedEventArgs e)
    {
        _facilityPage = 1;
        await LoadFacilitiesAsync();
    }

    private async void BtnFacilityPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_facilityPage > 1)
        {
            _facilityPage--;
            await LoadFacilitiesAsync();
        }
    }

    private async void BtnFacilityNext_Click(object sender, RoutedEventArgs e)
    {
        _facilityPage++;
        await LoadFacilitiesAsync();
    }

    private async void LstFacilities_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstFacilities.SelectedItem is not FacilityCatalogModel facility)
        {
            panelNoFacility.Visibility = Visibility.Visible;
            _selectedFacility = null;
            ResetAllSelections();
            _cachedFloorMap = null;
            cboMapArea.ItemsSource = null;
            canvasFloorMap.Children.Clear();
            gridAvailableUnits.ItemsSource = null;
            gridUnitTypes.ItemsSource = null;
            _loadedUnitTypesContextVersion = -1;
            _loadedAvailableUnitsContextVersion = -1;
            _loadedFloorMapContextVersion = -1;
            return;
        }

        _selectedFacility = facility;
        _catalogContextVersion++;
        _loadedUnitTypesContextVersion = -1;
        _loadedAvailableUnitsContextVersion = -1;
        _loadedFloorMapContextVersion = -1;
        panelNoFacility.Visibility = Visibility.Collapsed;

        txtSelectedFacilityTitle.Text = $"{facility.Name} ({facility.Code})";
        txtSelectedFacilityDetails.Text = $"Địa chỉ: {facility.FullAddress} | Giờ hoạt động: {facility.OperatingHoursDisplay}";
        txtSelectedFacilityTz.Text = $"Múi giờ: {facility.Timezone}";

        // Reset old selections and caches when facility changes (Finding A & D)
        ResetAllSelections();
        _cachedFloorMap = null;
        cboMapArea.ItemsSource = null;
        canvasFloorMap.Children.Clear();
        gridAvailableUnits.ItemsSource = null;
        gridUnitTypes.ItemsSource = null;

        _unitPage = 1;
        await RefreshSelectedFacilityDataAsync();
    }

    private async Task RefreshSelectedFacilityDataAsync()
    {
        if (_selectedFacility == null) return;

        if (_hasUnappliedFilterChanges)
        {
            lblStatusMessage.Text = "Bộ lọc đang có thay đổi chưa áp dụng. Vui lòng bấm 'Áp dụng lọc' để tải dữ liệu cho cơ sở này.";
            ResetAllSelections();
            return;
        }

        var selectedTab = tabCatalog.SelectedIndex;
        if (selectedTab == 0)
        {
            await LoadUnitTypesAsync();
        }
        else if (selectedTab == 1)
        {
            await LoadAvailableUnitsAsync();
        }
        else if (selectedTab == 2)
        {
            await LoadFloorMapAsync();
        }
    }

    private async Task LoadUnitTypesAsync()
    {
        if (_selectedFacility == null || _hasUnappliedFilterChanges) return;

        var contextVersion = _catalogContextVersion;
        var currentVersion = ++_unitTypesRequestVersion;
        var currentFacilityId = _selectedFacility.Id;

        lblStatusMessage.Text = "Đang tải danh mục loại kho và bảng giá...";
        var startDate = _appliedFilter?.StartDate;
        var endDate = _appliedFilter?.EndDate;
        var maxPrice = _appliedFilter?.MaxPrice;
        var minArea = _appliedFilter?.MinAreaM2;
        var maxArea = _appliedFilter?.MaxAreaM2;
        var climate = _appliedFilter?.ClimateControlled;

        var response = await ApiClient.Instance.GetFacilityUnitTypesAsync(
            _selectedFacility.Id,
            startDate,
            endDate,
            maxPrice,
            climate,
            minArea,
            maxArea);

        if (contextVersion != _catalogContextVersion || currentVersion != _unitTypesRequestVersion || _selectedFacility?.Id != currentFacilityId) return;

        if (!response.Success || response.Data == null)
        {
            lblStatusMessage.Text = $"Lỗi tải loại kho: {response.Message}";
            MessageBox.Show(response.Message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _loadedUnitTypesContextVersion = contextVersion;
        gridUnitTypes.ItemsSource = response.Data;
        lblStatusMessage.Text = $"Đã tải {response.Data.Count} loại kho tại {_selectedFacility.Name}.";
    }

    private async Task LoadAvailableUnitsAsync()
    {
        if (_selectedFacility == null || _hasUnappliedFilterChanges) return;

        var contextVersion = _catalogContextVersion;
        var currentVersion = ++_availableUnitsRequestVersion;
        var currentFacilityId = _selectedFacility.Id;

        lblStatusMessage.Text = "Đang tải danh sách ô kho khả dụng...";
        var startDate = _appliedFilter?.StartDate;
        var endDate = _appliedFilter?.EndDate;
        var maxPrice = _appliedFilter?.MaxPrice;
        var minArea = _appliedFilter?.MinAreaM2;
        var maxArea = _appliedFilter?.MaxAreaM2;
        var climate = _appliedFilter?.ClimateControlled;

        var response = await ApiClient.Instance.GetAvailableUnitsAsync(
            _selectedFacility.Id,
            _selectedUnitType?.UnitTypeId,
            null,
            climate,
            maxPrice,
            minArea,
            maxArea,
            startDate,
            endDate,
            _unitPage,
            UnitPageSize);

        if (contextVersion != _catalogContextVersion || currentVersion != _availableUnitsRequestVersion || _selectedFacility?.Id != currentFacilityId) return;

        if (!response.Success || response.Data == null)
        {
            lblStatusMessage.Text = $"Lỗi tải ô kho: {response.Message}";
            MessageBox.Show(response.Message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _loadedAvailableUnitsContextVersion = contextVersion;
        var result = response.Data;
        gridAvailableUnits.ItemsSource = result.Items;
        lblUnitPageInfo.Text = $"Trang {result.PageNumber} / {Math.Max(1, result.TotalPages)} ({result.TotalCount} ô trống ứng viên)";
        btnUnitPrev.IsEnabled = result.HasPreviousPage;
        btnUnitNext.IsEnabled = result.HasNextPage;
        lblStatusMessage.Text = $"Đã tải {result.Items.Count} ô trống ứng viên (trên tổng {result.TotalCount} ô đủ điều kiện). Theo BR-RSV-03, sức chứa nhận đặt được quản lý tại Tab 1 theo loại kho.";
    }

    private async Task LoadFloorMapAsync()
    {
        if (_selectedFacility == null || _hasUnappliedFilterChanges) return;

        var contextVersion = _catalogContextVersion;
        var currentVersion = ++_floorMapRequestVersion;
        var currentFacilityId = _selectedFacility.Id;

        lblStatusMessage.Text = "Đang tải sơ đồ mặt bằng...";
        var startDate = _appliedFilter?.StartDate;
        var endDate = _appliedFilter?.EndDate;

        // Finding A: Always query complete floor map (areaId = null) and filter locally in RenderFloorMap
        var response = await ApiClient.Instance.GetFacilityFloorMapAsync(
            _selectedFacility.Id,
            areaId: null,
            startDate,
            endDate);

        if (contextVersion != _catalogContextVersion || currentVersion != _floorMapRequestVersion || _selectedFacility?.Id != currentFacilityId) return;

        if (!response.Success || response.Data == null)
        {
            lblStatusMessage.Text = $"Lỗi tải sơ đồ: {response.Message}";
            MessageBox.Show(response.Message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _loadedFloorMapContextVersion = contextVersion;
        _cachedFloorMap = response.Data;

        // Reset map selection when newly loaded (Bug 3)
        ResetMapSelection();

        // Repopulate area dropdown cleanly for the selected facility
        var areaList = new List<FacilityAreaModel>
        {
            new() { Id = 0, Name = "Tất cả các khu vực" }
        };
        areaList.AddRange(_cachedFloorMap.Areas);
        cboMapArea.ItemsSource = areaList;
        cboMapArea.SelectedIndex = 0;

        lblMapUnplacedCount.Text = $"Ô chưa định vị: {_cachedFloorMap.TotalUnitsWithoutMap}";
        RenderFloorMap();
        lblStatusMessage.Text = $"Đã vẽ sơ đồ {_cachedFloorMap.TotalUnitsWithMap} ô kho tại {_selectedFacility.Name}.";
    }

    private void RenderFloorMap()
    {
        canvasFloorMap.Children.Clear();
        if (_cachedFloorMap == null) return;

        long selectedAreaId = 0;
        if (cboMapArea.SelectedValue is long aid)
        {
            selectedAreaId = aid;
        }

        var unitsToRender = selectedAreaId > 0
            ? _cachedFloorMap.Units.Where(u => u.AreaId == selectedAreaId).ToList()
            : _cachedFloorMap.Units;

        double maxX = 800;
        double maxY = 600;

        foreach (var unit in unitsToRender)
        {
            var width = Math.Max(40, (double)unit.Width);
            var height = Math.Max(30, (double)unit.Height);
            var posX = (double)unit.X;
            var posY = (double)unit.Y;

            if (posX + width > maxX) maxX = posX + width + 50;
            if (posY + height > maxY) maxY = posY + height + 50;

            var border = new Border
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(unit.StatusColorHex)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand,
                Tag = unit,
                ToolTip = $"{unit.UnitCode} ({unit.UnitTypeName})\nTrạng thái: {unit.StatusDisplayName}\nDiện tích: {unit.AreaM2:0.##} m²\nGiá niêm yết: {(unit.MonthlyRate.HasValue ? $"{unit.MonthlyRate.Value:N0} đ/tháng" : "N/A")}"
            };

            var text = new TextBlock
            {
                Text = unit.UnitCode,
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            border.Child = text;
            border.MouseLeftButtonUp += UnitMapBorder_MouseLeftButtonUp;

            Canvas.SetLeft(border, posX);
            Canvas.SetTop(border, posY);
            canvasFloorMap.Children.Add(border);
        }

        canvasFloorMap.Width = Math.Max(1000, maxX);
        canvasFloorMap.Height = Math.Max(700, maxY);
    }

    private void UnitMapBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not UnitMapItemModel unit) return;

        _selectedMapItem = unit;

        if (_hasUnappliedFilterChanges)
        {
            lblMapSelectionTitle.Text = $"Ô kho: {unit.UnitCode} | Loại kho: {unit.UnitTypeName}";
            lblMapSelectionDesc.Text = "Bộ lọc đã thay đổi nhưng chưa được áp dụng. Vui lòng bấm 'Áp dụng lọc' để cập nhật dữ liệu trước khi tiếp tục.";
            btnProceedFromMap.IsEnabled = false;
            return;
        }

        if (_loadedFloorMapContextVersion != _catalogContextVersion)
        {
            lblMapSelectionTitle.Text = $"Ô kho: {unit.UnitCode} | Loại kho: {unit.UnitTypeName}";
            lblMapSelectionDesc.Text = "Sơ đồ mặt bằng chưa được tải cho bộ lọc hiện tại. Vui lòng bấm 'Áp dụng lọc' để cập nhật dữ liệu.";
            btnProceedFromMap.IsEnabled = false;
            return;
        }

        lblMapSelectionTitle.Text = $"Ô kho: {unit.UnitCode} | Loại kho: {unit.UnitTypeName} ({unit.AreaM2:0.##} m²) | Trạng thái: {unit.StatusDisplayName}";

        // Consistent visual description according to BR-RSV-03 (Finding C)
        if (unit.CanSelectToProceed)
        {
            lblMapSelectionDesc.Text = $"Ô đang ở trạng thái Khả Dụng ({unit.StatusDisplayName}). Bạn có thể tiếp tục chọn Loại Kho '{unit.UnitTypeName}' để chuyển sang bước đặt chỗ (BR-RSV-03).";
        }
        else if (unit.DisplayStatus == "available")
        {
            lblMapSelectionDesc.Text = $"Ô đang ở trạng thái Trống tham khảo trên sơ đồ, nhưng Loại Kho '{unit.UnitTypeName}' hiện đã hết sức chứa nhận đặt hoặc chưa có biểu phí hợp lệ (BR-RSV-03). Không thể tiếp tục đặt loại kho này.";
        }
        else
        {
            lblMapSelectionDesc.Text = $"Ô đang ở trạng thái '{unit.StatusDisplayName}'. Theo quy định BR-OPS-02, ô này không thể chọn để thuê.";
        }

        btnProceedFromMap.IsEnabled = unit.CanSelectToProceed;
    }

    private void CboMapArea_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Bug 3: Reset map selection and proceed button when changing area without affecting Tab 1
        ResetMapSelection();
        RenderFloorMap();
    }

    private async void TabCatalog_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl)
        {
            if (_hasUnappliedFilterChanges)
            {
                lblStatusMessage.Text = "Bộ lọc đang có thay đổi chưa áp dụng. Vui lòng bấm 'Áp dụng lọc' để cập nhật dữ liệu cho tab này.";
                ResetAllSelections();
                return;
            }

            await RefreshSelectedFacilityDataAsync();
        }
    }

    private void DpDates_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        // Bug 5: Synchronous event notification of changed filter input
        OnFilterInputChanged();
    }

    private async void BtnApplyDateFilter_Click(object sender, RoutedEventArgs e)
    {
        if (dpStartDate.SelectedDate == null || dpEndDate.SelectedDate == null)
        {
            MessageBox.Show("Vui lòng chọn đầy đủ ngày bắt đầu và ngày kết thúc thuê.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (dpEndDate.SelectedDate <= dpStartDate.SelectedDate)
        {
            MessageBox.Show("Ngày kết thúc thuê phải lớn hơn ngày bắt đầu thuê.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Bug 6: Strict 3-state validation for max price
        var (priceOk, maxPrice, priceErr) = ParseVndPrice(txtFilterMaxPrice.Text);
        if (!priceOk)
        {
            MessageBox.Show(priceErr, "Lỗi bộ lọc giá", MessageBoxButton.OK, MessageBoxImage.Warning);
            lblStatusMessage.Text = priceErr;
            return;
        }

        // Bug 6: Strict 3-state validation for min/max area
        var (minAreaOk, minArea, minAreaErr) = ParseArea(txtFilterMinArea.Text, "Diện tích tối thiểu");
        if (!minAreaOk)
        {
            MessageBox.Show(minAreaErr, "Lỗi bộ lọc diện tích", MessageBoxButton.OK, MessageBoxImage.Warning);
            lblStatusMessage.Text = minAreaErr;
            return;
        }

        var (maxAreaOk, maxArea, maxAreaErr) = ParseArea(txtFilterMaxArea.Text, "Diện tích tối đa");
        if (!maxAreaOk)
        {
            MessageBox.Show(maxAreaErr, "Lỗi bộ lọc diện tích", MessageBoxButton.OK, MessageBoxImage.Warning);
            lblStatusMessage.Text = maxAreaErr;
            return;
        }

        if (minArea.HasValue && maxArea.HasValue && minArea.Value > maxArea.Value)
        {
            MessageBox.Show("Diện tích tối thiểu không được lớn hơn diện tích tối đa.", "Lỗi bộ lọc diện tích", MessageBoxButton.OK, MessageBoxImage.Warning);
            lblStatusMessage.Text = "Diện tích tối thiểu không được lớn hơn diện tích tối đa.";
            return;
        }

        // Capture snapshot of applied filters (Bug 5)
        _appliedFilter = new AppliedFilterSnapshot(
            DateOnly.FromDateTime(dpStartDate.SelectedDate.Value),
            DateOnly.FromDateTime(dpEndDate.SelectedDate.Value),
            maxPrice,
            minArea,
            maxArea,
            chkFilterClimate.IsChecked == true ? true : null);

        // Mark filter changes as applied
        _hasUnappliedFilterChanges = false;

        // Advance context version to invalidate any pending in-flight requests and mark other tabs as stale (Bug 4 & B)
        _catalogContextVersion++;
        _loadedUnitTypesContextVersion = -1;
        _loadedAvailableUnitsContextVersion = -1;
        _loadedFloorMapContextVersion = -1;

        // Invalidate stale data on other tabs so they cannot be clicked during or before loading
        ResetAllSelections();
        _cachedFloorMap = null;
        canvasFloorMap.Children.Clear();
        gridAvailableUnits.ItemsSource = null;
        gridUnitTypes.ItemsSource = null;

        _unitPage = 1;
        await RefreshSelectedFacilityDataAsync();
    }

    private void GridUnitTypes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (gridUnitTypes.SelectedItem is FacilityUnitTypeCatalogModel unitType)
        {
            _selectedUnitType = unitType;

            if (_hasUnappliedFilterChanges)
            {
                lblSelectedTypeSummary.Text = $"Đã chọn: {unitType.Name}. Lưu ý: Bộ lọc đã thay đổi nhưng chưa áp dụng. Bấm 'Áp dụng lọc' để tiếp tục.";
                btnProceedWithType.IsEnabled = false;
                return;
            }

            if (_loadedUnitTypesContextVersion != _catalogContextVersion)
            {
                lblSelectedTypeSummary.Text = $"Đã chọn: {unitType.Name}. Dữ liệu chưa được tải cho bộ lọc hiện tại. Bấm 'Áp dụng lọc' để cập nhật.";
                btnProceedWithType.IsEnabled = false;
                return;
            }

            lblSelectedTypeSummary.Text = $"Đã chọn loại kho: {unitType.Name} ({unitType.DimensionsDisplay}) - Giá: {unitType.MonthlyRateDisplay} - Khả dụng tham khảo: {unitType.EstimatedAvailableUnits} ô";
            btnProceedWithType.IsEnabled = unitType.EstimatedAvailableUnits > 0;
        }
        else
        {
            _selectedUnitType = null;
            lblSelectedTypeSummary.Text = "Chưa chọn loại kho nào.";
            btnProceedWithType.IsEnabled = false;
        }
    }

    private async void BtnProceedWithType_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFacility == null || _selectedUnitType == null) return;

        if (_hasUnappliedFilterChanges)
        {
            MessageBox.Show("Bộ lọc đã bị thay đổi nhưng chưa được áp dụng. Vui lòng bấm 'Áp dụng lọc' để cập nhật dữ liệu trước khi tiếp tục.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_loadedUnitTypesContextVersion != _catalogContextVersion)
        {
            MessageBox.Show("Dữ liệu loại kho chưa được tải thành công cho bộ lọc hiện tại. Vui lòng bấm 'Áp dụng lọc' để cập nhật.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_appliedFilter == null)
        {
            MessageBox.Show("Vui lòng áp dụng bộ lọc trước khi tiếp tục.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Guard against mismatched facility and unit type
        if (_selectedUnitType.FacilityId != _selectedFacility.Id)
        {
            MessageBox.Show("Loại kho đã chọn không thuộc về cơ sở hiện tại. Vui lòng chọn lại.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            ResetAllSelections();
            return;
        }

        if (_selectedUnitType.EstimatedAvailableUnits <= 0)
        {
            MessageBox.Show("Loại kho này hiện không còn ô khả dụng để nhận đặt.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await InitiateReservationFlowAsync(_selectedUnitType);
    }

    private async void BtnProceedFromMap_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFacility == null || _selectedMapItem == null || _cachedFloorMap == null) return;

        if (_hasUnappliedFilterChanges)
        {
            MessageBox.Show("Bộ lọc đã bị thay đổi nhưng chưa được áp dụng. Vui lòng bấm 'Áp dụng lọc' để cập nhật dữ liệu trước khi tiếp tục.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_loadedFloorMapContextVersion != _catalogContextVersion)
        {
            MessageBox.Show("Dữ liệu sơ đồ mặt bằng chưa được tải thành công cho bộ lọc hiện tại. Vui lòng bấm 'Áp dụng lọc' để cập nhật.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_appliedFilter == null)
        {
            MessageBox.Show("Vui lòng áp dụng bộ lọc trước khi tiếp tục.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Bug 3: Verify unit still belongs to cached floor map
        if (!_cachedFloorMap.Units.Contains(_selectedMapItem))
        {
            MessageBox.Show("Ô kho đã chọn không còn tồn tại trên sơ đồ hiện tại. Vui lòng chọn lại.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            ResetMapSelection();
            return;
        }

        // Bug 3: Verify unit matches current area filter if filtered
        if (cboMapArea.SelectedValue is long selectedAreaId && selectedAreaId > 0)
        {
            if (_selectedMapItem.AreaId != selectedAreaId)
            {
                MessageBox.Show("Ô kho đã chọn không thuộc khu vực đang hiển thị. Vui lòng chọn lại.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetMapSelection();
                return;
            }
        }

        if (!_selectedMapItem.CanSelectToProceed)
        {
            MessageBox.Show("Ô kho hoặc loại kho này hiện không đủ điều kiện để tiếp tục đặt chỗ.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_hasUnappliedFilterChanges)
        {
            MessageBox.Show("Bộ lọc đang có thay đổi chưa được áp dụng. Vui lòng bấm 'Áp dụng lọc' trước khi tiếp tục.", "Bộ lọc chưa áp dụng", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_selectedFacility == null || _appliedFilter == null) return;

        // Find the unit type model from loaded unit types list ONLY if matching current context version
        FacilityUnitTypeCatalogModel? unitType = null;
        if (_loadedUnitTypesContextVersion == _catalogContextVersion &&
            gridUnitTypes.ItemsSource is IEnumerable<FacilityUnitTypeCatalogModel> list)
        {
            unitType = list.FirstOrDefault(t => t.UnitTypeId == _selectedMapItem.UnitTypeId);
        }

        if (unitType == null)
        {
            // Snapshot context before await to guard against race conditions or user interactions during wait
            var targetFacilityId = _selectedFacility.Id;
            var targetUnitTypeId = _selectedMapItem.UnitTypeId;
            var targetFilter = _appliedFilter;
            var targetContextVersion = _catalogContextVersion;

            try
            {
                var resp = await ApiClient.Instance.GetFacilityUnitTypesAsync(
                    targetFacilityId,
                    targetFilter.StartDate,
                    targetFilter.EndDate,
                    targetFilter.MaxPrice,
                    targetFilter.ClimateControlled,
                    targetFilter.MinAreaM2,
                    targetFilter.MaxAreaM2);

                // Verify context hasn't changed during async await
                if (_catalogContextVersion != targetContextVersion ||
                    _selectedFacility == null || _selectedFacility.Id != targetFacilityId ||
                    _appliedFilter != targetFilter ||
                    _selectedMapItem == null || _selectedMapItem.UnitTypeId != targetUnitTypeId)
                {
                    // Context changed; discard stale response
                    return;
                }

                if (resp.Success && resp.Data != null)
                {
                    _loadedUnitTypesContextVersion = targetContextVersion;
                    gridUnitTypes.ItemsSource = resp.Data;
                    unitType = resp.Data.FirstOrDefault(t => t.UnitTypeId == targetUnitTypeId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching unit types from map: {ex.Message}");
            }
        }

        if (unitType == null || unitType.MonthlyRate <= 0)
        {
            MessageBox.Show(
                "Không thể xác định biểu phí hợp lệ cho loại kho này tại cơ sở trong khoảng thời gian đã chọn. Vui lòng kiểm tra lại bộ lọc ngày thuê.",
                "Lỗi biểu phí",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await InitiateReservationFlowAsync(unitType);
    }

    private async Task InitiateReservationFlowAsync(FacilityUnitTypeCatalogModel unitType)
    {
        if (_selectedFacility == null || _appliedFilter == null) return;

        // 1. Check authentication
        if (!SessionStore.IsLoggedIn)
        {
            var loginChoice = MessageBox.Show(
                "Bạn cần đăng nhập bằng tài khoản Khách hàng để thực hiện đặt chỗ trực tuyến.\n" +
                "Bạn có muốn mở màn hình Đăng nhập ngay bây giờ không?",
                "Yêu cầu Đăng nhập",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (loginChoice == MessageBoxResult.Yes)
            {
                var loginWin = new LoginWindow();
                loginWin.ShowDialog();
                UpdateUserSessionHeader();
            }

            if (!SessionStore.IsLoggedIn) return;
        }

        // 2. Check Storage Customer role
        if (SessionStore.CurrentUser?.Roles.Contains("storage_customer") != true)
        {
            MessageBox.Show(
                "Chức năng đặt chỗ trực tuyến chỉ áp dụng cho tài khoản Khách hàng (Storage Customer).\n" +
                "Tài khoản hiện tại không có vai trò này để tạo đơn đặt chỗ.",
                "Không có quyền",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // 3. Open confirmation dialog
        var confirmDialog = new ConfirmReservationDialog(
            _selectedFacility,
            unitType,
            _appliedFilter.StartDate,
            _appliedFilter.EndDate)
        {
            Owner = this
        };

        if (confirmDialog.ShowDialog() == true && confirmDialog.CreatedReservation != null)
        {
            // Open reservation detail window with 15-minute countdown
            var detailWindow = new ReservationDetailWindow(confirmDialog.CreatedReservation)
            {
                Owner = this
            };
            detailWindow.ShowDialog();

            // Refresh catalog availability to reflect new hold
            await RefreshSelectedFacilityDataAsync();
        }
    }

    private void BtnMyReservations_Click(object sender, RoutedEventArgs e)
    {
        if (!SessionStore.IsLoggedIn)
        {
            var loginChoice = MessageBox.Show(
                "Bạn cần đăng nhập tài khoản Khách hàng để xem các đơn đặt chỗ của bạn.\n" +
                "Bạn có muốn mở màn hình Đăng nhập ngay bây giờ không?",
                "Yêu cầu Đăng nhập",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (loginChoice == MessageBoxResult.Yes)
            {
                var loginWin = new LoginWindow();
                loginWin.ShowDialog();
                UpdateUserSessionHeader();
            }

            if (!SessionStore.IsLoggedIn) return;
        }

        var myWin = new MyReservationsWindow
        {
            Owner = this
        };
        myWin.ShowDialog();
    }

    private async void BtnUnitPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_unitPage > 1)
        {
            _unitPage--;
            await LoadAvailableUnitsAsync();
        }
    }

    private async void BtnUnitNext_Click(object sender, RoutedEventArgs e)
    {
        _unitPage++;
        await LoadAvailableUnitsAsync();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
