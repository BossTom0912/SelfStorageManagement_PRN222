using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

/// <summary>
/// Business logic service interface for KhoiVTH (Võ Thái Hoàng Khôi - PRN222) storage unit management.
/// Integrates storage unit administration, role & facility validation, optimistic locking, and KhoiVTH view queries.
/// </summary>
public interface IStorageUnitKhoiVTHService : IStorageUnitAdminService
{
    Task<IReadOnlyList<StorageUnitKhoiVTH>> GetKhoiVTHUnitsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UnitTypeKhoiVTH>> GetKhoiVTHTypesAsync(CancellationToken cancellationToken = default);
}
