namespace SelfStorageManagementSystem.BusinessLogic.Common.Constants;

public static class EmploymentStatusConstants
{
    public const string Active = "active";
    public const string Leave = "leave";
    public const string Terminated = "terminated";

    public static readonly IReadOnlyList<string> AllStatuses = new[]
    {
        Active,
        Leave,
        Terminated
    };

    public static bool IsValidStatus(string status) =>
        AllStatuses.Contains(status);
}
