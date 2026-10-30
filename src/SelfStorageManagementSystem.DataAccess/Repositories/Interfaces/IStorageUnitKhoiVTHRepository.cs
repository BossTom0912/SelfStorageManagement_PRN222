using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Models;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

/// <summary>
/// Repository interface for KhoiVTH (Võ Thái Hoàng Khôi - PRN222) storage unit data access operations.
/// </summary>
public interface IStorageUnitKhoiVTHRepository : IStorageUnitAdminRepository
{
    Task<IReadOnlyList<StorageUnitKhoiVTH>> GetAllKhoiVTHUnitsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UnitTypeKhoiVTH>> GetAllKhoiVTHTypesAsync(CancellationToken cancellationToken = default);
}
