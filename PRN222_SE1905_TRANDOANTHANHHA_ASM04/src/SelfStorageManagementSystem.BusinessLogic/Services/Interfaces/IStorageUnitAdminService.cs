using System;
using System.Threading;
using System.Threading.Tasks;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

/// <summary>
/// Business logic service interface for administrative storage unit management.
/// Enforces server-side authentication, role and facility scope authorization, optimistic concurrency,
/// referential integrity checks, and capacity commitment protection under transactional capacity locks.
/// </summary>
public interface IStorageUnitAdminService
{
    Task<PagedResult<StorageUnitAdminDto>> ListAsync(
        StorageUnitFilterRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default);

    Task<StorageUnitAdminDetailDto?> GetByIdAsync(
        long unitId,
        long actorUserId,
        CancellationToken cancellationToken = default);

    Task<StorageUnitLookupsDto> GetLookupsAsync(
        long actorUserId,
        long? facilityId = null,
        CancellationToken cancellationToken = default);

    Task<StorageUnitAdminDto> CreateAsync(
        CreateStorageUnitRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default);

    Task<StorageUnitAdminDto> UpdateAsync(
        long unitId,
        UpdateStorageUnitRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default);

    Task<DeleteStorageUnitResult> DeleteAsync(
        long unitId,
        DateTimeOffset expectedUpdatedAt,
        long actorUserId,
        CancellationToken cancellationToken = default);
}
