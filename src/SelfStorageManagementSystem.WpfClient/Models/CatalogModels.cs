using System.Text.Json.Serialization;

namespace SelfStorageManagementSystem.WpfClient.Models;

public class FacilityCatalogModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("addressLine")]
    public string AddressLine { get; set; } = string.Empty;

    [JsonPropertyName("ward")]
    public string? Ward { get; set; }

    [JsonPropertyName("district")]
    public string? District { get; set; }

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public decimal? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public decimal? Longitude { get; set; }

    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";

    [JsonPropertyName("openingTime")]
    public string? OpeningTime { get; set; }

    [JsonPropertyName("closingTime")]
    public string? ClosingTime { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    public string FullAddress => $"{AddressLine}{(string.IsNullOrWhiteSpace(District) ? "" : $", {District}")}, {City}";

    public string OperatingHoursDisplay => (!string.IsNullOrEmpty(OpeningTime) && !string.IsNullOrEmpty(ClosingTime))
        ? $"{OpeningTime} - {ClosingTime}"
        : "24/7";
}

public class FacilityUnitTypeCatalogModel
{
    [JsonPropertyName("unitTypeId")]
    public long UnitTypeId { get; set; }

    [JsonPropertyName("facilityId")]
    public long FacilityId { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("widthM")]
    public decimal WidthM { get; set; }

    [JsonPropertyName("lengthM")]
    public decimal LengthM { get; set; }

    [JsonPropertyName("heightM")]
    public decimal HeightM { get; set; }

    [JsonPropertyName("areaM2")]
    public decimal? AreaM2 { get; set; }

    [JsonPropertyName("volumeM3")]
    public decimal? VolumeM3 { get; set; }

    [JsonPropertyName("climateControlled")]
    public bool ClimateControlled { get; set; }

    [JsonPropertyName("maxWeightKg")]
    public decimal? MaxWeightKg { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("monthlyRate")]
    public decimal MonthlyRate { get; set; }

    [JsonPropertyName("depositAmount")]
    public decimal DepositAmount { get; set; }

    [JsonPropertyName("bookingFee")]
    public decimal BookingFee { get; set; }

    [JsonPropertyName("rateValidFrom")]
    public string? RateValidFrom { get; set; }

    [JsonPropertyName("rateValidTo")]
    public string? RateValidTo { get; set; }

    [JsonPropertyName("totalListedUnits")]
    public int TotalListedUnits { get; set; }

    [JsonPropertyName("estimatedAvailableUnits")]
    public int EstimatedAvailableUnits { get; set; }

    [JsonPropertyName("availabilityNote")]
    public string AvailabilityNote { get; set; } = string.Empty;

    public string DimensionsDisplay => $"{WidthM:0.##}m x {LengthM:0.##}m x {HeightM:0.##}m ({AreaM2:0.##} m²)";
    public string MonthlyRateDisplay => $"{MonthlyRate:N0} đ/tháng";
    public string DepositDisplay => $"{DepositAmount:N0} đ";
    public string ClimateControlDisplay => ClimateControlled ? "Kho mát (Có máy lạnh)" : "Kho tiêu chuẩn";
}

public class AvailableStorageUnitModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("facilityId")]
    public long FacilityId { get; set; }

    [JsonPropertyName("unitTypeId")]
    public long UnitTypeId { get; set; }

    [JsonPropertyName("unitTypeName")]
    public string UnitTypeName { get; set; } = string.Empty;

    [JsonPropertyName("unitCode")]
    public string UnitCode { get; set; } = string.Empty;

    [JsonPropertyName("areaId")]
    public long? AreaId { get; set; }

    [JsonPropertyName("areaName")]
    public string? AreaName { get; set; }

    [JsonPropertyName("floorLabel")]
    public string? FloorLabel { get; set; }

    [JsonPropertyName("zoneLabel")]
    public string? ZoneLabel { get; set; }

    [JsonPropertyName("physicalStatus")]
    public string PhysicalStatus { get; set; } = "available";

    [JsonPropertyName("isListed")]
    public bool IsListed { get; set; }

    [JsonPropertyName("widthM")]
    public decimal WidthM { get; set; }

    [JsonPropertyName("lengthM")]
    public decimal LengthM { get; set; }

    [JsonPropertyName("heightM")]
    public decimal HeightM { get; set; }

    [JsonPropertyName("areaM2")]
    public decimal? AreaM2 { get; set; }

    [JsonPropertyName("volumeM3")]
    public decimal? VolumeM3 { get; set; }

    [JsonPropertyName("climateControlled")]
    public bool ClimateControlled { get; set; }

    [JsonPropertyName("monthlyRate")]
    public decimal? MonthlyRate { get; set; }

    [JsonPropertyName("hasMapPosition")]
    public bool HasMapPosition { get; set; }

    [JsonPropertyName("mapX")]
    public decimal? MapX { get; set; }

    [JsonPropertyName("mapY")]
    public decimal? MapY { get; set; }

    [JsonPropertyName("mapWidth")]
    public decimal? MapWidth { get; set; }

    [JsonPropertyName("mapHeight")]
    public decimal? MapHeight { get; set; }

    public string DimensionsDisplay => $"{WidthM:0.##}m x {LengthM:0.##}m ({AreaM2:0.##} m²)";
    public string PriceDisplay => MonthlyRate.HasValue ? $"{MonthlyRate.Value:N0} đ/tháng" : "Chưa có biểu phí";
    public string LocationDisplay => $"{FloorLabel ?? "Tầng trệt"}{(string.IsNullOrWhiteSpace(ZoneLabel) ? "" : $" - Khu {ZoneLabel}")}";
    public string MapStatusDisplay => HasMapPosition ? "Đã có vị trí trên sơ đồ" : "Chưa định vị sơ đồ";
}

public class FacilityAreaModel
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("areaType")]
    public string AreaType { get; set; } = string.Empty;

    [JsonPropertyName("displayOrder")]
    public int DisplayOrder { get; set; }

    [JsonPropertyName("parentAreaId")]
    public long? ParentAreaId { get; set; }
}

public class UnitMapItemModel
{
    [JsonPropertyName("unitId")]
    public long UnitId { get; set; }

    [JsonPropertyName("areaId")]
    public long AreaId { get; set; }

    [JsonPropertyName("areaName")]
    public string AreaName { get; set; } = string.Empty;

    [JsonPropertyName("unitCode")]
    public string UnitCode { get; set; } = string.Empty;

    [JsonPropertyName("unitTypeId")]
    public long UnitTypeId { get; set; }

    [JsonPropertyName("unitTypeName")]
    public string UnitTypeName { get; set; } = string.Empty;

    [JsonPropertyName("areaM2")]
    public decimal? AreaM2 { get; set; }

    [JsonPropertyName("volumeM3")]
    public decimal? VolumeM3 { get; set; }

    [JsonPropertyName("climateControlled")]
    public bool ClimateControlled { get; set; }

    [JsonPropertyName("x")]
    public decimal X { get; set; }

    [JsonPropertyName("y")]
    public decimal Y { get; set; }

    [JsonPropertyName("width")]
    public decimal Width { get; set; }

    [JsonPropertyName("height")]
    public decimal Height { get; set; }

    [JsonPropertyName("rotationDegrees")]
    public decimal RotationDegrees { get; set; }

    [JsonPropertyName("displayStatus")]
    public string DisplayStatus { get; set; } = "available";

    [JsonPropertyName("canSelectToProceed")]
    public bool CanSelectToProceed { get; set; }

    [JsonPropertyName("monthlyRate")]
    public decimal? MonthlyRate { get; set; }

    public string StatusDisplayName => DisplayStatus switch
    {
        "available" => "Trống (Khả dụng)",
        "occupied" => "Đang thuê",
        "reserved" => "Đang giữ chỗ",
        "maintenance" => "Bảo trì",
        _ => "Không khả dụng"
    };

    public string StatusColorHex => DisplayStatus switch
    {
        "available" => "#10B981",    // Green
        "occupied" => "#EF4444",     // Red
        "reserved" => "#F59E0B",     // Amber / Yellow
        "maintenance" => "#64748B",  // Slate / Gray
        _ => "#94A3B8"               // Muted gray
    };
}

public class FacilityFloorMapModel
{
    [JsonPropertyName("facilityId")]
    public long FacilityId { get; set; }

    [JsonPropertyName("facilityName")]
    public string FacilityName { get; set; } = string.Empty;

    [JsonPropertyName("areas")]
    public List<FacilityAreaModel> Areas { get; set; } = new();

    [JsonPropertyName("units")]
    public List<UnitMapItemModel> Units { get; set; } = new();

    [JsonPropertyName("totalUnitsWithMap")]
    public int TotalUnitsWithMap { get; set; }

    [JsonPropertyName("totalUnitsWithoutMap")]
    public int TotalUnitsWithoutMap { get; set; }
}
