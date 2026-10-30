using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Unit tests for StorageUnitAdminServiceKhoiVTH enforcing:
/// 1. Actor authentication & server-side role/scope verification (Admin, Ops, Manager, Staff, Customer).
/// 2. Role-based read/write access (Staff read-only, Manager write restricted to assigned facility).
/// 3. Creation rules (Server enforces available and is_listed=true, unique code, area in facility).
/// 4. Update rules (Optimistic concurrency with ExpectedUpdatedAt, edit only allowed fields, status cannot be manipulated).
/// 5. Delete protection (Status available only, no historical FK references, no capacity breach/unassigned holds/confirmed).
/// </summary>
public class StorageUnitAdminServiceTests
{
    private static SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    private static async Task SeedDatabaseAsync(SelfStorageDbContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // 1. Facilities
        var fac1 = new facility
        {
            id = 10,
            code = "FAC-HN-01",
            name = "Cơ sở Cầu Giấy",
            address_line = "123 Cầu Giấy",
            district = "Cầu Giấy",
            city = "Hà Nội",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };
        var fac2 = new facility
        {
            id = 20,
            code = "FAC-HCM-01",
            name = "Cơ sở Quận 1",
            address_line = "456 Lê Lợi",
            district = "Quận 1",
            city = "Hồ Chí Minh",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };
        var fac3 = new facility
        {
            id = 30,
            code = "FAC-DN-01",
            name = "Cơ sở Đà Nẵng",
            address_line = "789 Nguyễn Văn Linh",
            district = "Hải Châu",
            city = "Đà Nẵng",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };
        context.facilities.AddRange(fac1, fac2, fac3);

        // 2. Unit Types
        var type1 = new unit_type
        {
            id = 100,
            code = "TYPE-S",
            name = "Kho Mini S",
            width_m = 1.5m,
            length_m = 2.0m,
            height_m = 2.5m,
            area_m2 = 3.0m,
            volume_m3 = 7.5m,
            is_active = true
        };
        var type2 = new unit_type
        {
            id = 200,
            code = "TYPE-M",
            name = "Kho Vừa M",
            width_m = 2.0m,
            length_m = 3.0m,
            height_m = 2.5m,
            area_m2 = 6.0m,
            volume_m3 = 15.0m,
            is_active = true
        };
        context.unit_types.AddRange(type1, type2);

        // 3. Facility Areas
        var area1 = new facility_area
        {
            id = 1000,
            facility_id = 10,
            code = "AREA-HN-1",
            name = "Tầng 1 Khu A",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        var area2 = new facility_area
        {
            id = 2000,
            facility_id = 20,
            code = "AREA-HCM-1",
            name = "Tầng 1 HCM",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        context.facility_areas.AddRange(area1, area2);

        // 4. Roles
        var rAdmin = new role { id = 1, code = RoleConstants.SystemAdministrator, display_name = "Admin" };
        var rOps = new role { id = 2, code = RoleConstants.BusinessOperationsManager, display_name = "Ops Manager" };
        var rMgr = new role { id = 3, code = RoleConstants.FacilityManager, display_name = "Manager" };
        var rStaff = new role { id = 4, code = RoleConstants.FacilityStaff, display_name = "Staff" };
        var rCust = new role { id = 5, code = RoleConstants.StorageCustomer, display_name = "Customer" };
        context.roles.AddRange(rAdmin, rOps, rMgr, rStaff, rCust);

        // 5. Users & Profiles
        // User 1: System Admin
        var uAdmin = new user { id = 1, email = "admin@selfstorage.com", status = UserStatusConstants.Active, password_hash = "hash", created_at = now, updated_at = now };
        var urAdmin = new user_role { user_id = 1, role_id = 1, granted_at = now };
        var epAdmin = new employee_profile { user_id = 1, employee_code = "EMP-ADM-01", full_name = "System Administrator", hire_date = today, employment_status = EmploymentStatusConstants.Active };
        context.users.Add(uAdmin);
        context.user_roles.Add(urAdmin);
        context.employee_profiles.Add(epAdmin);

        // User 2: Business Operations Manager
        var uOps = new user { id = 2, email = "ops@selfstorage.com", status = UserStatusConstants.Active, password_hash = "hash", created_at = now, updated_at = now };
        var urOps = new user_role { user_id = 2, role_id = 2, granted_at = now };
        var epOps = new employee_profile { user_id = 2, employee_code = "EMP-OPS-01", full_name = "Operations Manager", hire_date = today, employment_status = EmploymentStatusConstants.Active };
        context.users.Add(uOps);
        context.user_roles.Add(urOps);
        context.employee_profiles.Add(epOps);

        // User 10: Facility Manager (Manager at Fac 10, Staff at Fac 20)
        var uMgr = new user { id = 10, email = "manager@selfstorage.com", status = UserStatusConstants.Active, password_hash = "hash", created_at = now, updated_at = now };
        var urMgr = new user_role { user_id = 10, role_id = 3, granted_at = now };
        var epMgr = new employee_profile { user_id = 10, employee_code = "EMP-MGR-01", full_name = "Nguyen Van Manager", hire_date = today, employment_status = EmploymentStatusConstants.Active };
        var sfaMgr10 = new staff_facility_assignment { id = 1001, employee_id = 10, facility_id = 10, assignment_role = RoleConstants.FacilityManager, starts_at = now.AddDays(-10), ends_at = null, created_at = now };
        var sfaMgr20 = new staff_facility_assignment { id = 1002, employee_id = 10, facility_id = 20, assignment_role = RoleConstants.FacilityStaff, starts_at = now.AddDays(-10), ends_at = null, created_at = now };
        context.users.Add(uMgr);
        context.user_roles.Add(urMgr);
        context.employee_profiles.Add(epMgr);
        context.staff_facility_assignments.AddRange(sfaMgr10, sfaMgr20);

        // User 20: Facility Staff (Staff at Fac 10 only)
        var uStaff = new user { id = 20, email = "staff@selfstorage.com", status = UserStatusConstants.Active, password_hash = "hash", created_at = now, updated_at = now };
        var urStaff = new user_role { user_id = 20, role_id = 4, granted_at = now };
        var epStaff = new employee_profile { user_id = 20, employee_code = "EMP-STF-01", full_name = "Tran Van Staff", hire_date = today, employment_status = EmploymentStatusConstants.Active };
        var sfaStaff10 = new staff_facility_assignment { id = 2001, employee_id = 20, facility_id = 10, assignment_role = RoleConstants.FacilityStaff, starts_at = now.AddDays(-10), ends_at = null, created_at = now };
        context.users.Add(uStaff);
        context.user_roles.Add(urStaff);
        context.employee_profiles.Add(epStaff);
        context.staff_facility_assignments.Add(sfaStaff10);

        // User 30: Staff with NO assignments
        var uNoAssign = new user { id = 30, email = "unassigned@selfstorage.com", status = UserStatusConstants.Active, password_hash = "hash", created_at = now, updated_at = now };
        var urNoAssign = new user_role { user_id = 30, role_id = 4, granted_at = now };
        var epNoAssign = new employee_profile { user_id = 30, employee_code = "EMP-UNA-01", full_name = "Staff Unassigned", hire_date = today, employment_status = EmploymentStatusConstants.Active };
        context.users.Add(uNoAssign);
        context.user_roles.Add(urNoAssign);
        context.employee_profiles.Add(epNoAssign);

        // User 40: Customer Only
        var uCust = new user { id = 40, email = "customer@gmail.com", status = UserStatusConstants.Active, password_hash = "hash", created_at = now, updated_at = now };
        var urCust = new user_role { user_id = 40, role_id = 5, granted_at = now };
        var cpCust = new customer_profile { user_id = 40, full_name = "Le Van Customer" };
        context.users.Add(uCust);
        context.user_roles.Add(urCust);
        context.customer_profiles.Add(cpCust);

        // User 50: Inactive Manager
        var uInactive = new user { id = 50, email = "disabled@selfstorage.com", status = "disabled", password_hash = "hash", created_at = now, updated_at = now };
        var urInactive = new user_role { user_id = 50, role_id = 3, granted_at = now };
        var epInactive = new employee_profile { user_id = 50, employee_code = "EMP-DIS-01", full_name = "Disabled User", hire_date = today, employment_status = EmploymentStatusConstants.Terminated };
        context.users.Add(uInactive);
        context.user_roles.Add(urInactive);
        context.employee_profiles.Add(epInactive);

        // 6. Initial Storage Units
        // Unit 1: Fac 10, Type 100, Area 1000, Available
        var unit1 = new storage_unit
        {
            id = 1,
            facility_id = 10,
            unit_type_id = 100,
            area_id = 1000,
            unit_code = "SU-10-01",
            floor_label = "Tầng 1",
            zone_label = "Khu A",
            physical_status = "available",
            is_listed = true,
            notes = "Clean unit",
            created_at = now,
            updated_at = now
        };

        // Unit 2: Fac 10, Type 100, Area 1000, Occupied
        var unit2 = new storage_unit
        {
            id = 2,
            facility_id = 10,
            unit_type_id = 100,
            area_id = 1000,
            unit_code = "SU-10-02",
            floor_label = "Tầng 1",
            zone_label = "Khu A",
            physical_status = "occupied",
            is_listed = true,
            notes = "Occupied unit",
            created_at = now,
            updated_at = now
        };

        // Unit 3: Fac 10, Type 200, Area 1000, Available (for clean delete test)
        var unit3 = new storage_unit
        {
            id = 3,
            facility_id = 10,
            unit_type_id = 200,
            area_id = 1000,
            unit_code = "SU-10-03",
            floor_label = "Tầng 1",
            zone_label = "Khu B",
            physical_status = "available",
            is_listed = true,
            notes = "Type 200 unit",
            created_at = now,
            updated_at = now
        };

        // Unit 4: Fac 20, Type 100, Area 2000, Available
        var unit4 = new storage_unit
        {
            id = 4,
            facility_id = 20,
            unit_type_id = 100,
            area_id = 2000,
            unit_code = "SU-20-01",
            floor_label = "Tầng 1",
            zone_label = "Khu HCM",
            physical_status = "available",
            is_listed = true,
            notes = "HCM unit",
            created_at = now,
            updated_at = now
        };

        // Unit 5: Fac 30, Type 100, null Area, Available
        var unit5 = new storage_unit
        {
            id = 5,
            facility_id = 30,
            unit_type_id = 100,
            area_id = null,
            unit_code = "SU-30-01",
            floor_label = null,
            zone_label = null,
            physical_status = "available",
            is_listed = true,
            notes = "DN unit",
            created_at = now,
            updated_at = now
        };

        context.storage_units.AddRange(unit1, unit2, unit3, unit4, unit5);
        await context.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private static (StorageUnitAdminServiceKhoiVTH Service, SelfStorageDbContext Context) CreateTestService(
        string dbName,
        TimeProvider? timeProvider = null)
    {
        var context = CreateInMemoryContext(dbName);
        var unitRepo = new StorageUnitAdminRepositoryKhoiVTH(context);
        var userRepo = new UserRepositoryKhoiVTH(context);
        var scopeService = new FacilityScopeServiceKhoiVTH(context, userRepo);
        var service = new StorageUnitAdminServiceKhoiVTH(unitRepo, userRepo, scopeService, timeProvider);
        return (service, context);
    }

    // =========================================================================
    // 1. Actor Authentication & Roles ("Test giả facility/role")
    // =========================================================================

    [Fact]
    public async Task ActorNotFound_ThrowsUnauthorizedException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.ListAsync(new StorageUnitFilterRequest(), actorUserId: 99999));
    }

    [Fact]
    public async Task ActorInactive_ThrowsUnauthorizedException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // User 50 is disabled
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.ListAsync(new StorageUnitFilterRequest(), actorUserId: 50));
    }

    [Fact]
    public async Task ActorCustomerRole_ThrowsForbiddenException_OnAllOperations()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // User 40 is a customer
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ListAsync(new StorageUnitFilterRequest(), actorUserId: 40));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetByIdAsync(1, actorUserId: 40));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetLookupsAsync(actorUserId: 40));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateAsync(new CreateStorageUnitRequest { FacilityId = 10, UnitTypeId = 100, UnitCode = "NEW-CUST" }, actorUserId: 40));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.UpdateAsync(1, new UpdateStorageUnitRequest { ExpectedUpdatedAt = DateTimeOffset.UtcNow, UnitCode = "UPD" }, actorUserId: 40));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.DeleteAsync(1, DateTimeOffset.UtcNow, actorUserId: 40));
    }

    // =========================================================================
    // 2. Scope & Permissions for Read Operations (List, GetById, Lookups)
    // =========================================================================

    [Fact]
    public async Task ListAsync_Admin_CanViewAllUnitsAcrossAllFacilities()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var result = await service.ListAsync(new StorageUnitFilterRequest(), actorUserId: 1);

        Assert.Equal(5, result.TotalCount);
        Assert.Contains(result.Items, u => u.FacilityId == 10);
        Assert.Contains(result.Items, u => u.FacilityId == 20);
        Assert.Contains(result.Items, u => u.FacilityId == 30);
    }

    [Fact]
    public async Task ListAsync_OperationsManager_CanViewAllUnitsAcrossAllFacilities()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var result = await service.ListAsync(new StorageUnitFilterRequest(), actorUserId: 2);

        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task ListAsync_Staff_WithoutFacilityFilter_OnlyReturnsUnitsInAssignedFacilities()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // Staff (User 20) is assigned ONLY to Facility 10
        var result = await service.ListAsync(new StorageUnitFilterRequest(), actorUserId: 20);

        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, u => Assert.Equal(10, u.FacilityId));
    }

    [Fact]
    public async Task ListAsync_Staff_WithAssignedFacility_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var result = await service.ListAsync(new StorageUnitFilterRequest { FacilityId = 10 }, actorUserId: 20);

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task ListAsync_Staff_WithUnassignedFacility_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // Staff (User 20) is not assigned to Facility 20
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ListAsync(new StorageUnitFilterRequest { FacilityId = 20 }, actorUserId: 20));
    }

    [Fact]
    public async Task ListAsync_StaffWithNoAssignments_ReturnsEmptyPagedResult()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // User 30 has staff role but 0 assignments
        var result = await service.ListAsync(new StorageUnitFilterRequest(), actorUserId: 30);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetByIdAsync_Admin_CanViewAnyUnit()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit = await service.GetByIdAsync(4, actorUserId: 1); // Facility 20 unit

        Assert.NotNull(unit);
        Assert.Equal(4, unit.Id);
        Assert.Equal("SU-20-01", unit.UnitCode);
    }

    [Fact]
    public async Task GetByIdAsync_Staff_InAssignedFacility_ReturnsDetailDto()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit = await service.GetByIdAsync(1, actorUserId: 20); // Facility 10

        Assert.NotNull(unit);
        Assert.Equal("SU-10-01", unit.UnitCode);
        Assert.Equal(10, unit.FacilityId);
        Assert.Equal("Kho Mini S", unit.UnitTypeName);
    }

    [Fact]
    public async Task GetByIdAsync_Staff_InUnassignedFacility_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // Unit 4 belongs to Facility 20; User 20 is only assigned to Facility 10
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetByIdAsync(4, actorUserId: 20));
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentUnit_ReturnsNull()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit = await service.GetByIdAsync(99999, actorUserId: 1);

        Assert.Null(unit);
    }

    [Fact]
    public async Task GetLookupsAsync_Admin_ReturnsAllActiveFacilities()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var lookups = await service.GetLookupsAsync(actorUserId: 1);

        Assert.Equal(3, lookups.Facilities.Count);
        Assert.Contains(lookups.Facilities, f => f.Id == 10);
        Assert.Contains(lookups.Facilities, f => f.Id == 20);
        Assert.Contains(lookups.Facilities, f => f.Id == 30);
    }

    [Fact]
    public async Task GetLookupsAsync_Staff_ReturnsOnlyAssignedFacilities()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var lookups = await service.GetLookupsAsync(actorUserId: 20);

        Assert.Single(lookups.Facilities);
        Assert.Equal(10, lookups.Facilities[0].Id);
    }

    [Fact]
    public async Task GetLookupsAsync_Staff_UnassignedFacilitySpecified_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetLookupsAsync(actorUserId: 20, facilityId: 20));
    }

    // =========================================================================
    // 3. Write Permissions & Scope ("staff ghi", "manager sai assignment")
    // =========================================================================

    [Fact]
    public async Task CreateAsync_StaffRole_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var req = new CreateStorageUnitRequest
        {
            FacilityId = 10,
            UnitTypeId = 100,
            UnitCode = "SU-STAFF-NEW"
        };

        // User 20 is Staff: Staff is strictly read-only
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateAsync(req, actorUserId: 20));

        Assert.Contains("staff", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_Manager_UnassignedFacility_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // User 10 is Manager at Fac 10, but has NO assignment at Fac 30
        var req = new CreateStorageUnitRequest
        {
            FacilityId = 30,
            UnitTypeId = 100,
            UnitCode = "SU-MGR-FAC30"
        };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateAsync(req, actorUserId: 10));

        Assert.Contains("not assigned as manager", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_Manager_AssignedAsStaffOnlyAtTargetFacility_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // User 10 is assigned as 'facility_staff' at Facility 20 (not 'facility_manager')
        var req = new CreateStorageUnitRequest
        {
            FacilityId = 20,
            UnitTypeId = 100,
            UnitCode = "SU-MGR-AS-STAFF"
        };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateAsync(req, actorUserId: 10));

        Assert.Contains("not assigned as manager", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_StaffRole_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit1 = await context.storage_units.FindAsync(1L);
        var req = new UpdateStorageUnitRequest
        {
            ExpectedUpdatedAt = unit1!.updated_at,
            UnitCode = "SU-10-01-EDIT"
        };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.UpdateAsync(1, req, actorUserId: 20));

        Assert.Contains("staff", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_Manager_UnassignedFacility_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // Unit 5 belongs to Facility 30 (Manager 10 has no manager assignment there)
        var unit5 = await context.storage_units.FindAsync(5L);
        var req = new UpdateStorageUnitRequest
        {
            ExpectedUpdatedAt = unit5!.updated_at,
            UnitCode = "SU-30-01-EDIT"
        };

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.UpdateAsync(5, req, actorUserId: 10));

        Assert.Contains("not assigned as manager", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_StaffRole_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit1 = await context.storage_units.FindAsync(1L);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.DeleteAsync(1, unit1!.updated_at, actorUserId: 20));

        Assert.Contains("staff", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_Manager_UnassignedFacility_ThrowsForbiddenException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // Unit 5 is at Facility 30
        var unit5 = await context.storage_units.FindAsync(5L);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.DeleteAsync(5, unit5!.updated_at, actorUserId: 10));

        Assert.Contains("not assigned as manager", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 4. Create Policy & Scope Enforcements ("Create gán available và is_listed=true", "duplicate code", "area khác cơ sở")
    // =========================================================================

    [Fact]
    public async Task CreateAsync_ManagerAssignedAsManager_SucceedsAndEnforcesAvailableAndListed()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var req = new CreateStorageUnitRequest
        {
            FacilityId = 10,
            UnitTypeId = 100,
            AreaId = 1000,
            UnitCode = "SU-MGR-NEW",
            FloorLabel = "Tầng 1",
            ZoneLabel = "Khu A",
            Notes = "Manager created this unit"
        };

        var result = await service.CreateAsync(req, actorUserId: 10);

        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal("SU-MGR-NEW", result.UnitCode);
        Assert.Equal(10, result.FacilityId);
        Assert.Equal(100, result.UnitTypeId);
        Assert.Equal("available", result.PhysicalStatus); // Server strictly assigns available
        Assert.True(result.IsListed); // Server strictly assigns is_listed = true
        Assert.Equal("Manager created this unit", result.Notes);

        // Verify in database
        var inDb = await context.storage_units.FindAsync(result.Id);
        Assert.NotNull(inDb);
        Assert.Equal("available", inDb.physical_status);
        Assert.True(inDb.is_listed);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCodeInFacility_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // "SU-10-01" already exists in Facility 10
        var req = new CreateStorageUnitRequest
        {
            FacilityId = 10,
            UnitTypeId = 100,
            UnitCode = "SU-10-01"
        };

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(req, actorUserId: 10));
    }

    [Fact]
    public async Task CreateAsync_AreaBelongsToDifferentFacility_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // Area 2000 belongs to Facility 20; target unit is in Facility 10
        var req = new CreateStorageUnitRequest
        {
            FacilityId = 10,
            UnitTypeId = 100,
            AreaId = 2000,
            UnitCode = "SU-MISMATCH-AREA"
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateAsync(req, actorUserId: 10));
    }

    [Fact]
    public async Task CreateAsync_MissingRequiredFields_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateAsync(new CreateStorageUnitRequest { FacilityId = 0, UnitTypeId = 100, UnitCode = "CODE" }, actorUserId: 1));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateAsync(new CreateStorageUnitRequest { FacilityId = 10, UnitTypeId = 0, UnitCode = "CODE" }, actorUserId: 1));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateAsync(new CreateStorageUnitRequest { FacilityId = 10, UnitTypeId = 100, UnitCode = "  " }, actorUserId: 1));
    }

    // =========================================================================
    // 5. Edit Policy & Concurrency ("stale version", "Edit chỉ sửa UnitCode, AreaId, FloorLabel, ZoneLabel, Notes; facility/type/state/listing không đổi")
    // =========================================================================

    [Fact]
    public async Task UpdateAsync_ValidEdit_ModifiesAllowedFieldsOnly_KeepsStatusFacilityTypeListingUnchanged()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit1 = await context.storage_units.FindAsync(1L);
        var originalFacilityId = unit1!.facility_id;
        var originalTypeId = unit1.unit_type_id;
        var originalStatus = unit1.physical_status;
        var originalListing = unit1.is_listed;
        var initialUpdatedAt = unit1.updated_at;

        var req = new UpdateStorageUnitRequest
        {
            ExpectedUpdatedAt = initialUpdatedAt,
            UnitCode = "SU-10-01-RENAMED",
            AreaId = 1000,
            FloorLabel = "Tầng 2",
            ZoneLabel = "Khu B",
            Notes = "Updated notes by Manager"
        };

        var result = await service.UpdateAsync(1, req, actorUserId: 10);

        Assert.Equal("SU-10-01-RENAMED", result.UnitCode);
        Assert.Equal("Tầng 2", result.FloorLabel);
        Assert.Equal("Khu B", result.ZoneLabel);
        Assert.Equal("Updated notes by Manager", result.Notes);

        // Assert strictly protected system fields:
        Assert.Equal(originalFacilityId, result.FacilityId);
        Assert.Equal(originalTypeId, result.UnitTypeId);
        Assert.Equal(originalStatus, result.PhysicalStatus); // State did not change!
        Assert.Equal(originalListing, result.IsListed); // Listing did not change!

        // Verify in database
        var inDb = await context.storage_units.FindAsync(1L);
        Assert.NotNull(inDb);
        Assert.Equal("SU-10-01-RENAMED", inDb.unit_code);
        Assert.Equal("available", inDb.physical_status);
        Assert.True(inDb.is_listed);
        Assert.Equal(10, inDb.facility_id);
        Assert.Equal(100, inDb.unit_type_id);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateUnitCode_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit1 = await context.storage_units.FindAsync(1L);

        // Attempting to rename Unit 1 to "SU-10-02" which already belongs to Unit 2
        var req = new UpdateStorageUnitRequest
        {
            ExpectedUpdatedAt = unit1!.updated_at,
            UnitCode = "SU-10-02"
        };

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(1, req, actorUserId: 10));
    }

    [Fact]
    public async Task UpdateAsync_AreaFromDifferentFacility_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit1 = await context.storage_units.FindAsync(1L);

        // Area 2000 is in Facility 20
        var req = new UpdateStorageUnitRequest
        {
            ExpectedUpdatedAt = unit1!.updated_at,
            UnitCode = "SU-10-01",
            AreaId = 2000
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.UpdateAsync(1, req, actorUserId: 10));
    }

    [Fact]
    public async Task UpdateAsync_StaleExpectedUpdatedAt_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10);

        var req = new UpdateStorageUnitRequest
        {
            ExpectedUpdatedAt = staleTimestamp,
            UnitCode = "SU-10-01-EDIT"
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(1, req, actorUserId: 10));

        Assert.Contains("conflict", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_MissingExpectedUpdatedAt_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var req = new UpdateStorageUnitRequest
        {
            ExpectedUpdatedAt = default,
            UnitCode = "SU-10-01-EDIT"
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.UpdateAsync(1, req, actorUserId: 10));
    }

    [Fact]
    public async Task UpdateAsync_UnitNotFound_ThrowsNotFoundException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var req = new UpdateStorageUnitRequest
        {
            ExpectedUpdatedAt = DateTimeOffset.UtcNow,
            UnitCode = "NON-EXISTENT"
        };

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(99999, req, actorUserId: 1));
    }

    // =========================================================================
    // 6. Delete Policy & Capacity Protection ("history Delete", "capacity Delete")
    // =========================================================================

    [Fact]
    public async Task DeleteAsync_UnitNotAvailable_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // Unit 2 has physical_status = 'occupied'
        var unit2 = await context.storage_units.FindAsync(2L);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.DeleteAsync(2, unit2!.updated_at, actorUserId: 10));

        Assert.Contains("available", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_UnitHasHistoryOrReferences_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit1 = await context.storage_units.FindAsync(1L);

        // Add historical allocation to Unit 1
        context.unit_allocations.Add(new unit_allocation
        {
            id = 9901,
            storage_unit_id = 1,
            allocation_kind = "initial",
            allocation_start_date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)),
            allocation_end_date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
            status = "ended",
            created_at = DateTimeOffset.UtcNow.AddMonths(-2)
        });
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.DeleteAsync(1, unit1!.updated_at, actorUserId: 10));

        Assert.Contains("history", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await context.storage_units.FindAsync(1L)); // Still in database
    }

    [Fact]
    public async Task DeleteAsync_ActiveHoldOnFacilityAndUnitType_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit1 = await context.storage_units.FindAsync(1L);
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // Add an active unassigned hold on Facility 10, UnitType 100
        context.reservations.Add(new reservation
        {
            id = 8801,
            facility_id = 10,
            unit_type_id = 100,
            customer_id = 40,
            reservation_code = "RES-HOLD-8801",
            status = "pending",
            hold_until = now.AddMinutes(14), // active hold in future
            start_date = today,
            end_date = today.AddMonths(1)
        });
        await context.SaveChangesAsync();

        // Deleting Unit 1 must be blocked because there is an active hold for this unit type
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.DeleteAsync(1, unit1!.updated_at, actorUserId: 10));

        Assert.Contains("reservation holds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await context.storage_units.FindAsync(1L)); // Preserved
    }

    [Fact]
    public async Task DeleteAsync_ActiveConfirmedReservationOnFacilityAndUnitType_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var unit1 = await context.storage_units.FindAsync(1L);
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // Add an active confirmed unassigned reservation for Facility 10, UnitType 100
        context.reservations.Add(new reservation
        {
            id = 8802,
            facility_id = 10,
            unit_type_id = 100,
            customer_id = 40,
            reservation_code = "RES-CONF-8802",
            status = "confirmed",
            start_date = today,
            end_date = today.AddMonths(1)
        });
        await context.SaveChangesAsync();

        // Deleting Unit 1 must be blocked because there is an unassigned confirmed reservation
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.DeleteAsync(1, unit1!.updated_at, actorUserId: 10));

        Assert.Contains("confirmed bookings", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await context.storage_units.FindAsync(1L)); // Preserved
    }

    [Fact]
    public async Task DeleteAsync_StaleExpectedUpdatedAt_ThrowsConflictException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.DeleteAsync(1, staleTimestamp, actorUserId: 10));

        Assert.Contains("conflict", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_MissingExpectedUpdatedAt_ThrowsBadRequestException()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.DeleteAsync(1, default, actorUserId: 10));
    }

    [Fact]
    public async Task DeleteAsync_CleanAvailableUnitNoCapacityConflict_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        // Unit 3 is Facility 10, UnitType 200, Available, no references, no holds, no confirmed
        var unit3 = await context.storage_units.FindAsync(3L);
        Assert.NotNull(unit3);

        var result = await service.DeleteAsync(3, unit3.updated_at, actorUserId: 10);

        Assert.True(result.Success);
        Assert.Equal(3, result.UnitId);
        Assert.Equal("SU-10-03", result.UnitCode);
        Assert.Equal("STORAGE_UNIT_DELETED", result.Message);

        // Unit 3 must be completely removed from database
        Assert.Null(await context.storage_units.FindAsync(3L));
    }

    // =========================================================================
    // 7. Codex Finding 1 (P1): Terminated Employee Cannot Administer Storage Units
    // =========================================================================

    [Fact]
    public async Task TerminatedOperationsManager_ListAsync_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var now = DateTimeOffset.UtcNow;
        var terminatedOps = new user
        {
            id = 60,
            email = "terminated_ops@selfstorage.com",
            status = UserStatusConstants.Active,
            password_hash = "hash",
            created_at = now,
            updated_at = now
        };
        var urOps = new user_role { user_id = 60, role_id = 2, granted_at = now };
        var epTerminated = new employee_profile
        {
            user_id = 60,
            employee_code = "EMP-OPS-TERM",
            full_name = "Terminated Ops Manager",
            hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            employment_status = EmploymentStatusConstants.Terminated
        };
        context.users.Add(terminatedOps);
        context.user_roles.Add(urOps);
        context.employee_profiles.Add(epTerminated);
        await context.SaveChangesAsync();

        var request = new StorageUnitFilterRequest { PageNumber = 1, PageSize = 10 };
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ListAsync(request, actorUserId: 60));
    }

    [Fact]
    public async Task TerminatedOperationsManager_CreateAsync_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var now = DateTimeOffset.UtcNow;
        var terminatedOps = new user
        {
            id = 60,
            email = "terminated_ops@selfstorage.com",
            status = UserStatusConstants.Active,
            password_hash = "hash",
            created_at = now,
            updated_at = now
        };
        var urOps = new user_role { user_id = 60, role_id = 2, granted_at = now };
        var epTerminated = new employee_profile
        {
            user_id = 60,
            employee_code = "EMP-OPS-TERM",
            full_name = "Terminated Ops Manager",
            hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            employment_status = EmploymentStatusConstants.Terminated
        };
        context.users.Add(terminatedOps);
        context.user_roles.Add(urOps);
        context.employee_profiles.Add(epTerminated);
        await context.SaveChangesAsync();

        var request = new CreateStorageUnitRequest
        {
            FacilityId = 10,
            UnitTypeId = 100,
            UnitCode = "SU-TERM-OPS"
        };
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateAsync(request, actorUserId: 60));
    }

    [Fact]
    public async Task TerminatedOperationsManager_UpdateAsync_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var now = DateTimeOffset.UtcNow;
        var terminatedOps = new user
        {
            id = 60,
            email = "terminated_ops@selfstorage.com",
            status = UserStatusConstants.Active,
            password_hash = "hash",
            created_at = now,
            updated_at = now
        };
        var urOps = new user_role { user_id = 60, role_id = 2, granted_at = now };
        var epTerminated = new employee_profile
        {
            user_id = 60,
            employee_code = "EMP-OPS-TERM",
            full_name = "Terminated Ops Manager",
            hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            employment_status = EmploymentStatusConstants.Terminated
        };
        context.users.Add(terminatedOps);
        context.user_roles.Add(urOps);
        context.employee_profiles.Add(epTerminated);
        await context.SaveChangesAsync();

        var unit = await context.storage_units.FindAsync(1L);
        var request = new UpdateStorageUnitRequest
        {
            UnitCode = "SU-UPD-TERM",
            ExpectedUpdatedAt = unit!.updated_at
        };
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.UpdateAsync(1L, request, actorUserId: 60));
    }

    [Fact]
    public async Task TerminatedOperationsManager_DeleteAsync_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var now = DateTimeOffset.UtcNow;
        var terminatedOps = new user
        {
            id = 60,
            email = "terminated_ops@selfstorage.com",
            status = UserStatusConstants.Active,
            password_hash = "hash",
            created_at = now,
            updated_at = now
        };
        var urOps = new user_role { user_id = 60, role_id = 2, granted_at = now };
        var epTerminated = new employee_profile
        {
            user_id = 60,
            employee_code = "EMP-OPS-TERM",
            full_name = "Terminated Ops Manager",
            hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            employment_status = EmploymentStatusConstants.Terminated
        };
        context.users.Add(terminatedOps);
        context.user_roles.Add(urOps);
        context.employee_profiles.Add(epTerminated);
        await context.SaveChangesAsync();

        var unit = await context.storage_units.FindAsync(3L);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.DeleteAsync(3L, unit!.updated_at, actorUserId: 60));
    }

    [Fact]
    public async Task TerminatedSystemAdministrator_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var now = DateTimeOffset.UtcNow;
        var terminatedAdmin = new user
        {
            id = 61,
            email = "terminated_admin@selfstorage.com",
            status = UserStatusConstants.Active,
            password_hash = "hash",
            created_at = now,
            updated_at = now
        };
        var urAdmin = new user_role { user_id = 61, role_id = 1, granted_at = now };
        var epTerminatedAdmin = new employee_profile
        {
            user_id = 61,
            employee_code = "EMP-ADM-TERM",
            full_name = "Terminated System Admin",
            hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)),
            employment_status = EmploymentStatusConstants.Terminated
        };
        context.users.Add(terminatedAdmin);
        context.user_roles.Add(urAdmin);
        context.employee_profiles.Add(epTerminatedAdmin);
        await context.SaveChangesAsync();

        var request = new StorageUnitFilterRequest { PageNumber = 1, PageSize = 10 };
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ListAsync(request, actorUserId: 61));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetByIdAsync(1L, actorUserId: 61));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetLookupsAsync(actorUserId: 61));
    }

    [Fact]
    public async Task ActiveOperationsManager_RemainsAuthorized()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var request = new StorageUnitFilterRequest { PageNumber = 1, PageSize = 10 };
        var result = await service.ListAsync(request, actorUserId: 2);
        Assert.NotNull(result);
        Assert.True(result.TotalCount > 0);
    }

    // =========================================================================
    // 8. Codex Finding 2 (P2): MonthlyRate Lookup Accuracy & No Zero Fabrication
    // =========================================================================

    [Fact]
    public async Task GetLookupsAsync_WithFacility_ReturnsExactFacilityMonthlyRate()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;

        context.facility_rates.Add(new facility_rate
        {
            id = 1001,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1850000m,
            deposit_amount = 1850000m,
            booking_fee = 50000m,
            valid_from = today.AddDays(-10),
            valid_to = null,
            created_at = now
        });
        await context.SaveChangesAsync();

        var lookups = await service.GetLookupsAsync(actorUserId: 1, facilityId: 10);
        var type100 = lookups.UnitTypes.Single(ut => ut.Id == 100);

        Assert.Equal(1850000m, type100.MonthlyRate);
    }

    [Fact]
    public async Task GetLookupsAsync_DoesNotReturnRateFromDifferentFacility()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;

        context.facility_rates.Add(new facility_rate
        {
            id = 2001,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1200000m,
            deposit_amount = 1200000m,
            booking_fee = 50000m,
            valid_from = today.AddDays(-10),
            valid_to = null,
            created_at = now
        });
        context.facility_rates.Add(new facility_rate
        {
            id = 2002,
            facility_id = 20,
            unit_type_id = 100,
            monthly_rate = 2500000m,
            deposit_amount = 2500000m,
            booking_fee = 50000m,
            valid_from = today.AddDays(-10),
            valid_to = null,
            created_at = now
        });
        await context.SaveChangesAsync();

        var lookupsFac10 = await service.GetLookupsAsync(actorUserId: 1, facilityId: 10);
        var lookupsFac20 = await service.GetLookupsAsync(actorUserId: 1, facilityId: 20);

        var typeFac10 = lookupsFac10.UnitTypes.Single(ut => ut.Id == 100);
        var typeFac20 = lookupsFac20.UnitTypes.Single(ut => ut.Id == 100);

        Assert.Equal(1200000m, typeFac10.MonthlyRate);
        Assert.Equal(2500000m, typeFac20.MonthlyRate);
        Assert.NotEqual(typeFac20.MonthlyRate, typeFac10.MonthlyRate);
    }

    [Fact]
    public async Task GetLookupsAsync_WithMultipleHistoricalRates_SelectsCurrentValidRate()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;

        context.facility_rates.Add(new facility_rate
        {
            id = 3001,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 900000m,
            deposit_amount = 900000m,
            booking_fee = 50000m,
            valid_from = today.AddDays(-60),
            valid_to = today.AddDays(-1),
            created_at = now.AddDays(-60)
        });
        context.facility_rates.Add(new facility_rate
        {
            id = 3002,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1400000m,
            deposit_amount = 1400000m,
            booking_fee = 50000m,
            valid_from = today.AddDays(-10),
            valid_to = today.AddDays(30),
            created_at = now.AddDays(-10)
        });
        context.facility_rates.Add(new facility_rate
        {
            id = 3003,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1900000m,
            deposit_amount = 1900000m,
            booking_fee = 50000m,
            valid_from = today.AddDays(31),
            valid_to = null,
            created_at = now
        });
        await context.SaveChangesAsync();

        var lookups = await service.GetLookupsAsync(actorUserId: 1, facilityId: 10);
        var type100 = lookups.UnitTypes.Single(ut => ut.Id == 100);

        Assert.Equal(1400000m, type100.MonthlyRate);
    }

    [Fact]
    public async Task GetLookupsAsync_WithoutConfiguredRate_DoesNotFabricateZeroAsRealRate()
    {
        var dbName = Guid.NewGuid().ToString();
        var (service, context) = CreateTestService(dbName);
        await SeedDatabaseAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;

        // Facility 10 only has an EXPIRED rate for Type 100
        context.facility_rates.Add(new facility_rate
        {
            id = 4001,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1100000m,
            deposit_amount = 1100000m,
            booking_fee = 50000m,
            valid_from = today.AddDays(-100),
            valid_to = today.AddDays(-5),
            created_at = now.AddDays(-100)
        });
        await context.SaveChangesAsync();

        // 1. Queried for Facility 10 where the only rate is expired:
        var lookupsFac10 = await service.GetLookupsAsync(actorUserId: 1, facilityId: 10);
        var type100Fac10 = lookupsFac10.UnitTypes.Single(ut => ut.Id == 100);

        Assert.Null(type100Fac10.MonthlyRate);
        Assert.NotEqual<decimal?>(0m, type100Fac10.MonthlyRate);

        // 2. Queried with facilityId = null (global lookups with multiple facilities):
        var lookupsGlobal = await service.GetLookupsAsync(actorUserId: 1, facilityId: null);
        Assert.NotEmpty(lookupsGlobal.UnitTypes);
        foreach (var ut in lookupsGlobal.UnitTypes)
        {
            Assert.Null(ut.MonthlyRate);
            Assert.NotEqual<decimal?>(0m, ut.MonthlyRate);
        }
    }

    // =========================================================================
    // 9. Re-Review Findings: Facility Timezone & Half-Open Validity Interval [valid_from, valid_to)
    // =========================================================================

    [Fact]
    public async Task GetLookupsAsync_FacilityTimezoneAheadOfUtc_SelectsLocalCurrentRate()
    {
        var dbName = Guid.NewGuid().ToString();
        // Fixed UTC: 2026-10-15 20:00:00 UTC
        // Facility 10 has timezone = "Asia/Ho_Chi_Minh" (UTC+7).
        // In local time: 2026-10-15 20:00 + 7 hours = 2026-10-16 03:00.
        // Effective local date is 2026-10-16.
        var utcNow = new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);
        var fixedTimeProvider = new FixedTimeProvider(utcNow);

        var (service, context) = CreateTestService(dbName, fixedTimeProvider);
        await SeedDatabaseAsync(context);

        // Rate 1: Old rate valid until 2026-10-16 (under [valid_from, valid_to), expired on 2026-10-16)
        context.facility_rates.Add(new facility_rate
        {
            id = 5001,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1_000_000m,
            deposit_amount = 1_000_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 1),
            valid_to = new DateOnly(2026, 10, 16),
            created_at = utcNow.AddDays(-10)
        });

        // Rate 2: New rate valid starting 2026-10-16
        context.facility_rates.Add(new facility_rate
        {
            id = 5002,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 2_000_000m,
            deposit_amount = 2_000_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 16),
            valid_to = null,
            created_at = utcNow.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var lookups = await service.GetLookupsAsync(actorUserId: 1, facilityId: 10);
        var type100 = lookups.UnitTypes.Single(ut => ut.Id == 100);

        // Under UTC, the date would be 2026-10-15, which would fail to select Rate 2.
        // Under facility timezone Asia/Ho_Chi_Minh, local date is 2026-10-16, selecting Rate 2.
        Assert.Equal(2_000_000m, type100.MonthlyRate);
    }

    [Fact]
    public async Task GetLookupsAsync_RateWithValidToEqualToday_IsExpired()
    {
        var dbName = Guid.NewGuid().ToString();
        var utcNow = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
        var fixedTimeProvider = new FixedTimeProvider(utcNow);

        var (service, context) = CreateTestService(dbName, fixedTimeProvider);
        await SeedDatabaseAsync(context);

        // Effective date in Facility 10 is 2026-10-16.
        // Rate has valid_to == 2026-10-16. Half-open interval [valid_from, valid_to) dictates that
        // when effectiveDate == valid_to, effectiveDate < valid_to is false -> rate is expired.
        context.facility_rates.Add(new facility_rate
        {
            id = 5003,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1_500_000m,
            deposit_amount = 1_500_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 1),
            valid_to = new DateOnly(2026, 10, 16),
            created_at = utcNow.AddDays(-15)
        });
        await context.SaveChangesAsync();

        var lookups = await service.GetLookupsAsync(actorUserId: 1, facilityId: 10);
        var type100 = lookups.UnitTypes.Single(ut => ut.Id == 100);

        Assert.Null(type100.MonthlyRate);
    }

    [Fact]
    public async Task GetLookupsAsync_RateWithValidToTomorrow_IsActive()
    {
        var dbName = Guid.NewGuid().ToString();
        var utcNow = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
        var fixedTimeProvider = new FixedTimeProvider(utcNow);

        var (service, context) = CreateTestService(dbName, fixedTimeProvider);
        await SeedDatabaseAsync(context);

        // Effective date in Facility 10 is 2026-10-16.
        // Rate has valid_to == 2026-10-17. Since 2026-10-16 < 2026-10-17 is true, rate is active today.
        context.facility_rates.Add(new facility_rate
        {
            id = 5004,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1_750_000m,
            deposit_amount = 1_750_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 1),
            valid_to = new DateOnly(2026, 10, 17),
            created_at = utcNow.AddDays(-15)
        });
        await context.SaveChangesAsync();

        var lookups = await service.GetLookupsAsync(actorUserId: 1, facilityId: 10);
        var type100 = lookups.UnitTypes.Single(ut => ut.Id == 100);

        Assert.Equal(1_750_000m, type100.MonthlyRate);
    }

    [Fact]
    public async Task GetLookupsAsync_GlobalLookup_KeepsMonthlyRateNull()
    {
        var dbName = Guid.NewGuid().ToString();
        var utcNow = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
        var fixedTimeProvider = new FixedTimeProvider(utcNow);

        var (service, context) = CreateTestService(dbName, fixedTimeProvider);
        await SeedDatabaseAsync(context);

        // Rates configured for both Facility 10 and Facility 20
        context.facility_rates.Add(new facility_rate
        {
            id = 5005,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1_200_000m,
            deposit_amount = 1_200_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 1),
            valid_to = null,
            created_at = utcNow
        });
        context.facility_rates.Add(new facility_rate
        {
            id = 5006,
            facility_id = 20,
            unit_type_id = 100,
            monthly_rate = 2_200_000m,
            deposit_amount = 2_200_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 1),
            valid_to = null,
            created_at = utcNow
        });
        await context.SaveChangesAsync();

        // Global lookup with facilityId = null: must keep MonthlyRate = null for all unit types
        var lookups = await service.GetLookupsAsync(actorUserId: 1, facilityId: null);

        Assert.NotEmpty(lookups.UnitTypes);
        foreach (var unitType in lookups.UnitTypes)
        {
            Assert.Null(unitType.MonthlyRate);
        }
    }

    // =========================================================================
    // 10. Task T06-F02: MapToDetailDto No Fallback to Expired or Future Rates
    // =========================================================================

    [Fact]
    public async Task GetByIdAsync_RateWithValidToEqualLocalToday_ReturnsZero()
    {
        var dbName = Guid.NewGuid().ToString();
        var utcNow = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
        var fixedTimeProvider = new FixedTimeProvider(utcNow);

        var (service, context) = CreateTestService(dbName, fixedTimeProvider);
        await SeedDatabaseAsync(context);

        // Unit 1 is in Facility 10 ("Asia/Ho_Chi_Minh", UTC+7) with UnitType 100.
        // Effective local date is 2026-10-16.
        // Rate has valid_to == local today (2026-10-16). Under [valid_from, valid_to), it is expired.
        context.facility_rates.Add(new facility_rate
        {
            id = 8001,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1_500_000m,
            deposit_amount = 1_500_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 1),
            valid_to = new DateOnly(2026, 10, 16),
            created_at = utcNow.AddDays(-15)
        });
        await context.SaveChangesAsync();

        var detail = await service.GetByIdAsync(1, actorUserId: 1);

        Assert.NotNull(detail);
        Assert.Equal(0m, detail.MonthlyRate);
    }

    [Fact]
    public async Task GetByIdAsync_FutureRateOnly_ReturnsZero()
    {
        var dbName = Guid.NewGuid().ToString();
        var utcNow = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
        var fixedTimeProvider = new FixedTimeProvider(utcNow);

        var (service, context) = CreateTestService(dbName, fixedTimeProvider);
        await SeedDatabaseAsync(context);

        // Unit 1 in Facility 10, UnitType 100.
        // Only configured rate starts in the future (2026-10-20).
        context.facility_rates.Add(new facility_rate
        {
            id = 8002,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 2_500_000m,
            deposit_amount = 2_500_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 20),
            valid_to = null,
            created_at = utcNow
        });
        await context.SaveChangesAsync();

        var detail = await service.GetByIdAsync(1, actorUserId: 1);

        Assert.NotNull(detail);
        Assert.Equal(0m, detail.MonthlyRate);
    }

    [Fact]
    public async Task GetByIdAsync_MultipleEffectiveRates_SelectsLatestValidFrom()
    {
        var dbName = Guid.NewGuid().ToString();
        var utcNow = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
        var fixedTimeProvider = new FixedTimeProvider(utcNow);

        var (service, context) = CreateTestService(dbName, fixedTimeProvider);
        await SeedDatabaseAsync(context);

        // Unit 1 in Facility 10, UnitType 100. Local date: 2026-10-16.
        // Rate A: valid_from 2026-10-01
        context.facility_rates.Add(new facility_rate
        {
            id = 8003,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1_200_000m,
            deposit_amount = 1_200_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 1),
            valid_to = null,
            created_at = utcNow.AddDays(-15)
        });
        // Rate B: valid_from 2026-10-10 (more recent valid_from)
        context.facility_rates.Add(new facility_rate
        {
            id = 8004,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1_800_000m,
            deposit_amount = 1_800_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 10),
            valid_to = null,
            created_at = utcNow.AddDays(-6)
        });
        await context.SaveChangesAsync();

        var detail = await service.GetByIdAsync(1, actorUserId: 1);

        Assert.NotNull(detail);
        Assert.Equal(1_800_000m, detail.MonthlyRate);
    }

    [Fact]
    public async Task GetByIdAsync_FacilityTimezoneAheadOfUtc_UsesLocalDate()
    {
        var dbName = Guid.NewGuid().ToString();
        // Fixed UTC: 2026-10-15 20:00:00 UTC
        // Facility 10 ("Asia/Ho_Chi_Minh", UTC+7): local time is 2026-10-16 03:00 -> date is 2026-10-16.
        var utcNow = new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero);
        var fixedTimeProvider = new FixedTimeProvider(utcNow);

        var (service, context) = CreateTestService(dbName, fixedTimeProvider);
        await SeedDatabaseAsync(context);

        // Rate 1: Old rate valid until 2026-10-16 (under [valid_from, valid_to), expired on 2026-10-16)
        context.facility_rates.Add(new facility_rate
        {
            id = 8005,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 1_000_000m,
            deposit_amount = 1_000_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 1),
            valid_to = new DateOnly(2026, 10, 16),
            created_at = utcNow.AddDays(-10)
        });
        // Rate 2: New rate active starting 2026-10-16
        context.facility_rates.Add(new facility_rate
        {
            id = 8006,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 2_100_000m,
            deposit_amount = 2_100_000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 10, 16),
            valid_to = null,
            created_at = utcNow.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var detail = await service.GetByIdAsync(1, actorUserId: 1);

        Assert.NotNull(detail);
        // Under UTC date (2026-10-15), Rate 2 is not valid yet and Rate 1 would be selected or fail.
        // Under local date (2026-10-16), Rate 1 is expired and Rate 2 is active.
        Assert.Equal(2_100_000m, detail.MonthlyRate);
    }
}
