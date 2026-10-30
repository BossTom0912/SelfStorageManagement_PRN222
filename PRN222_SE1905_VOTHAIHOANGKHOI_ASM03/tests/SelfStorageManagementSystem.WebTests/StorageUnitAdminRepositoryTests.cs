using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Exceptions;
using SelfStorageManagementSystem.DataAccess.Models;
using SelfStorageManagementSystem.DataAccess.Repositories.Implementations;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

public class StorageUnitAdminRepositoryTests
{
    private SelfStorageDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new SelfStorageDbContext(options);
    }

    private async Task SeedBasicFacilityAndTypeAsync(SelfStorageDbContext context)
    {
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
        var inactiveFac = new facility
        {
            id = 30,
            code = "FAC-INACTIVE",
            name = "Cơ sở Đóng Cửa",
            address_line = "789 Đóng Cửa",
            district = "Ba Đình",
            city = "Hà Nội",
            status = "closed",
            timezone = "Asia/Ho_Chi_Minh"
        };

        var type1 = new unit_type
        {
            id = 100,
            code = "TYPE-S",
            name = "Kho Mini S",
            width_m = 1.5m,
            length_m = 2.0m,
            height_m = 2.5m,
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
            is_active = true
        };
        var inactiveType = new unit_type
        {
            id = 300,
            code = "TYPE-INACTIVE",
            name = "Kho Ngừng Kinh Doanh",
            width_m = 1.0m,
            length_m = 1.0m,
            height_m = 2.0m,
            is_active = false
        };

        var area1 = new facility_area
        {
            id = 1000,
            facility_id = 10,
            code = "AREA-T1",
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

        context.facilities.AddRange(fac1, fac2, inactiveFac);
        context.unit_types.AddRange(type1, type2, inactiveType);
        context.facility_areas.AddRange(area1, area2);
        await context.SaveChangesAsync();
    }

    // =========================================================================
    // 1. Query & SQL Pagination Tests
    // =========================================================================

    [Fact]
    public async Task GetPagedStorageUnits_ShouldFilterAndPaginateCorrectly()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        // Seed 15 storage units for facility 10
        for (int i = 1; i <= 15; i++)
        {
            context.storage_units.Add(new storage_unit
            {
                id = i,
                facility_id = 10,
                unit_type_id = i <= 10 ? 100 : 200,
                area_id = 1000,
                unit_code = $"U-{i:D3}",
                physical_status = i % 2 == 0 ? "available" : "occupied",
                is_listed = true,
                floor_label = "Tầng 1",
                notes = i == 5 ? "Gần cửa sổ thoát hiểm" : null,
                created_at = DateTimeOffset.UtcNow.AddDays(-i),
                updated_at = DateTimeOffset.UtcNow.AddDays(-i)
            });
        }
        await context.SaveChangesAsync();

        var repo = new StorageUnitAdminRepository(context);

        // Page 1 with pageSize 5
        var paged1 = await repo.GetPagedStorageUnitsAsync(new StorageUnitAdminQueryParameters
        {
            FacilityId = 10,
            PageNumber = 1,
            PageSize = 5
        });

        Assert.Equal(15, paged1.TotalCount);
        Assert.Equal(5, paged1.Items.Count);
        Assert.Equal("U-001", paged1.Items[0].unit_code);

        // Page 2 with pageSize 5
        var paged2 = await repo.GetPagedStorageUnitsAsync(new StorageUnitAdminQueryParameters
        {
            FacilityId = 10,
            PageNumber = 2,
            PageSize = 5
        });

        Assert.Equal(15, paged2.TotalCount);
        Assert.Equal(5, paged2.Items.Count);
        Assert.Equal("U-006", paged2.Items[0].unit_code);

        // Filter by physical_status = 'available'
        var availableOnly = await repo.GetPagedStorageUnitsAsync(new StorageUnitAdminQueryParameters
        {
            FacilityId = 10,
            PhysicalStatus = "available",
            PageNumber = 1,
            PageSize = 20
        });

        Assert.Equal(7, availableOnly.TotalCount); // 2, 4, 6, 8, 10, 12, 14
        Assert.All(availableOnly.Items, u => Assert.Equal("available", u.physical_status));

        // Filter by searchTerm matching notes
        var searchNotes = await repo.GetPagedStorageUnitsAsync(new StorageUnitAdminQueryParameters
        {
            FacilityId = 10,
            SearchTerm = "thoát hiểm"
        });

        Assert.Equal(1, searchNotes.TotalCount);
        Assert.Equal("U-005", searchNotes.Items[0].unit_code);
    }

    // =========================================================================
    // 2. UnitCode Uniqueness & Lookups
    // =========================================================================

    [Fact]
    public async Task UnitCodeExistsInFacility_ShouldBeFacilitySpecific()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        context.storage_units.Add(new storage_unit
        {
            id = 1,
            facility_id = 10,
            unit_type_id = 100,
            unit_code = "A-101",
            physical_status = "available",
            is_listed = true,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var repo = new StorageUnitAdminRepository(context);

        // Exists in facility 10
        Assert.True(await repo.UnitCodeExistsInFacilityAsync(10, "A-101"));
        Assert.True(await repo.UnitCodeExistsInFacilityAsync(10, " a-101 ")); // case & whitespace trim

        // Excluded by unit ID (e.g. self-check during update)
        Assert.False(await repo.UnitCodeExistsInFacilityAsync(10, "A-101", excludeUnitId: 1));

        // Does NOT exist in facility 20
        Assert.False(await repo.UnitCodeExistsInFacilityAsync(20, "A-101"));
    }

    [Fact]
    public async Task ValidateScopeReferences_ShouldDetectMismatches()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        var repo = new StorageUnitAdminRepository(context);

        // 1. Valid scope (Facility 10, Type 100, Area 1000)
        var valid = await repo.ValidateScopeReferencesAsync(10, 100, 1000);
        Assert.True(valid.IsValid);
        Assert.Null(valid.FailureReason);

        // 2. Area belongs to facility 20, but specified for facility 10 -> trigger 51020 violation prevention
        var mismatch = await repo.ValidateScopeReferencesAsync(10, 100, 2000);
        Assert.False(mismatch.IsValid);
        Assert.Equal("AREA_FACILITY_MISMATCH", mismatch.FailureReason);

        // 3. Inactive facility
        var inactiveFac = await repo.ValidateScopeReferencesAsync(30, 100, null);
        Assert.False(inactiveFac.IsValid);
        Assert.Equal("FACILITY_INACTIVE", inactiveFac.FailureReason);

        // 4. Inactive unit type
        var inactiveType = await repo.ValidateScopeReferencesAsync(10, 300, null);
        Assert.False(inactiveType.IsValid);
        Assert.Equal("UNIT_TYPE_INACTIVE", inactiveType.FailureReason);
    }

    // =========================================================================
    // 3. Reference Summary Tests
    // =========================================================================

    [Fact]
    public async Task GetStorageUnitReferenceSummary_ShouldCountAllForeignKeys()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        var unit = new storage_unit
        {
            id = 1,
            facility_id = 10,
            unit_type_id = 100,
            unit_code = "U-REF-TEST",
            physical_status = "available",
            is_listed = true,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        context.storage_units.Add(unit);

        // Add 1 allocation
        context.unit_allocations.Add(new unit_allocation
        {
            id = 1,
            storage_unit_id = 1,
            status = "active",
            allocation_start_date = DateOnly.FromDateTime(DateTime.UtcNow),
            allocation_end_date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            allocation_kind = "regular"
        });

        // Add 1 inspection
        context.inspections.Add(new inspection
        {
            id = 1,
            facility_id = 10,
            storage_unit_id = 1,
            inspected_by = 999,
            inspection_type = "move_in",
            status = "draft"
        });

        // Add 1 status history
        context.unit_status_histories.Add(new unit_status_history
        {
            id = 1,
            storage_unit_id = 1,
            old_status = "available",
            new_status = "reserved",
            changed_at = DateTimeOffset.UtcNow
        });

        // Add 1 map position
        context.unit_map_positions.Add(new unit_map_position
        {
            unit_id = 1,
            area_id = 1000,
            x = 10.5m,
            y = 20.5m,
            metadata = "{}"
        });

        await context.SaveChangesAsync();

        var repo = new StorageUnitAdminRepository(context);
        var summary = await repo.GetStorageUnitReferenceSummaryAsync(1);

        Assert.Equal(1, summary.AllocationsCount);
        Assert.Equal(1, summary.ActiveAllocationsCount);
        Assert.Equal(1, summary.InspectionsCount);
        Assert.Equal(1, summary.StatusHistoryCount);
        Assert.True(summary.HasMapPosition);
        Assert.True(summary.HasAnyReferences);
        Assert.False(summary.CanHardDelete);
    }

    // =========================================================================
    // 4. Capacity Impact (Holds & Confirmed)
    // =========================================================================

    [Fact]
    public async Task GetCapacityImpact_ShouldDistinguishUnassignedHoldsAndConfirmed()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // 3 available units of Type 100 in Facility 10
        for (int i = 1; i <= 3; i++)
        {
            context.storage_units.Add(new storage_unit
            {
                id = i,
                facility_id = 10,
                unit_type_id = 100,
                unit_code = $"CAP-{i}",
                physical_status = "available",
                is_listed = true,
                created_at = now,
                updated_at = now
            });
        }

        // 1 unassigned active hold (pending, hold_until in future)
        context.reservations.Add(new reservation
        {
            id = 101,
            facility_id = 10,
            unit_type_id = 100,
            customer_id = 501,
            reservation_code = "RES-HOLD-1",
            status = "pending",
            hold_until = now.AddMinutes(12),
            start_date = today,
            end_date = today.AddMonths(1)
        });

        // 1 unassigned confirmed reservation (deposit paid, awaiting counter check-in)
        context.reservations.Add(new reservation
        {
            id = 102,
            facility_id = 10,
            unit_type_id = 100,
            customer_id = 502,
            reservation_code = "RES-CONF-1",
            status = "confirmed",
            start_date = today,
            end_date = today.AddMonths(1)
        });

        // 1 expired hold (should NOT count)
        context.reservations.Add(new reservation
        {
            id = 103,
            facility_id = 10,
            unit_type_id = 100,
            customer_id = 503,
            reservation_code = "RES-EXP-1",
            status = "pending",
            hold_until = now.AddMinutes(-5),
            start_date = today,
            end_date = today.AddMonths(1)
        });

        await context.SaveChangesAsync();

        var repo = new StorageUnitAdminRepository(context);
        var impact = await repo.GetCapacityImpactAsync(10, 100, targetUnitId: 1, nowUtc: now);

        Assert.Equal(3, impact.TotalUnitsCount);
        Assert.Equal(3, impact.CandidateAvailableUnitsCount);
        Assert.Equal(1, impact.UnassignedHoldsCount);
        Assert.Equal(1, impact.UnassignedConfirmedCount);
        Assert.Equal(2, impact.TotalUnassignedActiveReservationsCount);
        Assert.Equal(1, impact.NetAvailableCapacity); // 3 candidate - 2 unassigned = 1

        Assert.True(impact.IsTargetUnitCurrentlyCandidate);
        Assert.Equal(2, impact.CandidateUnitsAfterDelete); // 3 - 1 = 2
        Assert.Equal(0, impact.NetCapacityAfterDelete); // 2 - 2 = 0
        Assert.False(impact.WouldBreachCapacityIfDeleted); // remaining 0 is not negative
    }

    // =========================================================================
    // 5. Atomic Mutations Under Capacity Lock
    // =========================================================================

    [Fact]
    public async Task CreateStorageUnitWithLock_ShouldSucceedAndEnforceUniqueness()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        var repo = new StorageUnitAdminRepository(context);

        var newUnit = new storage_unit
        {
            facility_id = 10,
            unit_type_id = 100,
            area_id = 1000,
            unit_code = "SU-CREATE-01",
            floor_label = "Tầng 1",
            is_listed = true
        };

        var created = await repo.CreateStorageUnitWithLockAsync(newUnit);

        Assert.True(created.id > 0);
        Assert.Equal("available", created.physical_status);
        Assert.NotEqual(default, created.created_at);
        Assert.NotEqual(default, created.updated_at);

        // Attempting to create duplicate unit_code in same facility must throw StorageUnitUniqueConstraintException
        var duplicateUnit = new storage_unit
        {
            facility_id = 10,
            unit_type_id = 100,
            unit_code = "SU-CREATE-01"
        };

        await Assert.ThrowsAsync<StorageUnitUniqueConstraintException>(
            () => repo.CreateStorageUnitWithLockAsync(duplicateUnit));
    }

    [Fact]
    public async Task UpdateStorageUnitWithLock_ShouldEnforceConcurrencyAndApplyToLoadedEntity()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        var initialTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var unit = new storage_unit
        {
            id = 50,
            facility_id = 10,
            unit_type_id = 100,
            area_id = 1000,
            unit_code = "SU-UPDATE-01",
            physical_status = "available",
            is_listed = true,
            notes = "Initial Note",
            created_at = initialTime,
            updated_at = initialTime
        };
        context.storage_units.Add(unit);
        await context.SaveChangesAsync();

        var repo = new StorageUnitAdminRepository(context);

        // 1. Stale concurrency token must throw StorageUnitConcurrencyException
        var staleTimestamp = initialTime.AddSeconds(-30);
        await Assert.ThrowsAsync<StorageUnitConcurrencyException>(() =>
            repo.UpdateStorageUnitWithLockAsync(50, staleTimestamp, u => u.notes = "Stale Edit"));

        // 2. Matching concurrency token must update entity and advance updated_at
        var updated = await repo.UpdateStorageUnitWithLockAsync(50, initialTime, u =>
        {
            u.notes = "Updated Note";
            u.physical_status = "maintenance";
        });

        Assert.Equal("Updated Note", updated.notes);
        Assert.Equal("maintenance", updated.physical_status);
        Assert.True(updated.updated_at > initialTime);
    }

    [Fact]
    public async Task DeleteStorageUnitWithLock_ShouldRespectPolicyAndFkProtection()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        var time = DateTimeOffset.UtcNow;
        var unit = new storage_unit
        {
            id = 99,
            facility_id = 10,
            unit_type_id = 100,
            unit_code = "SU-DEL-01",
            physical_status = "available",
            is_listed = true,
            created_at = time,
            updated_at = time
        };
        context.storage_units.Add(unit);
        await context.SaveChangesAsync();

        var repo = new StorageUnitAdminRepository(context);

        // 1. Policy rejects deletion
        var policyRejected = await repo.DeleteStorageUnitWithLockAsync(
            99,
            time,
            evaluatePolicy: ctx => Task.FromResult(false));

        Assert.False(policyRejected.Success);
        Assert.Equal("DELETE_POLICY_REJECTED", policyRejected.Message);
        Assert.NotNull(await context.storage_units.FindAsync(99L)); // Still exists

        // 2. Add an allocation FK reference -> When deleting unit with dependent records, retain row and set is_listed=false atomically
        context.unit_allocations.Add(new unit_allocation
        {
            id = 991,
            storage_unit_id = 99,
            status = "active",
            allocation_start_date = DateOnly.FromDateTime(DateTime.UtcNow),
            allocation_end_date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            allocation_kind = "regular"
        });
        await context.SaveChangesAsync();

        var unlistResult = await repo.DeleteStorageUnitWithLockAsync(
            99,
            time,
            evaluatePolicy: ctx => Task.FromResult(true));

        Assert.True(unlistResult.Success);
        Assert.True(unlistResult.IsUnlistedOnly);
        Assert.Equal("STORAGE_UNIT_UNLISTED", unlistResult.Message);

        var retainedUnit = await context.storage_units.FindAsync(99L);
        Assert.NotNull(retainedUnit);
        Assert.False(retainedUnit.is_listed);
        Assert.True(retainedUnit.updated_at > time);

        // Concurrency check: attempting delete with old stale time now throws StorageUnitConcurrencyException
        await Assert.ThrowsAsync<StorageUnitConcurrencyException>(() =>
            repo.DeleteStorageUnitWithLockAsync(99, time, evaluatePolicy: ctx => Task.FromResult(true)));

        // 3. Remove FK reference -> Clean unit preserves hard delete policy
        var alloc = await context.unit_allocations.FindAsync(991L);
        Assert.NotNull(alloc);
        context.unit_allocations.Remove(alloc);
        await context.SaveChangesAsync();

        var successResult = await repo.DeleteStorageUnitWithLockAsync(
            99,
            retainedUnit.updated_at,
            evaluatePolicy: ctx => Task.FromResult(true));

        Assert.True(successResult.Success);
        Assert.False(successResult.IsUnlistedOnly);
        Assert.Equal("STORAGE_UNIT_DELETED", successResult.Message);
        Assert.Null(await context.storage_units.FindAsync(99L)); // Gone!
    }

    // =========================================================================
    // 6. Codex Review Fixes Verification Tests
    // =========================================================================

    [Fact]
    public async Task GetLookupUnitTypes_FacilityScoped_StrictRatesOnly()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        // Facility 10 has rate for Type 100 only
        context.facility_rates.Add(new facility_rate
        {
            id = 1,
            facility_id = 10,
            unit_type_id = 100,
            monthly_rate = 500000m,
            deposit_amount = 500000m,
            booking_fee = 50000m,
            valid_from = new DateOnly(2026, 1, 1),
            created_at = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        var repo = new StorageUnitAdminRepository(context);

        // 1. Facility 10: only Type 100 has rate -> returns exactly [Type 100], excludes Type 200 and inactive Type 300
        var fac10Types = await repo.GetLookupUnitTypesAsync(10);
        Assert.Single(fac10Types);
        Assert.Equal(100, fac10Types[0].id);

        // 2. Facility 20: 0 rates configured -> returns EMPTY list [], does NOT fall back to all active types
        var fac20Types = await repo.GetLookupUnitTypesAsync(20);
        Assert.Empty(fac20Types);

        // 3. facilityId == null: returns all active types (Type 100 & Type 200), excludes inactive Type 300
        var globalTypes = await repo.GetLookupUnitTypesAsync(null);
        Assert.Equal(2, globalTypes.Count);
        Assert.Contains(globalTypes, t => t.id == 100);
        Assert.Contains(globalTypes, t => t.id == 200);
        Assert.DoesNotContain(globalTypes, t => t.id == 300);
    }

    [Fact]
    public async Task GetPagedStorageUnits_DeterministicSortingAndPaginationWithTies()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        await SeedBasicFacilityAndTypeAsync(context);

        // Seed 12 units with EXACT same created_at and physical_status to induce sorting ties
        var fixedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (int i = 1; i <= 12; i++)
        {
            context.storage_units.Add(new storage_unit
            {
                id = i,
                facility_id = 10,
                unit_type_id = 100,
                area_id = 1000,
                unit_code = $"TIE-{i:D2}",
                physical_status = "available",
                is_listed = true,
                floor_label = "Tầng 1",
                created_at = fixedTime,
                updated_at = fixedTime
            });
        }
        await context.SaveChangesAsync();

        var repo = new StorageUnitAdminRepository(context);

        // Query 3 pages of size 4 sorted by created_at desc (which are all tied)
        var p1 = await repo.GetPagedStorageUnitsAsync(new StorageUnitAdminQueryParameters
        {
            FacilityId = 10,
            SortBy = "created_at",
            SortDescending = true,
            PageNumber = 1,
            PageSize = 4
        });

        var p2 = await repo.GetPagedStorageUnitsAsync(new StorageUnitAdminQueryParameters
        {
            FacilityId = 10,
            SortBy = "created_at",
            SortDescending = true,
            PageNumber = 2,
            PageSize = 4
        });

        var p3 = await repo.GetPagedStorageUnitsAsync(new StorageUnitAdminQueryParameters
        {
            FacilityId = 10,
            SortBy = "created_at",
            SortDescending = true,
            PageNumber = 3,
            PageSize = 4
        });

        Assert.Equal(12, p1.TotalCount);
        Assert.Equal(4, p1.Items.Count);
        Assert.Equal(4, p2.Items.Count);
        Assert.Equal(4, p3.Items.Count);

        var allFetchedIds = p1.Items.Select(x => x.id)
            .Concat(p2.Items.Select(x => x.id))
            .Concat(p3.Items.Select(x => x.id))
            .ToList();

        // Must contain exactly 12 distinct IDs without skips or duplicates
        Assert.Equal(12, allFetchedIds.Distinct().Count());
        Assert.Equal(12, allFetchedIds.Count);
    }

    [Fact]
    public async Task UpdateAndReload_AcrossContexts_DetectsStaleReadAndReloadsFreshEntity()
    {
        var dbName = Guid.NewGuid().ToString();
        var initialTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        // Seed unit in initial context
        using (var seedContext = CreateInMemoryContext(dbName))
        {
            await SeedBasicFacilityAndTypeAsync(seedContext);
            seedContext.storage_units.Add(new storage_unit
            {
                id = 55,
                facility_id = 10,
                unit_type_id = 100,
                area_id = 1000,
                unit_code = "SU-STALE-55",
                physical_status = "available",
                is_listed = true,
                notes = "Original Note",
                created_at = initialTime,
                updated_at = initialTime
            });
            await seedContext.SaveChangesAsync();
        }

        // Two independent DbContexts simulating concurrent user sessions
        using var context1 = CreateInMemoryContext(dbName);
        using var context2 = CreateInMemoryContext(dbName);

        var repo1 = new StorageUnitAdminRepository(context1);
        var repo2 = new StorageUnitAdminRepository(context2);

        // Context 1 tracks unit 55 in its ChangeTracker
        var trackedIn1 = await context1.storage_units.FindAsync(55L);
        Assert.NotNull(trackedIn1);
        Assert.Equal("Original Note", trackedIn1.notes);

        // Context 2 updates unit 55 under lock
        var updated2 = await repo2.UpdateStorageUnitWithLockAsync(55, initialTime, u =>
        {
            u.notes = "Updated by User 2";
        });
        Assert.True(updated2.updated_at > initialTime);

        // Context 1's in-memory entity is now stale. Attempting update with initialTime MUST throw StorageUnitConcurrencyException
        await Assert.ThrowsAsync<StorageUnitConcurrencyException>(() =>
            repo1.UpdateStorageUnitWithLockAsync(55, initialTime, u => u.notes = "Stale Edit by User 1"));

        // Context 1 attempting delete with stale timestamp MUST also throw StorageUnitConcurrencyException
        await Assert.ThrowsAsync<StorageUnitConcurrencyException>(() =>
            repo1.DeleteStorageUnitWithLockAsync(55, initialTime, ctx => Task.FromResult(true)));

        // Context 1 updating with the fresh timestamp:
        // Repository reloads trackedEntry via ReloadAsync(), updates properties, and succeeds
        var updated1 = await repo1.UpdateStorageUnitWithLockAsync(55, updated2.updated_at, u =>
        {
            u.notes = "Fresh Edit by User 1";
        });

        Assert.Equal("Fresh Edit by User 1", updated1.notes);
        Assert.True(updated1.updated_at > updated2.updated_at);

        // Verify tracked instance in context1 was updated
        Assert.Equal("Fresh Edit by User 1", trackedIn1.notes);
    }

    [Fact]
    public async Task TwoResourceLockOrdering_CrossTypeTransfer_ExecutesDeterministicallyWithoutDeadlock()
    {
        var dbName = Guid.NewGuid().ToString();
        var time = DateTimeOffset.UtcNow;

        using (var seedContext = CreateInMemoryContext(dbName))
        {
            await SeedBasicFacilityAndTypeAsync(seedContext);

            // Unit 1: in Fac 10, Type 100
            seedContext.storage_units.Add(new storage_unit
            {
                id = 71,
                facility_id = 10,
                unit_type_id = 100,
                area_id = 1000,
                unit_code = "SU-XFER-1",
                physical_status = "available",
                is_listed = true,
                created_at = time,
                updated_at = time
            });

            // Unit 2: in Fac 10, Type 200
            seedContext.storage_units.Add(new storage_unit
            {
                id = 72,
                facility_id = 10,
                unit_type_id = 200,
                area_id = 1000,
                unit_code = "SU-XFER-2",
                physical_status = "available",
                is_listed = true,
                created_at = time,
                updated_at = time
            });

            await seedContext.SaveChangesAsync();
        }

        // Requirement 6: Use distinct DbContext instances for parallel tasks
        using var context1 = CreateInMemoryContext(dbName);
        using var context2 = CreateInMemoryContext(dbName);

        var repo1 = new StorageUnitAdminRepository(context1);
        var repo2 = new StorageUnitAdminRepository(context2);

        // Task 1 transfers Unit 71 from Type 100 to Type 200 (Source lock: Fac10/Type100, Target lock: Fac10/Type200)
        var task1 = repo1.UpdateStorageUnitWithLockAsync(
            71,
            time,
            u =>
            {
                u.unit_type_id = 200;
            },
            targetFacilityId: 10,
            targetUnitTypeId: 200);

        // Task 2 transfers Unit 72 from Type 200 to Type 100 (Source lock: Fac10/Type200, Target lock: Fac10/Type100)
        var task2 = repo2.UpdateStorageUnitWithLockAsync(
            72,
            time,
            u =>
            {
                u.unit_type_id = 100;
            },
            targetFacilityId: 10,
            targetUnitTypeId: 100);

        // Running both concurrently must succeed without deadlock
        await Task.WhenAll(task1, task2);

        var res1 = await task1;
        var res2 = await task2;

        Assert.Equal(10, res1.facility_id);
        Assert.Equal(200, res1.unit_type_id);

        Assert.Equal(10, res2.facility_id);
        Assert.Equal(100, res2.unit_type_id);
    }

    [Fact]
    public void CapacityLockKey_MatchesReservationConvention()
    {
        var key = StorageUnitAdminRepository.GetCapacityLockKey(10, 100);
        Assert.Equal("SS_Facility_10_UnitType_100_Capacity", key);

        var key2 = StorageUnitAdminRepository.GetCapacityLockKey(20, 200);
        Assert.Equal("SS_Facility_20_UnitType_200_Capacity", key2);
    }

    // =========================================================================
    // 7. P1 & P2 Regression Verification Tests (Codex Review Fixes)
    // =========================================================================

    [Fact]
    public async Task UpdateStorageUnitWithLock_ApplyUpdatesRunsExactlyOnce()
    {
        var dbName = Guid.NewGuid().ToString();
        var time = DateTimeOffset.UtcNow;

        using (var seedContext = CreateInMemoryContext(dbName))
        {
            await SeedBasicFacilityAndTypeAsync(seedContext);
            seedContext.storage_units.Add(new storage_unit
            {
                id = 81,
                facility_id = 10,
                unit_type_id = 100,
                area_id = 1000,
                unit_code = "SU-ONCE-01",
                physical_status = "available",
                is_listed = true,
                created_at = time,
                updated_at = time
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = CreateInMemoryContext(dbName);
        var repo = new StorageUnitAdminRepository(context);

        int applyUpdatesInvocationCount = 0;
        var result = await repo.UpdateStorageUnitWithLockAsync(81, time, u =>
        {
            applyUpdatesInvocationCount++;
            u.notes = $"Updated_CallCount_{applyUpdatesInvocationCount}";
        });

        // P1: Assert applyUpdates was executed EXACTLY ONCE
        Assert.Equal(1, applyUpdatesInvocationCount);
        Assert.Equal("Updated_CallCount_1", result.notes);

        // Verify database persistence
        using var verifyContext = CreateInMemoryContext(dbName);
        var inDb = await verifyContext.storage_units.FindAsync(81L);
        Assert.NotNull(inDb);
        Assert.Equal("Updated_CallCount_1", inDb.notes);
    }

    [Fact]
    public async Task UpdateStorageUnitWithLock_NonDeterministicCallback_UsesSingleStagedResult()
    {
        var dbName = Guid.NewGuid().ToString();
        var time = DateTimeOffset.UtcNow;

        using (var seedContext = CreateInMemoryContext(dbName))
        {
            await SeedBasicFacilityAndTypeAsync(seedContext);
            seedContext.storage_units.Add(new storage_unit
            {
                id = 82,
                facility_id = 10,
                unit_type_id = 100,
                area_id = 1000,
                unit_code = "SU-NON-DET-01",
                physical_status = "available",
                is_listed = true,
                created_at = time,
                updated_at = time
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = CreateInMemoryContext(dbName);
        var repo = new StorageUnitAdminRepository(context);

        int runCounter = 0;
        var result = await repo.UpdateStorageUnitWithLockAsync(82, time, u =>
        {
            runCounter++;
            // If called twice, notes would change on the second invocation
            u.notes = runCounter == 1 ? "Deterministic_Staged_Value" : "Polluted_Second_Run_Value";
        });

        // Must run exactly once and persist the staged value
        Assert.Equal(1, runCounter);
        Assert.Equal("Deterministic_Staged_Value", result.notes);

        using var verifyContext = CreateInMemoryContext(dbName);
        var inDb = await verifyContext.storage_units.FindAsync(82L);
        Assert.NotNull(inDb);
        Assert.Equal("Deterministic_Staged_Value", inDb.notes);
    }

    [Fact]
    public async Task UpdateStorageUnitWithLock_TargetIdMismatch_RejectedBeforeSaveChanges()
    {
        var dbName = Guid.NewGuid().ToString();
        var time = DateTimeOffset.UtcNow;

        using (var seedContext = CreateInMemoryContext(dbName))
        {
            await SeedBasicFacilityAndTypeAsync(seedContext);
            seedContext.storage_units.Add(new storage_unit
            {
                id = 83,
                facility_id = 10,
                unit_type_id = 100,
                area_id = 1000,
                unit_code = "SU-MISMATCH-01",
                physical_status = "available",
                is_listed = true,
                notes = "Original Notes",
                created_at = time,
                updated_at = time
            });
            await seedContext.SaveChangesAsync();
        }

        using var context = CreateInMemoryContext(dbName);
        var repo = new StorageUnitAdminRepository(context);

        // 1. TargetUnitTypeId mismatch: callback mutates to 200, caller claims target is 300
        var exType = await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.UpdateStorageUnitWithLockAsync(
                83,
                time,
                u =>
                {
                    u.unit_type_id = 200;
                    u.notes = "Mutated Attempt";
                },
                targetFacilityId: 10,
                targetUnitTypeId: 300));

        Assert.Contains("targetUnitTypeId", exType.Message);

        // 2. TargetFacilityId mismatch: callback mutates to 20, caller claims target is 99
        var exFac = await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.UpdateStorageUnitWithLockAsync(
                83,
                time,
                u =>
                {
                    u.facility_id = 20;
                    u.notes = "Mutated Attempt";
                },
                targetFacilityId: 99,
                targetUnitTypeId: 100));

        Assert.Contains("targetFacilityId", exFac.Message);

        // Verify that database was NOT modified at all
        using var verifyContext = CreateInMemoryContext(dbName);
        var inDb = await verifyContext.storage_units.FindAsync(83L);
        Assert.NotNull(inDb);
        Assert.Equal(10, inDb.facility_id);
        Assert.Equal(100, inDb.unit_type_id);
        Assert.Equal("Original Notes", inDb.notes);
        Assert.Equal(time, inDb.updated_at);
    }

    [Fact]
    public void MutationPlan_ComputesDeterministicLocks_CoveringSourceAndTarget()
    {
        // 1. Changing Facility: Fac 10 -> Fac 20, Type 100 -> Type 100
        var facChangeKeys = StorageUnitAdminRepository.DetermineCapacityLockKeys(10, 100, 20, 100);
        Assert.Equal(2, facChangeKeys.Count);
        Assert.Equal("SS_Facility_10_UnitType_100_Capacity", facChangeKeys[0]);
        Assert.Equal("SS_Facility_20_UnitType_100_Capacity", facChangeKeys[1]);

        // 2. Changing UnitType: Fac 10 -> Fac 10, Type 100 -> Type 200
        var typeChangeKeys = StorageUnitAdminRepository.DetermineCapacityLockKeys(10, 100, 10, 200);
        Assert.Equal(2, typeChangeKeys.Count);
        Assert.Equal("SS_Facility_10_UnitType_100_Capacity", typeChangeKeys[0]);
        Assert.Equal("SS_Facility_10_UnitType_200_Capacity", typeChangeKeys[1]);

        // 3. Changing Both Facility and UnitType: Fac 10/Type 100 -> Fac 20/Type 200
        var bothChangeKeys = StorageUnitAdminRepository.DetermineCapacityLockKeys(10, 100, 20, 200);
        Assert.Equal(2, bothChangeKeys.Count);
        Assert.Equal("SS_Facility_10_UnitType_100_Capacity", bothChangeKeys[0]);
        Assert.Equal("SS_Facility_20_UnitType_200_Capacity", bothChangeKeys[1]);

        // 4. Same Facility and UnitType (No move): Fac 10/Type 100 -> Fac 10/Type 100
        var sameKeys = StorageUnitAdminRepository.DetermineCapacityLockKeys(10, 100, 10, 100);
        Assert.Single(sameKeys);
        Assert.Equal("SS_Facility_10_UnitType_100_Capacity", sameKeys[0]);

        // 5. Verify Ordinal sorting regardless of source/target order
        var reversedKeys = StorageUnitAdminRepository.DetermineCapacityLockKeys(20, 200, 10, 100);
        Assert.Equal(2, reversedKeys.Count);
        Assert.Equal("SS_Facility_10_UnitType_100_Capacity", reversedKeys[0]);
        Assert.Equal("SS_Facility_20_UnitType_200_Capacity", reversedKeys[1]);
    }

    [Fact]
    public async Task Cancellation_DuringMultiLockAcquisition_ReleasesPartiallyAcquiredSemaphoresWithoutLeak()
    {
        var dbName = Guid.NewGuid().ToString();
        var time = DateTimeOffset.UtcNow;

        using (var seedContext = CreateInMemoryContext(dbName))
        {
            await SeedBasicFacilityAndTypeAsync(seedContext);
            var fac998 = new facility
            {
                id = 998,
                code = "FAC-998",
                name = "Cơ sở Test Isolation",
                address_line = "998 Đường Test",
                district = "Isolation",
                city = "Hà Nội",
                status = "active",
                timezone = "Asia/Ho_Chi_Minh"
            };
            var type901 = new unit_type { id = 901, code = "TYPE-901", name = "Test Type 1", width_m = 1, length_m = 1, height_m = 1, is_active = true };
            var type902 = new unit_type { id = 902, code = "TYPE-902", name = "Test Type 2", width_m = 2, length_m = 2, height_m = 2, is_active = true };
            var area9000 = new facility_area { id = 9000, facility_id = 998, code = "AREA-998", name = "Area 998", area_type = "floor", display_order = 1, is_active = true, map_metadata = "{}" };
            seedContext.facilities.Add(fac998);
            seedContext.unit_types.AddRange(type901, type902);
            seedContext.facility_areas.Add(area9000);
            seedContext.storage_units.Add(new storage_unit
            {
                id = 85,
                facility_id = 998,
                unit_type_id = 901,
                area_id = 9000,
                unit_code = "SU-CANCEL-01",
                physical_status = "available",
                is_listed = true,
                created_at = time,
                updated_at = time
            });
            await seedContext.SaveChangesAsync();
        }

        var sourceLockKey = StorageUnitAdminRepository.GetCapacityLockKey(998, 901);
        var targetLockKey = StorageUnitAdminRepository.GetCapacityLockKey(998, 902);

        // Pre-acquire the target lock (Key 2) so that any transfer to Type 902 blocks waiting on Key 2
        var semTarget = StorageUnitAdminRepository.GetInMemorySemaphore(targetLockKey);
        await semTarget.WaitAsync(); // Holds Target lock

        var semSource = StorageUnitAdminRepository.GetInMemorySemaphore(sourceLockKey);
        Assert.Equal(1, semSource.CurrentCount);

        using var cts = new CancellationTokenSource();
        using var context = CreateInMemoryContext(dbName);
        var repo = new StorageUnitAdminRepository(context);

        // Start background update attempting to transfer Unit 85 from Type 901 to Type 902
        // Keys to lock in order: 1) sourceLockKey (998/901), 2) targetLockKey (998/902)
        var updateTask = repo.UpdateStorageUnitWithLockAsync(
            85,
            time,
            u => u.unit_type_id = 902,
            targetFacilityId: 998,
            targetUnitTypeId: 902,
            cancellationToken: cts.Token);

        // Wait briefly for the task to acquire Key 1 and block waiting on Key 2
        await Task.Delay(50);

        // Cancel the operation while it is blocked waiting on Key 2
        cts.Cancel();

        // The task MUST fail with OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updateTask);

        // P2: Verify that Key 1 was NOT leaked and was cleanly released in the finally block!
        Assert.Equal(1, semSource.CurrentCount);

        // A new operation requiring Key 1 MUST succeed without timing out
        using var freshContext = CreateInMemoryContext(dbName);
        var freshRepo = new StorageUnitAdminRepository(freshContext);
        var followUpUpdate = await freshRepo.UpdateStorageUnitWithLockAsync(
            85,
            time,
            u => u.notes = "Success after cancel");

        Assert.Equal("Success after cancel", followUpUpdate.notes);

        // Release the pre-acquired target lock blocker
        semTarget.Release();
        Assert.Equal(1, semTarget.CurrentCount);
    }
}
