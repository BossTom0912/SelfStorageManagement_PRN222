using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.CheckIn;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class CheckInServiceTests
{
    private const string PinHmacSecret = "TestPinHmacSecretKeyMustBe32BytesLong!";

    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    private (CheckInService Service, SelfStorageDbContext Context, CheckInRepository Repo, FacilityCatalogRepository CatalogRepo) SetupService(string dbName)
    {
        var context = CreateInMemoryContext(dbName);
        var checkInRepo = new CheckInRepository(context);
        var catalogRepo = new FacilityCatalogRepository(context);
        var userRepo = new UserRepository(context);
        var scopeService = new FacilityScopeService(context, userRepo);
        var logger = NullLogger<CheckInService>.Instance;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CheckIn:PinHmacSecret"] = PinHmacSecret
            })
            .Build();

        var service = new CheckInService(checkInRepo, scopeService, config);
        return (service, context, checkInRepo, catalogRepo);
    }

    private async Task SeedBaselineDataAsync(
        SelfStorageDbContext context,
        long customerUserId = 10,
        long staffUserId = 20,
        long unassignedStaffUserId = 30,
        long facilityId = 1,
        string? customerIdNumber = "079201001234")
    {
        // Roles
        var customerRole = new role { id = 1, code = RoleConstants.StorageCustomer, display_name = "Customer" };
        var staffRole = new role { id = 2, code = RoleConstants.FacilityStaff, display_name = "Facility Staff" };
        var managerRole = new role { id = 3, code = RoleConstants.FacilityManager, display_name = "Facility Manager" };
        context.roles.AddRange(customerRole, staffRole, managerRole);

        // Facility
        var facility = new facility
        {
            id = facilityId,
            code = "FAC-HCM",
            name = "Co So Thu Duc",
            address_line = "123 Vo Van Ngan",
            city = "Ho Chi Minh City",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh"
        };
        context.facilities.Add(facility);

        // Unit Type
        var unitType = new unit_type
        {
            id = 1,
            code = "STANDARD-S",
            name = "Kho Standard S 3m2",
            area_m2 = 3.0m,
            climate_controlled = false,
            is_active = true
        };
        context.unit_types.Add(unitType);

        // Policy Version
        var policy = new policy_version
        {
            id = 1,
            policy_type = "rental_terms",
            version = "2026.1",
            content = "{\"terms\":\"Standard\"}",
            valid_from = new DateOnly(2020, 1, 1),
            created_at = DateTimeOffset.UtcNow
        };
        context.policy_versions.Add(policy);

        // Storage Units
        var unit1 = new storage_unit
        {
            id = 101,
            facility_id = facilityId,
            unit_type_id = 1,
            unit_code = "A-101",
            physical_status = "available",
            is_listed = true,
            floor_label = "Tang 1",
            zone_label = "Khu A"
        };
        var unit2 = new storage_unit
        {
            id = 102,
            facility_id = facilityId,
            unit_type_id = 1,
            unit_code = "A-102",
            physical_status = "available",
            is_listed = true,
            floor_label = "Tang 1",
            zone_label = "Khu A"
        };
        context.storage_units.AddRange(unit1, unit2);

        // Users & Profiles
        var customerUser = new user
        {
            id = customerUserId,
            email = "customer@test.com",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var customerProfile = new customer_profile
        {
            user_id = customerUserId,
            full_name = "Nguyen Van Khach",
            identity_number = customerIdNumber,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        customerUser.user_roleusers.Add(new user_role { user_id = customerUserId, role_id = 1, role = customerRole, granted_at = DateTimeOffset.UtcNow });

        var staffUser = new user
        {
            id = staffUserId,
            email = "staff@test.com",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var staffProfile = new employee_profile
        {
            user_id = staffUserId,
            employee_code = "EMP001",
            full_name = "Tran Nhan Vien",
            employment_status = "active",
            hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        staffUser.user_roleusers.Add(new user_role { user_id = staffUserId, role_id = 2, role = staffRole, granted_at = DateTimeOffset.UtcNow });

        // Staff assignment to facility
        var assignment = new staff_facility_assignment
        {
            id = 1,
            employee_id = staffUserId,
            facility_id = facilityId,
            assignment_role = RoleConstants.FacilityStaff,
            starts_at = DateTimeOffset.UtcNow.AddMonths(-1),
            ends_at = null,
            created_at = DateTimeOffset.UtcNow
        };
        context.staff_facility_assignments.Add(assignment);

        // Unassigned staff
        var unassignedStaff = new user
        {
            id = unassignedStaffUserId,
            email = "unassigned@test.com",
            password_hash = "hash",
            status = UserStatusConstants.Active,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        var unassignedProfile = new employee_profile
        {
            user_id = unassignedStaffUserId,
            employee_code = "EMP002",
            full_name = "Le Khong Co So",
            employment_status = "active",
            hire_date = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        unassignedStaff.user_roleusers.Add(new user_role { user_id = unassignedStaffUserId, role_id = 2, role = staffRole, granted_at = DateTimeOffset.UtcNow });

        context.users.AddRange(customerUser, staffUser, unassignedStaff);
        context.customer_profiles.Add(customerProfile);
        context.employee_profiles.AddRange(staffProfile, unassignedProfile);

        await context.SaveChangesAsync();
    }

    private async Task<(reservation Reservation, rental_agreement Agreement, invoice Invoice)> SeedConfirmedReservationAndScheduledAgreementAsync(
        SelfStorageDbContext context,
        long reservationId = 1,
        long agreementId = 1,
        long invoiceId = 1,
        long customerUserId = 10,
        long facilityId = 1,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool isPaid = true)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sDate = startDate ?? today;
        var eDate = endDate ?? today.AddMonths(1);

        var reservation = new reservation
        {
            id = reservationId,
            reservation_code = $"RSV-{reservationId:D6}",
            customer_id = customerUserId,
            facility_id = facilityId,
            unit_type_id = 1,
            start_date = sDate,
            end_date = eDate,
            status = "confirmed",
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            created_at = DateTimeOffset.UtcNow.AddDays(-1),
            updated_at = DateTimeOffset.UtcNow.AddDays(-1)
        };

        var agreement = new rental_agreement
        {
            id = agreementId,
            agreement_no = $"AGR-{agreementId:D6}",
            reservation_id = reservationId,
            customer_id = customerUserId,
            facility_id = facilityId,
            policy_version_id = 1,
            start_date = sDate,
            end_date = eDate,
            monthly_rate_snapshot = 1000000m,
            deposit_snapshot = 1000000m,
            deposit_balance = 1000000m,
            status = "scheduled",
            created_at = DateTimeOffset.UtcNow.AddDays(-1),
            updated_at = DateTimeOffset.UtcNow.AddDays(-1)
        };

        var invoice = new invoice
        {
            id = invoiceId,
            invoice_no = $"INV-{invoiceId:D6}",
            customer_id = customerUserId,
            reservation_id = reservationId,
            agreement_id = agreementId,
            issue_date = sDate,
            due_date = sDate.AddDays(7),
            currency = "VND",
            subtotal_amount = 2000000m,
            discount_amount = 0m,
            tax_amount = 0m,
            total_amount = 2000000m,
            paid_amount = isPaid ? 2000000m : 0m,
            status = isPaid ? "paid" : "issued",
            created_at = DateTimeOffset.UtcNow.AddDays(-1)
        };

        context.reservations.Add(reservation);
        context.rental_agreements.Add(agreement);
        context.invoices.Add(invoice);

        await context.SaveChangesAsync();
        return (reservation, agreement, invoice);
    }

    [Fact]
    public async Task SearchCheckIn_UnassignedStaff_ThrowsForbiddenException()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.SearchCheckInAsync("RSV-000001", 30, [RoleConstants.FacilityStaff]));
    }

    [Fact]
    public async Task SearchCheckIn_AssignedStaff_ReturnsDetailsAndMasksIdentityNumber()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context, customerIdNumber: "079201001234");
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        var result = await service.SearchCheckInAsync("RSV-000001", 20, [RoleConstants.FacilityStaff]);

        Assert.NotNull(result);
        Assert.Equal("RSV-000001", result.ReservationCode);
        Assert.Equal("Nguyen Van Khach", result.CustomerName);
        Assert.True(result.HasIdentityNumber);
        Assert.Equal("********1234", result.MaskedIdentityNumber);
        Assert.Equal("confirmed", result.ReservationStatus);
        Assert.Equal("scheduled", result.AgreementStatus);
        Assert.True(result.IsFullyPaid);
    }

    [Fact]
    public async Task GetEligibleUnits_AssignedStaff_ReturnsListedAvailableUnits()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        var units = await service.GetEligibleUnitsAsync(1, 20, [RoleConstants.FacilityStaff]);

        Assert.Equal(2, units.Count);
        Assert.Contains(units, u => u.UnitCode == "A-101");
        Assert.Contains(units, u => u.UnitCode == "A-102");
    }

    [Fact]
    public async Task VerifyIdentity_ExistingNumber_Matches_Succeeds()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context, customerIdNumber: "079201001234");
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        var request = new VerifyIdentityRequest
        {
            VerificationMethod = "government_id",
            DocumentNumber = "079201001234",
            IsConfirmedMatch = true,
            Notes = "Giay to hop le"
        };

        var response = await service.VerifyIdentityAsync(1, request, 20, [RoleConstants.FacilityStaff]);

        Assert.NotNull(response);
        Assert.Equal("verified", response.Result);
        Assert.Equal("government_id", response.VerificationMethod);

        var stored = await context.identity_verifications.FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("verified", stored.result);
        Assert.Equal(20, stored.verified_by);
    }

    [Fact]
    public async Task VerifyIdentity_ExistingNumber_Mismatches_ThrowsBadRequest()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context, customerIdNumber: "079201001234");
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        var request = new VerifyIdentityRequest
        {
            VerificationMethod = "government_id",
            DocumentNumber = "999999999999",
            IsConfirmedMatch = true
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.VerifyIdentityAsync(1, request, 20, [RoleConstants.FacilityStaff]));

        var stored = await context.identity_verifications.FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("rejected", stored.result);
    }

    [Fact]
    public async Task VerifyIdentity_EmptyProfileNumber_StaffSuppliesNumber_UpdatesCustomerProfile()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context, customerIdNumber: null);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        var request = new VerifyIdentityRequest
        {
            VerificationMethod = "passport",
            DocumentNumber = "B1234567",
            IsConfirmedMatch = true
        };

        var response = await service.VerifyIdentityAsync(1, request, 20, [RoleConstants.FacilityStaff]);

        Assert.Equal("verified", response.Result);

        var customerProfile = await context.customer_profiles.FirstAsync(cp => cp.user_id == 10);
        Assert.Equal("B1234567", customerProfile.identity_number);
    }

    [Fact]
    public async Task AssignUnit_ValidUnit_SetsUnitReservedAndCreatesActiveAllocation()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Verify ID first
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);

        var request = new AssignUnitRequest { StorageUnitId = 101 };
        var allocation = await service.AssignUnitAsync(1, request, 20, [RoleConstants.FacilityStaff]);

        Assert.NotNull(allocation);
        Assert.Equal(101, allocation.StorageUnitId);
        Assert.Equal("active", allocation.Status);
        Assert.Equal("rental", allocation.AllocationKind);

        var unit = await context.storage_units.FindAsync(101L);
        Assert.Equal("reserved", unit!.physical_status);
    }

    [Fact]
    public async Task AssignUnit_SameUnitReplay_ReturnsExistingAllocation()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Verify ID first
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);

        var request = new AssignUnitRequest { StorageUnitId = 101 };
        var first = await service.AssignUnitAsync(1, request, 20, [RoleConstants.FacilityStaff]);
        var second = await service.AssignUnitAsync(1, request, 20, [RoleConstants.FacilityStaff]);

        Assert.Equal(first.AllocationId, second.AllocationId);
    }

    [Fact]
    public async Task AssignUnit_DifferentUnitWhenActiveExists_ThrowsConflict()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Verify ID first
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);

        await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 102 }, 20, [RoleConstants.FacilityStaff]));
    }

    [Fact]
    public async Task ReleaseUnit_ValidRequest_ReleasesAllocationAndRestoresUnitToAvailable()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Verify ID first
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);

        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        var releaseReq = new ReleaseUnitRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Reason = "Khach muon doi sang kho khac"
        };
        var releasedAlloc = await service.ReleaseUnitAsync(1, releaseReq, 20, [RoleConstants.FacilityStaff]);

        Assert.Equal("released", releasedAlloc.Status);
        var unit = await context.storage_units.FindAsync(101L);
        Assert.Equal("available", unit!.physical_status);
    }

    [Fact]
    public async Task Inspection_IncompleteChecklist_ThrowsBadRequest()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Verify ID first
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);

        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        var request = new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "good" },
                new() { ItemName = "khóa", Condition = "good" }
                // Missing đèn and vệ sinh
            }
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.PerformInspectionAsync(1, request, 20, [RoleConstants.FacilityStaff]));
    }

    [Fact]
    public async Task Inspection_AllGood_CompletesInspection()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Verify ID first
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);

        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        var request = new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "good" },
                new() { ItemName = "khóa", Condition = "good" },
                new() { ItemName = "đèn", Condition = "good" },
                new() { ItemName = "vệ sinh", Condition = "good" }
            }
        };

        var response = await service.PerformInspectionAsync(1, request, 20, [RoleConstants.FacilityStaff]);

        Assert.Equal("completed", response.Status);
        Assert.Equal("good", response.OverallCondition);
    }

    [Fact]
    public async Task Inspection_DamagedItem_FailsInspection_ReleasesAllocationAndSetsUnitToMaintenance()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Verify ID first
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);

        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        var request = new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "damaged", Notes = "Cua bi ket" },
                new() { ItemName = "khóa", Condition = "good" },
                new() { ItemName = "đèn", Condition = "good" },
                new() { ItemName = "vệ sinh", Condition = "good" }
            }
        };

        var response = await service.PerformInspectionAsync(1, request, 20, [RoleConstants.FacilityStaff]);

        Assert.Equal("failed", response.Status);

        var updatedAlloc = await context.unit_allocations.FindAsync(alloc.AllocationId);
        Assert.Equal("released", updatedAlloc!.status);

        var unit = await context.storage_units.FindAsync(101L);
        Assert.Equal("maintenance", unit!.physical_status);

        // Reservation and agreement remain confirmed/scheduled
        var res = await context.reservations.FindAsync(1L);
        var agr = await context.rental_agreements.FindAsync(1L);
        Assert.Equal("confirmed", res!.status);
        Assert.Equal("scheduled", agr!.status);
    }

    [Fact]
    public async Task CompleteCheckIn_FullWorkflow_Succeeds_GeneratesPin_TransitionsToOccupied()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // 1. Verify ID
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest
        {
            VerificationMethod = "government_id",
            DocumentNumber = "079201001234",
            IsConfirmedMatch = true
        }, 20, [RoleConstants.FacilityStaff]);

        // 2. Assign Unit
        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        // 3. Inspect
        var insp = await service.PerformInspectionAsync(1, new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "good" },
                new() { ItemName = "khóa", Condition = "good" },
                new() { ItemName = "đèn", Condition = "good" },
                new() { ItemName = "vệ sinh", Condition = "good" }
            }
        }, 20, [RoleConstants.FacilityStaff]);

        // 4. Complete
        var completeReq = new CompleteCheckInRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            CurrentInspectionId = insp.InspectionId,
            CustomerAcknowledged = true,
            Notes = "Khach hang da ky bien ban nhan kho"
        };

        var completeRes = await service.CompleteCheckInAsync(1, completeReq, 20, [RoleConstants.FacilityStaff]);

        Assert.NotNull(completeRes);
        Assert.False(completeRes.IsAlreadyCompleted);
        Assert.NotNull(completeRes.Pin);
        Assert.Equal(6, completeRes.Pin.Length);
        Assert.True(int.TryParse(completeRes.Pin, out _));
        Assert.Equal("AGR-000001", completeRes.AgreementNo);
        Assert.Equal("A-101", completeRes.UnitCode);

        // Verify DB entities
        var res = await context.reservations.FindAsync(1L);
        var agr = await context.rental_agreements.FindAsync(1L);
        var unit = await context.storage_units.FindAsync(101L);
        Assert.Equal("checked_in", res!.status);
        Assert.Equal("active", agr!.status);
        Assert.NotNull(agr.checked_in_at);
        Assert.NotNull(agr.signed_at);
        Assert.Equal("occupied", unit!.physical_status);

        // Verify credential created
        var cred = await context.access_credentials.FirstOrDefaultAsync(c => c.agreement_id == agr.id && c.status == "active");
        Assert.NotNull(cred);
        Assert.NotNull(cred.secret_digest);

        // Verify in_app notification created
        var notif = await context.notifications.FirstOrDefaultAsync(n => n.user_id == 10 && n.channel == "in_app");
        Assert.NotNull(notif);
        Assert.Contains("bàn giao", notif.subject, StringComparison.OrdinalIgnoreCase);

        // 5. Replay idempotent check-in
        var replayRes = await service.CompleteCheckInAsync(1, completeReq, 20, [RoleConstants.FacilityStaff]);
        Assert.True(replayRes.IsAlreadyCompleted);
        Assert.Null(replayRes.Pin); // Pin is null on replay per security rules
    }

    [Fact]
    public async Task CompleteCheckIn_CustomerNotAcknowledged_ThrowsBadRequest()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest
        {
            VerificationMethod = "government_id",
            DocumentNumber = "079201001234",
            IsConfirmedMatch = true
        }, 20, [RoleConstants.FacilityStaff]);

        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        var insp = await service.PerformInspectionAsync(1, new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "good" },
                new() { ItemName = "khóa", Condition = "good" },
                new() { ItemName = "đèn", Condition = "good" },
                new() { ItemName = "vệ sinh", Condition = "good" }
            }
        }, 20, [RoleConstants.FacilityStaff]);

        var completeReq = new CompleteCheckInRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            CurrentInspectionId = insp.InspectionId,
            CustomerAcknowledged = false
        };

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CompleteCheckInAsync(1, completeReq, 20, [RoleConstants.FacilityStaff]));
    }

    [Fact]
    public async Task ReissuePin_RevokesOldCredential_IssuesNewCredentialAndNotification()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Verify, assign, inspect, complete
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);
        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);
        var insp = await service.PerformInspectionAsync(1, new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "good" },
                new() { ItemName = "khóa", Condition = "good" },
                new() { ItemName = "đèn", Condition = "good" },
                new() { ItemName = "vệ sinh", Condition = "good" }
            }
        }, 20, [RoleConstants.FacilityStaff]);

        var complete = await service.CompleteCheckInAsync(1, new CompleteCheckInRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            CurrentInspectionId = insp.InspectionId,
            CustomerAcknowledged = true
        }, 20, [RoleConstants.FacilityStaff]);

        var initialCred = await context.access_credentials.FirstAsync(c => c.agreement_id == 1 && c.status == "active");

        // Reissue PIN with document verification
        var reissueReq = new ReissuePinRequest
        {
            CurrentCredentialId = initialCred.id,
            DocumentNumber = "079201001234",
            IsConfirmedMatch = true,
            Reason = "Khach quen ma PIN"
        };
        var reissueRes = await service.ReissuePinAsync(1, reissueReq, 20, [RoleConstants.FacilityStaff]);

        Assert.NotNull(reissueRes.Pin);
        Assert.Equal(6, reissueRes.Pin.Length);
        Assert.NotEqual(initialCred.id, reissueRes.NewCredentialId);

        var oldCred = await context.access_credentials.FindAsync(initialCred.id);
        Assert.Equal("revoked", oldCred!.status);
        Assert.NotNull(oldCred.revoked_at);

        var newCred = await context.access_credentials.FindAsync(reissueRes.NewCredentialId);
        Assert.Equal("active", newCred!.status);
    }

    [Fact]
    public async Task CustomerEndpoints_OnlyReturnsCustomerOwnData()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Complete check-in for customer 10
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);
        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);
        var insp = await service.PerformInspectionAsync(1, new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "good" },
                new() { ItemName = "khóa", Condition = "good" },
                new() { ItemName = "đèn", Condition = "good" },
                new() { ItemName = "vệ sinh", Condition = "good" }
            }
        }, 20, [RoleConstants.FacilityStaff]);
        await service.CompleteCheckInAsync(1, new CompleteCheckInRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            CurrentInspectionId = insp.InspectionId,
            CustomerAcknowledged = true
        }, 20, [RoleConstants.FacilityStaff]);

        // Customer 10 requests their units
        var units10 = await service.GetCustomerUnitsAsync(10, new PagedRequest { PageNumber = 1, PageSize = 10 });
        Assert.Equal(1, units10.TotalCount);
        Assert.Equal("A-101", units10.Items.First().UnitCode);

        // Customer 99 requests their units
        var units99 = await service.GetCustomerUnitsAsync(99, new PagedRequest { PageNumber = 1, PageSize = 10 });
        Assert.Equal(0, units99.TotalCount);

        // Customer 10 requests handover
        var handover10 = await service.GetCustomerHandoverAsync(10, 1);
        Assert.NotNull(handover10);
        Assert.Equal("AGR-000001", handover10.AgreementNo);

        // Customer 99 requests customer 10's handover -> throws NotFoundException (to prevent enumeration)
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetCustomerHandoverAsync(99, 1));
    }

    [Fact]
    public async Task CapacityRegression_ReservationWithActiveRentalAllocation_NotCountedAsUnassigned()
    {
        var (service, context, _, catalogRepo) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        var (res, agr, _) = await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Before allocation: reservation has no allocation, so it IS counted as unassigned
        var unassignedBefore = await catalogRepo.CountUnassignedActiveReservationsAsync(1, 1, today, today.AddMonths(1), DateTimeOffset.UtcNow);
        Assert.Equal(1, unassignedBefore);

        // Verify ID first
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);

        // Assign unit (creates unit_allocation with agreement_id = agr.id, reservation_id = null, status = 'active')
        await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        // After allocation: agreement now has an active rental allocation!
        // Capacity queries must NOT double count this reservation as unassigned.
        var unassignedAfter = await catalogRepo.CountUnassignedActiveReservationsAsync(1, 1, today, today.AddMonths(1), DateTimeOffset.UtcNow);
        Assert.Equal(0, unassignedAfter);
    }

    [Fact]
    public async Task Inspection_WhitespaceAndCasing_NormalizesConditionAndCategoryCorrectly()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);
        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        // 1. " Damaged " with mixed-case and padded whitespace must result in failed inspection with "damaged"
        var inspResult = await service.PerformInspectionAsync(1, new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = " CỬA ", Condition = " Damaged " },
                new() { ItemName = " KHÓA ", Condition = " GOOD " },
                new() { ItemName = " ĐÈN ", Condition = " Good " },
                new() { ItemName = " VỆ SINH ", Condition = " Good " }
            }
        }, 20, [RoleConstants.FacilityStaff]);

        Assert.Equal("failed", inspResult.Status);
        Assert.Equal("damaged", inspResult.OverallCondition);
        Assert.True(inspResult.UnitReleasedDueToFailure);

        // Unit transitioned to maintenance
        var unit = await context.storage_units.FindAsync(101L);
        Assert.NotNull(unit);
        Assert.Equal("maintenance", unit.physical_status);

        // Allocation ended
        var allocEntity = await context.unit_allocations.FindAsync(alloc.AllocationId);
        Assert.NotNull(allocEntity);
        Assert.Equal("released", allocEntity.status);
    }

    [Fact]
    public async Task Inspection_NotApplicableCondition_ThrowsBadRequestException()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);
        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        // not_applicable is forbidden for mandatory checklist items
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.PerformInspectionAsync(1, new UnitInspectionRequest
            {
                CurrentAllocationId = alloc.AllocationId,
                Items = new List<InspectionItemRequest>
                {
                    new() { ItemName = "cửa", Condition = "not_applicable" },
                    new() { ItemName = "khóa", Condition = "good" },
                    new() { ItemName = "đèn", Condition = "good" },
                    new() { ItemName = "vệ sinh", Condition = "good" }
                }
            }, 20, [RoleConstants.FacilityStaff]));

        Assert.Contains("not_applicable", ex.Message);
    }

    [Fact]
    public async Task Inspection_IsUnsafeFlagTrue_FailsInspectionAndSetsConditionToUnsafe()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);
        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        // All items good, but IsUnsafe = true
        var inspResult = await service.PerformInspectionAsync(1, new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            IsUnsafe = true,
            OverallSummary = "Trần kho có dấu hiệu rạn nứt nguy hiểm",
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "good" },
                new() { ItemName = "khóa", Condition = "good" },
                new() { ItemName = "đèn", Condition = "good" },
                new() { ItemName = "vệ sinh", Condition = "good" }
            }
        }, 20, [RoleConstants.FacilityStaff]);

        Assert.Equal("failed", inspResult.Status);
        Assert.Equal("unsafe", inspResult.OverallCondition);
        Assert.True(inspResult.UnitReleasedDueToFailure);

        var unit = await context.storage_units.FindAsync(101L);
        Assert.NotNull(unit);
        Assert.Equal("maintenance", unit.physical_status);
    }

    [Fact]
    public async Task Inspection_ItemConditionUnsafe_ThrowsBadRequestException()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);
        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);

        // Condition 'unsafe' on item level must be rejected (only allowed via IsUnsafe flag)
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.PerformInspectionAsync(1, new UnitInspectionRequest
            {
                CurrentAllocationId = alloc.AllocationId,
                Items = new List<InspectionItemRequest>
                {
                    new() { ItemName = "cửa", Condition = "unsafe" },
                    new() { ItemName = "khóa", Condition = "good" },
                    new() { ItemName = "đèn", Condition = "good" },
                    new() { ItemName = "vệ sinh", Condition = "good" }
                }
            }, 20, [RoleConstants.FacilityStaff]));

        Assert.Contains("IsUnsafe", ex.Message);
    }

    [Fact]
    public async Task VerifyIdentity_MismatchedDocument_SavesRejectedVerification_AndThrowsBadRequestException()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context, customerIdNumber: "079201001234");
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Staff enters mismatched document number
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.VerifyIdentityAsync(1, new VerifyIdentityRequest
            {
                VerificationMethod = "government_id",
                DocumentNumber = "079999999999",
                IsConfirmedMatch = true
            }, 20, [RoleConstants.FacilityStaff]));

        Assert.Contains("không khớp", ex.Message);

        // Verification record MUST be saved with result = "rejected"
        var verification = await context.identity_verifications
            .FirstOrDefaultAsync(v => v.reservation_id == 1);
        Assert.NotNull(verification);
        Assert.Equal("rejected", verification.result);

        // Customer profile identity_number must NOT be mutated
        var custProfile = await context.customer_profiles.FindAsync(10L);
        Assert.NotNull(custProfile);
        Assert.Equal("079201001234", custProfile.identity_number);
    }

    [Fact]
    public async Task VerifyIdentity_WhenStaffDeactivatedUnderLock_ThrowsForbiddenException()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Deactivate staff user
        var staffUser = await context.users.FindAsync(20L);
        Assert.NotNull(staffUser);
        staffUser.status = UserStatusConstants.Disabled;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.VerifyIdentityAsync(1, new VerifyIdentityRequest
            {
                VerificationMethod = "government_id",
                DocumentNumber = "079201001234",
                IsConfirmedMatch = true
            }, 20, [RoleConstants.FacilityStaff]));
    }

    [Fact]
    public async Task ReissuePin_PersistsRealCredentialIdInAuditLog_AndUsesActualStaffRole()
    {
        var (service, context, _, _) = SetupService(Guid.NewGuid().ToString());
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        // Complete check-in first
        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);
        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);
        var insp = await service.PerformInspectionAsync(1, new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "good" },
                new() { ItemName = "khóa", Condition = "good" },
                new() { ItemName = "đèn", Condition = "good" },
                new() { ItemName = "vệ sinh", Condition = "good" }
            }
        }, 20, [RoleConstants.FacilityStaff]);
        var complete = await service.CompleteCheckInAsync(1, new CompleteCheckInRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            CurrentInspectionId = insp.InspectionId,
            CustomerAcknowledged = true
        }, 20, [RoleConstants.FacilityStaff]);

        var credId = complete.CredentialId!.Value;

        // Reissue PIN
        var reissue = await service.ReissuePinAsync(1, new ReissuePinRequest
        {
            CurrentCredentialId = credId,
            DocumentNumber = "079201001234",
            IsConfirmedMatch = true,
            Reason = "Khách hàng quên mã PIN"
        }, 20, [RoleConstants.FacilityStaff]);

        Assert.NotNull(reissue);
        Assert.Equal(credId, reissue.OldCredentialId);
        Assert.True(reissue.NewCredentialId > 0);

        // Verify audit log
        var audit = await context.audit_logs
            .FirstOrDefaultAsync(a => a.action == "REISSUE_PIN");
        Assert.NotNull(audit);
        Assert.Equal(reissue.NewCredentialId.ToString(), audit.entity_id);
        Assert.Equal("facility_staff", audit.actor_role);
        Assert.Contains(reissue.NewCredentialId.ToString(), audit.new_values);
    }

    [Fact]
    public async Task CompleteCheckIn_SupportsAlternativeConfigurationKeys()
    {
        var dbName = Guid.NewGuid().ToString();
        var context = CreateInMemoryContext(dbName);
        var checkInRepo = new CheckInRepository(context);
        var userRepo = new UserRepository(context);
        var scopeService = new FacilityScopeService(context, userRepo);

        // Using CheckIn__PinHmacSecret (double underscore environment key format)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CheckIn__PinHmacSecret"] = "AlternativeKeySecretWithAtLeast32BytesLength!"
            })
            .Build();

        var service = new CheckInService(checkInRepo, scopeService, config);
        await SeedBaselineDataAsync(context);
        await SeedConfirmedReservationAndScheduledAgreementAsync(context);

        await service.VerifyIdentityAsync(1, new VerifyIdentityRequest { VerificationMethod = "government_id", DocumentNumber = "079201001234", IsConfirmedMatch = true }, 20, [RoleConstants.FacilityStaff]);
        var alloc = await service.AssignUnitAsync(1, new AssignUnitRequest { StorageUnitId = 101 }, 20, [RoleConstants.FacilityStaff]);
        var insp = await service.PerformInspectionAsync(1, new UnitInspectionRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            Items = new List<InspectionItemRequest>
            {
                new() { ItemName = "cửa", Condition = "good" },
                new() { ItemName = "khóa", Condition = "good" },
                new() { ItemName = "đèn", Condition = "good" },
                new() { ItemName = "vệ sinh", Condition = "good" }
            }
        }, 20, [RoleConstants.FacilityStaff]);

        var complete = await service.CompleteCheckInAsync(1, new CompleteCheckInRequest
        {
            CurrentAllocationId = alloc.AllocationId,
            CurrentInspectionId = insp.InspectionId,
            CustomerAcknowledged = true
        }, 20, [RoleConstants.FacilityStaff]);

        Assert.NotNull(complete.Pin);
        Assert.Equal(6, complete.Pin.Length);
        Assert.False(complete.IsAlreadyCompleted);
    }
}
