using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

/// <summary>
/// Concrete service implementation for KhoiVTH (Võ Thái Hoàng Khôi - PRN222) storage unit business logic.
/// Inherits from StorageUnitAdminServiceKhoiVTH to execute all storage unit operations with KhoiVTH anti-plagiarism identity.
/// </summary>
public class StorageUnitServiceKhoiVTH : StorageUnitAdminServiceKhoiVTH, IStorageUnitKhoiVTHService
{
    private readonly IStorageUnitAdminRepository _repository;

    public StorageUnitServiceKhoiVTH(
        IStorageUnitAdminRepository storageUnitAdminRepository,
        IUserRepository userRepository,
        IFacilityScopeService facilityScopeService,
        TimeProvider? timeProvider = null)
        : base(storageUnitAdminRepository, userRepository, facilityScopeService, timeProvider)
    {
        _repository = storageUnitAdminRepository ?? throw new ArgumentNullException(nameof(storageUnitAdminRepository));
    }

    public async Task<IReadOnlyList<StorageUnitKhoiVTH>> GetKhoiVTHUnitsAsync(CancellationToken cancellationToken = default)
    {
        if (_repository is IStorageUnitKhoiVTHRepository khoiRepo)
        {
            return await khoiRepo.GetAllKhoiVTHUnitsAsync(cancellationToken);
        }

        return Array.Empty<StorageUnitKhoiVTH>();
    }

    public async Task<IReadOnlyList<UnitTypeKhoiVTH>> GetKhoiVTHTypesAsync(CancellationToken cancellationToken = default)
    {
        if (_repository is IStorageUnitKhoiVTHRepository khoiRepo)
        {
            return await khoiRepo.GetAllKhoiVTHTypesAsync(cancellationToken);
        }

        return Array.Empty<UnitTypeKhoiVTH>();
    }
}
