using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Exceptions;
using SelfStorageManagementSystem.DataAccess.Models;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

/// <summary>
/// Service implementation for administrative storage unit management.
/// Enforces server-side authentication, role and facility scope authorization, optimistic concurrency,
/// referential integrity checks, and capacity commitment protection under transactional capacity locks.
/// Operates strictly within BLL using typed domain exceptions; does not reference ASP.NET Core or SignalR.
/// </summary>
public class StorageUnitAdminServiceKhoiVTH : IStorageUnitAdminService
{
    private readonly IStorageUnitAdminRepository _storageUnitAdminRepository;
    private readonly IUserRepository _userRepository;
    private readonly IFacilityScopeService _facilityScopeService;
    private readonly TimeProvider _timeProvider;

    public StorageUnitAdminServiceKhoiVTH(
        IStorageUnitAdminRepository storageUnitAdminRepository,
        IUserRepository userRepository,
        IFacilityScopeService facilityScopeService,
        TimeProvider? timeProvider = null)
    {
        _storageUnitAdminRepository = storageUnitAdminRepository ?? throw new ArgumentNullException(nameof(storageUnitAdminRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _facilityScopeService = facilityScopeService ?? throw new ArgumentNullException(nameof(facilityScopeService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PagedResult<StorageUnitAdminDto>> ListAsync(
        StorageUnitFilterRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (_, isSysOrOps, _, _) = await ValidateActorAndRolesAsync(actorUserId, cancellationToken);

        var queryParams = new StorageUnitAdminQueryParametersKhoiVTH
        {
            UnitTypeId = request.UnitTypeId,
            AreaId = request.AreaId,
            PhysicalStatus = request.PhysicalStatus,
            IsListed = request.IsListed,
            SearchTerm = request.SearchTerm,
            SortBy = request.SortBy,
            SortDescending = request.SortDescending,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };

        if (isSysOrOps)
        {
            queryParams.FacilityId = request.FacilityId;
        }
        else
        {
            var accessibleFacilityIds = await _facilityScopeService.GetAccessibleFacilityIdsAsync(actorUserId, cancellationToken);

            if (request.FacilityId.HasValue)
            {
                if (!accessibleFacilityIds.Contains(request.FacilityId.Value))
                {
                    throw new ForbiddenException($"User does not have access to facility ID {request.FacilityId.Value}.");
                }
                queryParams.FacilityId = request.FacilityId.Value;
            }
            else
            {
                if (accessibleFacilityIds.Count == 0)
                {
                    return new PagedResult<StorageUnitAdminDto>(Array.Empty<StorageUnitAdminDto>(), 0, request.PageNumber, request.PageSize);
                }
                queryParams.FacilityIds = accessibleFacilityIds;
            }
        }

        var (items, totalCount) = await _storageUnitAdminRepository.GetPagedStorageUnitsAsync(queryParams, cancellationToken);
        var dtos = items.Select(MapToAdminDto).ToList();

        return new PagedResult<StorageUnitAdminDto>(dtos, totalCount, request.PageNumber, request.PageSize);
    }

    public async Task<StorageUnitAdminDetailDto?> GetByIdAsync(
        long unitId,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        var (_, isSysOrOps, _, _) = await ValidateActorAndRolesAsync(actorUserId, cancellationToken);

        var unit = await _storageUnitAdminRepository.GetStorageUnitByIdAsync(unitId, includeDetails: true, cancellationToken);
        if (unit == null)
        {
            return null;
        }

        if (!isSysOrOps)
        {
            var hasAccess = await _facilityScopeService.HasAccessToFacilityAsync(
                actorUserId, unit.facility_id, RoleConstants.FacilityStaff, cancellationToken);
            if (!hasAccess)
            {
                throw new ForbiddenException($"User does not have access to facility ID {unit.facility_id}.");
            }
        }

        var refSummary = await _storageUnitAdminRepository.GetStorageUnitReferenceSummaryAsync(unitId, cancellationToken);
        return MapToDetailDto(unit, refSummary);
    }

    public async Task<StorageUnitLookupsDto> GetLookupsAsync(
        long actorUserId,
        long? facilityId = null,
        CancellationToken cancellationToken = default)
    {
        var (_, isSysOrOps, _, _) = await ValidateActorAndRolesAsync(actorUserId, cancellationToken);

        var allFacilities = await _storageUnitAdminRepository.GetLookupFacilitiesAsync(cancellationToken);
        IReadOnlyList<facility> accessibleFacilities;

        if (isSysOrOps)
        {
            accessibleFacilities = allFacilities;
        }
        else
        {
            var accessibleIds = await _facilityScopeService.GetAccessibleFacilityIdsAsync(actorUserId, cancellationToken);
            accessibleFacilities = allFacilities.Where(f => accessibleIds.Contains(f.id)).ToList();
        }

        if (facilityId.HasValue)
        {
            if (!accessibleFacilities.Any(f => f.id == facilityId.Value))
            {
                throw new ForbiddenException($"User does not have access to facility ID {facilityId.Value}.");
            }
        }

        var unitTypes = await _storageUnitAdminRepository.GetLookupUnitTypesAsync(facilityId, cancellationToken);

        IReadOnlyList<facility_area> areas = Array.Empty<facility_area>();
        DateOnly? effectiveDateInFacility = null;

        if (facilityId.HasValue)
        {
            var targetFacility = accessibleFacilities.First(f => f.id == facilityId.Value);
            var tz = ResolveTimeZone(targetFacility.timezone);
            var localDateTime = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), tz);
            effectiveDateInFacility = DateOnly.FromDateTime(localDateTime.DateTime);

            areas = await _storageUnitAdminRepository.GetLookupAreasAsync(facilityId.Value, cancellationToken);
        }

        return new StorageUnitLookupsDto
        {
            Facilities = accessibleFacilities.Select(f => new FacilityLookupDto
            {
                Id = f.id,
                Code = f.code,
                Name = f.name,
                AddressLine = f.address_line,
                City = f.city,
                Status = f.status
            }).ToList(),
            UnitTypes = unitTypes.Select(ut =>
            {
                decimal? monthlyRate = null;
                if (facilityId.HasValue && effectiveDateInFacility.HasValue && ut.facility_rates != null && ut.facility_rates.Count > 0)
                {
                    var matchingRate = ut.facility_rates
                        .Where(fr => fr.facility_id == facilityId.Value &&
                                     fr.valid_from <= effectiveDateInFacility.Value &&
                                     (fr.valid_to == null || effectiveDateInFacility.Value < fr.valid_to.Value))
                        .OrderByDescending(fr => fr.valid_from)
                        .ThenByDescending(fr => fr.id)
                        .FirstOrDefault();

                    monthlyRate = matchingRate?.monthly_rate;
                }

                return new StorageUnitTypeLookupDto
                {
                    Id = ut.id,
                    Code = ut.code,
                    Name = ut.name,
                    WidthMeters = ut.width_m,
                    LengthMeters = ut.length_m,
                    HeightMeters = ut.height_m,
                    FloorAreaSqm = ut.area_m2 ?? (ut.width_m * ut.length_m),
                    VolumeCbm = ut.volume_m3 ?? (ut.width_m * ut.length_m * ut.height_m),
                    MonthlyRate = monthlyRate,
                    IsActive = ut.is_active
                };
            }).ToList(),
            Areas = areas.Select(a => new StorageUnitAreaLookupDto
            {
                Id = a.id,
                FacilityId = a.facility_id,
                Code = a.code,
                Name = a.name,
                FloorLabel = a.area_type,
                IsActive = a.is_active
            }).ToList()
        };
    }

    public async Task<StorageUnitAdminDto> CreateAsync(
        CreateStorageUnitRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (_, isSysOrOps, isManager, isStaff) = await ValidateActorAndRolesAsync(actorUserId, cancellationToken);

        // Staff is strictly read-only for storage unit administration
        if (isStaff && !isManager && !isSysOrOps)
        {
            throw new ForbiddenException("Facility staff does not have permission to create storage units.");
        }

        if (request.FacilityId <= 0)
        {
            throw new BadRequestException("FacilityId is required.");
        }

        if (request.UnitTypeId <= 0)
        {
            throw new BadRequestException("UnitTypeId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.UnitCode))
        {
            throw new BadRequestException("UnitCode is required.");
        }

        // Managers can only create units within facilities where they are actively assigned as manager
        if (!isSysOrOps)
        {
            var hasManagerAccess = await _facilityScopeService.HasAccessToFacilityAsync(
                actorUserId, request.FacilityId, RoleConstants.FacilityManager, cancellationToken);
            if (!hasManagerAccess)
            {
                throw new ForbiddenException($"Manager is not assigned as manager at facility ID {request.FacilityId}.");
            }
        }

        // Server strictly assigns available and is_listed = true upon creation
        var newUnit = new storage_unit
        {
            facility_id = request.FacilityId,
            unit_type_id = request.UnitTypeId,
            area_id = request.AreaId,
            unit_code = request.UnitCode.Trim(),
            floor_label = request.FloorLabel?.Trim(),
            zone_label = request.ZoneLabel?.Trim(),
            notes = request.Notes?.Trim(),
            physical_status = "available",
            is_listed = true
        };

        try
        {
            var created = await _storageUnitAdminRepository.CreateStorageUnitWithLockAsync(newUnit, cancellationToken);
            var reloaded = await _storageUnitAdminRepository.GetStorageUnitByIdAsync(created.id, includeDetails: false, cancellationToken);
            return MapToAdminDto(reloaded ?? created);
        }
        catch (StorageUnitUniqueConstraintException ex)
        {
            throw new ConflictException(ex.Message, ex);
        }
        catch (StorageUnitAreaScopeMismatchException ex)
        {
            throw new BadRequestException(ex.Message, ex);
        }
        catch (StorageUnitLockTimeoutException ex)
        {
            throw new ConflictException("A concurrency lock timeout occurred while creating the storage unit. Please retry.", ex);
        }
        catch (StorageUnitPersistenceException ex)
        {
            throw new BadRequestException(ex.Message, ex);
        }
    }

    public async Task<StorageUnitAdminDto> UpdateAsync(
        long unitId,
        UpdateStorageUnitRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (_, isSysOrOps, isManager, isStaff) = await ValidateActorAndRolesAsync(actorUserId, cancellationToken);

        // Staff is strictly read-only
        if (isStaff && !isManager && !isSysOrOps)
        {
            throw new ForbiddenException("Facility staff does not have permission to modify storage units.");
        }

        if (request.ExpectedUpdatedAt == default)
        {
            throw new BadRequestException("ExpectedUpdatedAt is required for update.");
        }

        if (string.IsNullOrWhiteSpace(request.UnitCode))
        {
            throw new BadRequestException("UnitCode is required.");
        }

        var existing = await _storageUnitAdminRepository.GetStorageUnitByIdAsync(unitId, cancellationToken: cancellationToken);
        if (existing == null)
        {
            throw new NotFoundException($"Storage unit with ID {unitId} was not found.");
        }

        // Managers can only edit units in facilities where they are actively assigned as manager
        if (!isSysOrOps)
        {
            var hasManagerAccess = await _facilityScopeService.HasAccessToFacilityAsync(
                actorUserId, existing.facility_id, RoleConstants.FacilityManager, cancellationToken);
            if (!hasManagerAccess)
            {
                throw new ForbiddenException($"Manager is not assigned as manager at facility ID {existing.facility_id}.");
            }
        }

        // Edit allows modifying ONLY UnitCode, AreaId, FloorLabel, ZoneLabel, Notes.
        // FacilityId, UnitTypeId, PhysicalStatus, and IsListed remain strictly unchanged.
        try
        {
            var updated = await _storageUnitAdminRepository.UpdateStorageUnitWithLockAsync(
                unitId,
                request.ExpectedUpdatedAt,
                u =>
                {
                    u.unit_code = request.UnitCode.Trim();
                    u.area_id = request.AreaId;
                    u.floor_label = request.FloorLabel?.Trim();
                    u.zone_label = request.ZoneLabel?.Trim();
                    u.notes = request.Notes?.Trim();
                    // Keep existing.facility_id, existing.unit_type_id, existing.physical_status, existing.is_listed unchanged!
                },
                targetFacilityId: existing.facility_id,
                targetUnitTypeId: existing.unit_type_id,
                cancellationToken);

            var reloaded = await _storageUnitAdminRepository.GetStorageUnitByIdAsync(updated.id, includeDetails: false, cancellationToken);
            return MapToAdminDto(reloaded ?? updated);
        }
        catch (StorageUnitConcurrencyException ex)
        {
            throw new ConflictException(ex.Message, ex);
        }
        catch (StorageUnitUniqueConstraintException ex)
        {
            throw new ConflictException(ex.Message, ex);
        }
        catch (StorageUnitAreaScopeMismatchException ex)
        {
            throw new BadRequestException(ex.Message, ex);
        }
        catch (StorageUnitLockTimeoutException ex)
        {
            throw new ConflictException("A concurrency lock timeout occurred while updating the storage unit. Please retry.", ex);
        }
        catch (StorageUnitPersistenceException ex)
        {
            throw new BadRequestException(ex.Message, ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new NotFoundException(ex.Message, ex);
        }
    }

    public async Task<DeleteStorageUnitResult> DeleteAsync(
        long unitId,
        DateTimeOffset expectedUpdatedAt,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        var (_, isSysOrOps, isManager, isStaff) = await ValidateActorAndRolesAsync(actorUserId, cancellationToken);

        // Staff is strictly read-only
        if (isStaff && !isManager && !isSysOrOps)
        {
            throw new ForbiddenException("Facility staff does not have permission to delete storage units.");
        }

        if (expectedUpdatedAt == default)
        {
            throw new BadRequestException("ExpectedUpdatedAt is required for delete.");
        }

        var existing = await _storageUnitAdminRepository.GetStorageUnitByIdAsync(unitId, cancellationToken: cancellationToken);
        if (existing == null)
        {
            throw new NotFoundException($"Storage unit with ID {unitId} was not found.");
        }

        // Managers can only delete units in facilities where they are actively assigned as manager
        if (!isSysOrOps)
        {
            var hasManagerAccess = await _facilityScopeService.HasAccessToFacilityAsync(
                actorUserId, existing.facility_id, RoleConstants.FacilityManager, cancellationToken);
            if (!hasManagerAccess)
            {
                throw new ForbiddenException($"Manager is not assigned as manager at facility ID {existing.facility_id}.");
            }
        }

        // Pre-check: Unit must be available. Cannot delete occupied, maintenance, or reserved units.
        if (existing.physical_status != "available")
        {
            throw new ConflictException(
                $"Storage unit '{existing.unit_code}' cannot be deleted because its status is '{existing.physical_status}'. Only 'available' units can be deleted.");
        }

        // Define policy delegate executed atomically under the capacity lock
        Func<StorageUnitDeleteEvaluationContextKhoiVTH, Task<bool>> evaluatePolicy = ctx =>
        {
            // 1. Verify physical status under lock
            if (ctx.Unit.physical_status != "available")
            {
                return Task.FromResult(false);
            }

            // 2. Reject if any historical or active relational dependency exists
            if (ctx.ReferenceSummary.HasAnyReferences)
            {
                return Task.FromResult(false);
            }

            // 3. Block when facility/type has active holds or confirmed unassigned reservations,
            //    or if deleting would breach committed capacity.
            if (ctx.CapacityImpact.UnassignedHoldsCount > 0 ||
                ctx.CapacityImpact.UnassignedConfirmedCount > 0 ||
                ctx.CapacityImpact.WouldBreachCapacityIfDeleted)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        };

        try
        {
            var result = await _storageUnitAdminRepository.DeleteStorageUnitWithLockAsync(
                unitId,
                expectedUpdatedAt,
                evaluatePolicy,
                cancellationToken);

            if (!result.Success)
            {
                if (result.Message == "STORAGE_UNIT_HAS_EXISTING_REFERENCES" ||
                    (result.ReferenceSummary != null && result.ReferenceSummary.HasAnyReferences))
                {
                    throw new ConflictException(
                        $"Storage unit '{result.UnitCode}' cannot be deleted because it has existing history or relational records.");
                }

                if (result.CapacityImpact != null &&
                    (result.CapacityImpact.UnassignedHoldsCount > 0 ||
                     result.CapacityImpact.UnassignedConfirmedCount > 0 ||
                     result.CapacityImpact.WouldBreachCapacityIfDeleted))
                {
                    throw new ConflictException(
                        $"Storage unit '{result.UnitCode}' cannot be deleted because active reservation holds or confirmed bookings exist for this unit type.");
                }

                if (result.Message == "DELETE_POLICY_REJECTED")
                {
                    throw new ConflictException(
                        $"Storage unit '{result.UnitCode}' cannot be deleted due to policy restrictions (not available, existing references, or active capacity commitments).");
                }

                throw new ConflictException($"Storage unit deletion was rejected: {result.Message}");
            }

            return new DeleteStorageUnitResult
            {
                UnitId = result.UnitId,
                UnitCode = result.UnitCode,
                Success = true,
                Message = result.Message
            };
        }
        catch (StorageUnitConcurrencyException ex)
        {
            throw new ConflictException(ex.Message, ex);
        }
        catch (StorageUnitLockTimeoutException ex)
        {
            throw new ConflictException("A concurrency lock timeout occurred while deleting the storage unit. Please retry.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new NotFoundException(ex.Message, ex);
        }
    }

    // =========================================================================
    // Helpers & Mappers
    // =========================================================================

    private async Task<(UserIdentitySnapshotKhoiVTH Actor, bool IsAdminOrOps, bool IsManager, bool IsStaff)> ValidateActorAndRolesAsync(
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var actor = await _userRepository.GetIdentitySnapshotByIdAsync(actorUserId, _timeProvider.GetUtcNow(), cancellationToken);
        if (actor == null || actor.Status != UserStatusConstants.Active)
        {
            throw new UnauthorizedException("Actor user was not found or is inactive.");
        }

        var roles = actor.Roles;
        var isSysAdmin = roles.Contains(RoleConstants.SystemAdministrator);
        var isOpsManager = roles.Contains(RoleConstants.BusinessOperationsManager);
        var isFacManager = roles.Contains(RoleConstants.FacilityManager);
        var isFacStaff = roles.Contains(RoleConstants.FacilityStaff);

        if (!isSysAdmin && !isOpsManager && !isFacManager && !isFacStaff)
        {
            throw new ForbiddenException("Actor does not have permission to access storage unit administration.");
        }

        var hasEmployeeProfile = !string.IsNullOrWhiteSpace(actor.EmploymentStatus)
            || !string.IsNullOrWhiteSpace(actor.EmployeeCode)
            || !string.IsNullOrWhiteSpace(actor.EmployeeFullName);

        if (hasEmployeeProfile && !string.Equals(actor.EmploymentStatus, EmploymentStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException($"Actor has employee profile with non-active employment status: '{actor.EmploymentStatus}'. Access to storage unit administration is rejected.");
        }

        return (actor, isSysAdmin || isOpsManager, isFacManager, isFacStaff);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (Exception)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timezoneId, out var windowsTzId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(windowsTzId);
                }
                catch
                {
                    // Fall through
                }
            }
            else if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timezoneId, out var ianaTzId))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(ianaTzId);
                }
                catch
                {
                    // Fall through
                }
            }

            if (timezoneId.Equals("Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase) ||
                timezoneId.Equals("Asia/Bangkok", StringComparison.OrdinalIgnoreCase) ||
                timezoneId.Equals("Asia/Saigon", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                }
                catch
                {
                    return TimeZoneInfo.CreateCustomTimeZone("GMT+7", TimeSpan.FromHours(7), "GMT+7", "GMT+7");
                }
            }

            return TimeZoneInfo.Utc;
        }
    }

    private static StorageUnitAdminDto MapToAdminDto(storage_unit unit)
    {
        return new StorageUnitAdminDto
        {
            Id = unit.id,
            FacilityId = unit.facility_id,
            FacilityCode = unit.facility?.code ?? string.Empty,
            FacilityName = unit.facility?.name ?? string.Empty,
            UnitTypeId = unit.unit_type_id,
            UnitTypeCode = unit.unit_type?.code ?? string.Empty,
            UnitTypeName = unit.unit_type?.name ?? string.Empty,
            AreaId = unit.area_id,
            AreaCode = unit.area?.code,
            AreaName = unit.area?.name,
            UnitCode = unit.unit_code,
            FloorLabel = unit.floor_label,
            ZoneLabel = unit.zone_label,
            PhysicalStatus = unit.physical_status,
            IsListed = unit.is_listed,
            Notes = unit.notes,
            CreatedAt = unit.created_at,
            UpdatedAt = unit.updated_at
        };
    }

    private StorageUnitAdminDetailDto MapToDetailDto(storage_unit unit, StorageUnitReferenceSummaryKhoiVTH refSummary)
    {
        var activeAlloc = unit.unit_allocations.FirstOrDefault(a => a.status == "active");

        decimal monthlyRate = 0m;
        if (unit.facility?.facility_rates != null && unit.facility.facility_rates.Count > 0)
        {
            var tz = ResolveTimeZone(unit.facility.timezone);
            var localDateTime = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), tz);
            var effectiveDate = DateOnly.FromDateTime(localDateTime.DateTime);

            var rate = unit.facility.facility_rates
                .Where(r => r.facility_id == unit.facility_id &&
                            r.unit_type_id == unit.unit_type_id &&
                            r.valid_from <= effectiveDate &&
                            (r.valid_to == null || effectiveDate < r.valid_to.Value))
                .OrderByDescending(r => r.valid_from)
                .ThenByDescending(r => r.id)
                .FirstOrDefault();

            monthlyRate = rate?.monthly_rate ?? 0m;
        }

        return new StorageUnitAdminDetailDto
        {
            Id = unit.id,
            FacilityId = unit.facility_id,
            FacilityCode = unit.facility?.code ?? string.Empty,
            FacilityName = unit.facility?.name ?? string.Empty,
            UnitTypeId = unit.unit_type_id,
            UnitTypeCode = unit.unit_type?.code ?? string.Empty,
            UnitTypeName = unit.unit_type?.name ?? string.Empty,
            AreaId = unit.area_id,
            AreaCode = unit.area?.code,
            AreaName = unit.area?.name,
            UnitCode = unit.unit_code,
            FloorLabel = unit.floor_label,
            ZoneLabel = unit.zone_label,
            PhysicalStatus = unit.physical_status,
            IsListed = unit.is_listed,
            Notes = unit.notes,
            CreatedAt = unit.created_at,
            UpdatedAt = unit.updated_at,

            UnitTypeWidthMeters = unit.unit_type?.width_m ?? 0,
            UnitTypeLengthMeters = unit.unit_type?.length_m ?? 0,
            UnitTypeHeightMeters = unit.unit_type?.height_m ?? 0,
            UnitTypeFloorAreaSqm = unit.unit_type?.area_m2 ?? ((unit.unit_type?.width_m ?? 0) * (unit.unit_type?.length_m ?? 0)),
            UnitTypeVolumeCbm = unit.unit_type?.volume_m3 ?? ((unit.unit_type?.width_m ?? 0) * (unit.unit_type?.length_m ?? 0) * (unit.unit_type?.height_m ?? 0)),
            MonthlyRate = monthlyRate,

            AllocationsCount = refSummary.AllocationsCount,
            ActiveAllocationsCount = refSummary.ActiveAllocationsCount,
            InspectionsCount = refSummary.InspectionsCount,
            MaintenanceOrdersCount = refSummary.MaintenanceWorkOrdersCount,
            ActiveMaintenanceOrdersCount = refSummary.ActiveMaintenanceWorkOrdersCount,
            SupportTicketsCount = refSummary.SupportTicketsCount,
            StatusHistoryCount = refSummary.StatusHistoryCount,
            HasMapPosition = refSummary.HasMapPosition,

            CurrentCustomerName = activeAlloc?.agreement?.customer?.full_name,
            CurrentAgreementNo = activeAlloc?.agreement?.agreement_no,
            CurrentOccupiedSince = activeAlloc?.created_at
        };
    }
}
