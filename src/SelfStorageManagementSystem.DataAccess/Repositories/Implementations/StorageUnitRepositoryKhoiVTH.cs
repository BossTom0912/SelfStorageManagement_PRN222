using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

/// <summary>
/// Concrete repository implementation for KhoiVTH (Võ Thái Hoàng Khôi - PRN222) storage unit data access.
/// Extends StorageUnitAdminRepositoryKhoiVTH to provide both standard administrative operations and KhoiVTH compatibility views.
/// </summary>
public class StorageUnitRepositoryKhoiVTH : StorageUnitAdminRepositoryKhoiVTH, IStorageUnitKhoiVTHRepository
{
    private readonly SelfStorageDbContext _dbContext;

    public StorageUnitRepositoryKhoiVTH(SelfStorageDbContext context) : base(context)
    {
        _dbContext = context;
    }

    public async Task<IReadOnlyList<StorageUnitKhoiVTH>> GetAllKhoiVTHUnitsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.StorageUnitKhoiVTHs
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UnitTypeKhoiVTH>> GetAllKhoiVTHTypesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.UnitTypeKhoiVTHs
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
