namespace SelfStorageManagementSystem.BusinessLogic.Common;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyLengthBytes = 32;

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SelfStoragePRN222";
    public string Audience { get; set; } = "SelfStoragePRN222Clients";
    public int ExpiryMinutes { get; set; } = 60;
}
