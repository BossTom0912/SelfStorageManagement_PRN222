using System.Globalization;
using System.Security.Claims;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;

namespace SelfStorageManagementSystem.Web.Helpers;

/// <summary>
/// Chuẩn hóa định dạng hiển thị cho giao diện Web:
/// Tiền tệ Việt Nam (VND), Ngày thuê, Thời gian cơ sở (múi giờ Asia/Ho_Chi_Minh),
/// Tên hiển thị và Phù hiệu Role theo thứ tự ưu tiên đa vai trò.
/// </summary>
public static class AppFormatters
{
    private static readonly CultureInfo VietnameseCulture = new("vi-VN");

    /// <summary>
    /// Múi giờ mặc định cho toàn hệ thống cơ sở kho: Asia/Ho_Chi_Minh (UTC+7).
    /// Khởi tạo tương thích đa nền tảng (Windows / Linux / macOS / Docker).
    /// </summary>
    public static readonly TimeZoneInfo DefaultFacilityTimeZone = ResolveFacilityTimeZone("Asia/Ho_Chi_Minh");

    /// <summary>
    /// Thứ tự ưu tiên hiển thị vai trò người dùng (khi một tài khoản có nhiều quyền đồng thời):
    /// system_administrator -> business_operations_manager -> facility_manager -> facility_staff -> storage_customer.
    /// </summary>
    public static readonly IReadOnlyList<string> RoleDisplayPriority = new[]
    {
        RoleConstants.SystemAdministrator,
        RoleConstants.BusinessOperationsManager,
        RoleConstants.FacilityManager,
        RoleConstants.FacilityStaff,
        RoleConstants.StorageCustomer
    };

    /// <summary>
    /// Tìm kiếm và giải quyết TimeZoneInfo tương thích đa nền tảng.
    /// Tự động chuyển đổi giữa IANA ID (Linux/macOS) và Windows TimeZone ID.
    /// </summary>
    public static TimeZoneInfo ResolveFacilityTimeZone(string? timeZoneId = null)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZoneId = "Asia/Ho_Chi_Minh";
        }

        // 1. Thử tìm kiếm trực tiếp bằng ID được cung cấp
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception) { }

        // 2. Nếu là IANA ID, thử chuyển sang Windows ID tương ứng
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }
            catch (Exception) { }
        }

        // 3. Nếu là Windows ID, thử chuyển sang IANA ID tương ứng
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            }
            catch (Exception) { }
        }

        // 4. Các ứng viên múi giờ Việt Nam / Đông Nam Á phổ biến
        string[] fallbacks = { "SE Asia Standard Time", "Asia/Ho_Chi_Minh", "Asia/Saigon", "Asia/Bangkok" };
        foreach (var candidate in fallbacks)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (Exception) { }
        }

        // 5. Giải pháp dự phòng tuyệt đối: Múi giờ cố định UTC+7 không phụ thuộc OS
        return TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Ho_Chi_Minh",
            TimeSpan.FromHours(7),
            "(UTC+07:00) Giờ Chuẩn Việt Nam",
            "Giờ Chuẩn Việt Nam");
    }

    /// <summary>
    /// Chuẩn hóa hiển thị tiền tệ Việt Nam Đồng (VND).
    /// Ví dụ: 1500000 -> "1.500.000 ₫"
    /// </summary>
    public static string FormatVnd(decimal? amount)
    {
        if (!amount.HasValue)
        {
            return "0 ₫";
        }

        return string.Format(VietnameseCulture, "{0:#,##0} ₫", amount.Value);
    }

    /// <summary>
    /// Bí danh cho FormatVnd tương thích với các view gọi FormatCurrency.
    /// </summary>
    public static string FormatCurrency(decimal? amount) => FormatVnd(amount);

    /// <summary>
    /// Chuẩn hóa ngày thuê / ngày dương lịch: dd/MM/yyyy (tự động quy đổi về múi giờ cơ sở Việt Nam).
    /// Ví dụ: "15/10/2026"
    /// </summary>
    public static string FormatDate(DateTimeOffset? date, TimeZoneInfo? timeZone = null)
    {
        if (!date.HasValue)
        {
            return "—";
        }

        var tz = timeZone ?? DefaultFacilityTimeZone;
        var converted = TimeZoneInfo.ConvertTime(date.Value, tz);
        return converted.ToString("dd/MM/yyyy", VietnameseCulture);
    }

    public static string FormatDate(DateTimeOffset? date, string? timeZoneId)
    {
        return FormatDate(date, ResolveFacilityTimeZone(timeZoneId));
    }

    public static string FormatDate(DateTime? date, TimeZoneInfo? timeZone = null)
    {
        if (!date.HasValue)
        {
            return "—";
        }

        var tz = timeZone ?? DefaultFacilityTimeZone;
        DateTime converted;
        if (date.Value.Kind == DateTimeKind.Utc)
        {
            converted = TimeZoneInfo.ConvertTimeFromUtc(date.Value, tz);
        }
        else
        {
            converted = date.Value;
        }

        return converted.ToString("dd/MM/yyyy", VietnameseCulture);
    }

    public static string FormatDate(DateOnly? date)
    {
        if (!date.HasValue)
        {
            return "—";
        }

        return date.Value.ToString("dd/MM/yyyy", VietnameseCulture);
    }

    /// <summary>
    /// Chuẩn hóa thời gian cơ sở / thời điểm giao dịch: dd/MM/yyyy HH:mm.
    /// Tự động quy đổi mọi mốc thời gian UTC (hoặc mốc có offset khác) về đúng múi giờ mục tiêu (mặc định Asia/Ho_Chi_Minh).
    /// Ví dụ: 2026-10-15T07:30:00Z -> "15/10/2026 14:30"
    /// </summary>
    public static string FormatDateTime(DateTimeOffset? dateTime, TimeZoneInfo? timeZone = null)
    {
        if (!dateTime.HasValue)
        {
            return "—";
        }

        var tz = timeZone ?? DefaultFacilityTimeZone;
        var converted = TimeZoneInfo.ConvertTime(dateTime.Value, tz);
        return converted.ToString("dd/MM/yyyy HH:mm", VietnameseCulture);
    }

    public static string FormatDateTime(DateTimeOffset? dateTime, string? timeZoneId)
    {
        return FormatDateTime(dateTime, ResolveFacilityTimeZone(timeZoneId));
    }

    /// <summary>
    /// Chuẩn hóa thời gian cơ sở đầy đủ kèm giây: dd/MM/yyyy HH:mm:ss.
    /// Tự động quy đổi mọi mốc thời gian UTC về đúng múi giờ mục tiêu (mặc định Asia/Ho_Chi_Minh).
    /// </summary>
    public static string FormatDateTimeSeconds(DateTimeOffset? dateTime, TimeZoneInfo? timeZone = null)
    {
        if (!dateTime.HasValue)
        {
            return "—";
        }

        var tz = timeZone ?? DefaultFacilityTimeZone;
        var converted = TimeZoneInfo.ConvertTime(dateTime.Value, tz);
        return converted.ToString("dd/MM/yyyy HH:mm:ss", VietnameseCulture);
    }

    public static string FormatDateTimeSeconds(DateTimeOffset? dateTime, string? timeZoneId)
    {
        return FormatDateTimeSeconds(dateTime, ResolveFacilityTimeZone(timeZoneId));
    }

    public static string FormatDateTime(DateTime? dateTime, TimeZoneInfo? timeZone = null)
    {
        if (!dateTime.HasValue)
        {
            return "—";
        }

        var tz = timeZone ?? DefaultFacilityTimeZone;
        DateTime converted;
        if (dateTime.Value.Kind == DateTimeKind.Utc)
        {
            converted = TimeZoneInfo.ConvertTimeFromUtc(dateTime.Value, tz);
        }
        else
        {
            converted = dateTime.Value;
        }

        return converted.ToString("dd/MM/yyyy HH:mm", VietnameseCulture);
    }

    /// <summary>
    /// Chuẩn hóa giờ hoạt động cơ sở: HH:mm.
    /// Ví dụ: 08:00
    /// </summary>
    public static string FormatTime(TimeSpan? time)
    {
        if (!time.HasValue)
        {
            return "—";
        }

        return time.Value.ToString(@"hh\:mm");
    }

    /// <summary>
    /// Chuẩn hóa giờ hoạt động cơ sở cho TimeOnly?: HH:mm.
    /// Ví dụ: 08:00
    /// </summary>
    public static string FormatTime(TimeOnly? time)
    {
        if (!time.HasValue)
        {
            return "—";
        }

        return time.Value.ToString("HH:mm");
    }

    /// <summary>
    /// Chọn vai trò hiển thị ưu tiên cao nhất từ ClaimsPrincipal của người dùng đang đăng nhập.
    /// Áp dụng thứ tự ưu tiên: system_administrator -> business_operations_manager -> facility_manager -> facility_staff -> storage_customer.
    /// Thứ tự claim đầu vào tuyệt đối không ảnh hưởng đến kết quả vai trò hiển thị.
    /// </summary>
    public static string? GetPrimaryRole(ClaimsPrincipal? user)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // Đọc toàn bộ các role claim từ danh tính người dùng
        var userRoles = user.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Duyệt theo thứ tự ưu tiên nghiêm ngặt
        foreach (var role in RoleDisplayPriority)
        {
            if (userRoles.Contains(role) || user.IsInRole(role))
            {
                return role;
            }
        }

        // Fallback: Nếu là vai trò tùy biến không thuộc danh mục chuẩn
        return userRoles.FirstOrDefault();
    }

    /// <summary>
    /// Chọn vai trò hiển thị ưu tiên cao nhất từ danh sách chuỗi mã vai trò.
    /// </summary>
    public static string? GetPrimaryRole(IEnumerable<string>? roles)
    {
        if (roles == null)
        {
            return null;
        }

        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var role in RoleDisplayPriority)
        {
            if (roleSet.Contains(role))
            {
                return role;
            }
        }

        return roles.FirstOrDefault();
    }

    /// <summary>
    /// Trả về tên hiển thị tiếng Việt chuẩn hóa cho các mã vai trò (role codes).
    /// </summary>
    public static string GetRoleDisplayName(string? roleCode)
    {
        return roleCode switch
        {
            RoleConstants.StorageCustomer => "Khách hàng",
            RoleConstants.FacilityStaff => "Nhân viên cơ sở",
            RoleConstants.FacilityManager => "Quản lý cơ sở",
            RoleConstants.BusinessOperationsManager => "Điều hành kinh doanh",
            RoleConstants.SystemAdministrator => "Quản trị hệ thống",
            _ => string.IsNullOrWhiteSpace(roleCode) ? "Khách vãng lai" : roleCode
        };
    }

    /// <summary>
    /// Trả về class Bootstrap badge màu sắc tương ứng với từng vai trò.
    /// </summary>
    public static string GetRoleBadgeClass(string? roleCode)
    {
        return roleCode switch
        {
            RoleConstants.StorageCustomer => "badge bg-info text-dark",
            RoleConstants.FacilityStaff => "badge bg-primary",
            RoleConstants.FacilityManager => "badge bg-warning text-dark",
            RoleConstants.BusinessOperationsManager => "badge bg-success",
            RoleConstants.SystemAdministrator => "badge bg-danger",
            _ => "badge bg-secondary"
        };
    }
}
