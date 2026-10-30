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
/// Extends StorageUnitAdminRepository to provide both standard administrative operations and KhoiVTH compatibility views.
/// </summary>
public class StorageUnitKhoiVTHRepository : StorageUnitAdminRepository, IStorageUnitKhoiVTHRepository
{
    private readonly SelfStorageDbContext _dbContext;

    public StorageUnitKhoiVTHRepository(SelfStorageDbContext context) : base(context)
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
