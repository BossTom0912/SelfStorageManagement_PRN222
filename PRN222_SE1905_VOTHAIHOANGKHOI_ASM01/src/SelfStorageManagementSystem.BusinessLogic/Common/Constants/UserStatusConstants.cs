namespace SelfStorageManagementSystem.BusinessLogic.Common.Constants;

public static class UserStatusConstants
{
    public const string Active = "active";
    public const string Locked = "locked";
    public const string Disabled = "disabled";

    public static readonly IReadOnlyList<string> AllStatuses = new[]
    {
        Active,
        Locked,
        Disabled
    };

    public static bool IsValidStatus(string status) =>
        AllStatuses.Contains(status);
}
