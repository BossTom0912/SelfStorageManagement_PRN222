namespace SelfStorageManagementSystem.BusinessLogic.Common.Constants;

public static class RoleConstants
{
    public const string StorageCustomer = "storage_customer";
    public const string FacilityStaff = "facility_staff";
    public const string FacilityManager = "facility_manager";
    public const string BusinessOperationsManager = "business_operations_manager";
    public const string SystemAdministrator = "system_administrator";

    public static readonly IReadOnlyList<string> AllRoles = new[]
    {
        StorageCustomer,
        FacilityStaff,
        FacilityManager,
        BusinessOperationsManager,
        SystemAdministrator
    };

    public static readonly IReadOnlyList<string> EmployeeRoles = new[]
    {
        FacilityStaff,
        FacilityManager,
        BusinessOperationsManager
    };

    public static bool IsValidRole(string roleCode) =>
        AllRoles.Contains(roleCode);

    public static bool IsEmployeeRole(string roleCode) =>
        EmployeeRoles.Contains(roleCode);
}
