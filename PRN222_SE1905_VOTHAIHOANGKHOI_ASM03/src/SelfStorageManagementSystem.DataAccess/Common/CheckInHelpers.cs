using System.Security.Cryptography;
using System.Text;

namespace SelfStorageManagementSystem.DataAccess.Common;

public static class PinCryptoHelper
{
    public const int MinimumKeyLengthBytes = 16;

    public static string GeneratePin()
    {
        var number = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return number.ToString("D6");
    }

    public static string ComputeDigest(string pin, string? secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < MinimumKeyLengthBytes)
        {
            throw new InvalidOperationException(
                $"Khóa bí mật HMAC cho PIN chưa được cấu hình hoặc ngắn hơn {MinimumKeyLengthBytes} bytes. " +
                "Vui lòng thiết lập 'CheckIn:PinHmacSecret' hoặc biến môi trường 'CHECKIN_PIN_HMAC_SECRET'.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var pinBytes = Encoding.UTF8.GetBytes(pin);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(pinBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

public static class FacilityTimeHelper
{
    public static TimeZoneInfo GetFacilityTimeZone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TryGetVietnamTimeZone();
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch
        {
            if (timezone.Equals("Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase) ||
                timezone.Equals("Asia/Bangkok", StringComparison.OrdinalIgnoreCase) ||
                timezone.Equals("SE Asia Standard Time", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetVietnamTimeZone();
            }

            return TimeZoneInfo.Utc;
        }
    }

    private static TimeZoneInfo TryGetVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("SE Asia Standard Time", TimeSpan.FromHours(7), "SE Asia Standard Time", "SE Asia Standard Time");
            }
        }
    }

    public static DateOnly GetFacilityToday(string? timezone, DateTimeOffset nowUtc)
    {
        var tz = GetFacilityTimeZone(timezone);
        var localTime = TimeZoneInfo.ConvertTime(nowUtc, tz);
        return DateOnly.FromDateTime(localTime.DateTime);
    }

    public static DateTimeOffset GetFacilityStartOfDay(DateOnly date, string? timezone)
    {
        var tz = GetFacilityTimeZone(timezone);
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var offset = tz.GetUtcOffset(dt);
        return new DateTimeOffset(dt, offset);
    }
}

public static class IdentityNumberMasker
{
    public static string? Mask(string? identityNumber)
    {
        if (string.IsNullOrWhiteSpace(identityNumber))
        {
            return null;
        }

        var trimmed = identityNumber.Trim();
        if (trimmed.Length <= 4)
        {
            return new string('*', trimmed.Length);
        }

        return new string('*', trimmed.Length - 4) + trimmed[^4..];
    }
}

public class StaffCounterPermissionSnapshot
{
    public long UserId { get; set; }
    public bool IsUserActive { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool IsEmployeeActive { get; set; }
    public List<(long FacilityId, string AssignmentRole, DateTimeOffset StartsAt, DateTimeOffset? EndsAt)> Assignments { get; set; } = new();
}

public class VerifyIdentityOutcome
{
    public Entities.identity_verification Verification { get; set; } = null!;
    public bool IsMismatch { get; set; }
}
