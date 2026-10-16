using Microsoft.EntityFrameworkCore.Storage;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

public interface IUserRepository : IRepository<user>
{
    Task<user?> GetByEmailWithRolesAndProfilesAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    Task<user?> GetByIdWithRolesAndProfilesAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<user?> GetByEmailWithRolesAndProfilesAsNoTrackingAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    Task<user?> GetByIdWithRolesAndProfilesAsNoTrackingAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<SelfStorageManagementSystem.DataAccess.Models.UserIdentitySnapshot?> GetIdentitySnapshotByIdAsync(
        long id,
        DateTimeOffset? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<SelfStorageManagementSystem.DataAccess.Models.UserIdentitySnapshot?> GetIdentitySnapshotByEmailAsync(
        string normalizedEmail,
        DateTimeOffset? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task UpdateLastLoginAtAsync(
        long userId,
        DateTimeOffset lastLoginAt,
        CancellationToken cancellationToken = default);

    Task<(List<user> Users, int TotalCount)> GetPagedUsersAsync(
        string? searchTerm,
        string? status,
        string? roleCode,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    Task<bool> PhoneNumberExistsAsync(
        string phoneNumber,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);

    Task<bool> EmployeeCodeExistsAsync(
        string employeeCode,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);

    Task<bool> IdentityNumberExistsAsync(
        string identityNumber,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveSystemAdministratorsAsync(
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default);
}
