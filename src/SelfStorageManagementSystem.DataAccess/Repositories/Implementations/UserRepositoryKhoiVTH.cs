using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Models;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

public class UserRepositoryKhoiVTH : GenericRepositoryKhoiVTH<user>, IUserRepository
{
    public UserRepositoryKhoiVTH(SelfStorageDbContext context) : base(context)
    {
    }

    public async Task<user?> GetByEmailWithRolesAndProfilesAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .Include(u => u.customer_profile)
            .Include(u => u.employee_profile)
                .ThenInclude(ep => ep!.staff_facility_assignments)
                    .ThenInclude(sfa => sfa.facility)
            .FirstOrDefaultAsync(u => u.email == normalizedEmail, cancellationToken);
    }

    public async Task<user?> GetByIdWithRolesAndProfilesAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .Include(u => u.customer_profile)
            .Include(u => u.employee_profile)
                .ThenInclude(ep => ep!.staff_facility_assignments)
                    .ThenInclude(sfa => sfa.facility)
            .FirstOrDefaultAsync(u => u.id == id, cancellationToken);
    }

    public async Task<user?> GetByEmailWithRolesAndProfilesAsNoTrackingAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .Include(u => u.customer_profile)
            .Include(u => u.employee_profile)
                .ThenInclude(ep => ep!.staff_facility_assignments)
                    .ThenInclude(sfa => sfa.facility)
            .FirstOrDefaultAsync(u => u.email == normalizedEmail, cancellationToken);
    }

    public async Task<user?> GetByIdWithRolesAndProfilesAsNoTrackingAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .Include(u => u.customer_profile)
            .Include(u => u.employee_profile)
                .ThenInclude(ep => ep!.staff_facility_assignments)
                    .ThenInclude(sfa => sfa.facility)
            .FirstOrDefaultAsync(u => u.id == id, cancellationToken);
    }

    public async Task<UserIdentitySnapshotKhoiVTH?> GetIdentitySnapshotByIdAsync(
        long id,
        DateTimeOffset? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = asOfUtc ?? DateTimeOffset.UtcNow;

        return await _dbSet
            .AsNoTracking()
            .Where(u => u.id == id)
            .Select(u => new UserIdentitySnapshotKhoiVTH
            {
                UserId = u.id,
                Email = u.email,
                PhoneNumber = u.phone_number,
                PasswordHash = u.password_hash,
                Status = u.status,
                LastLoginAt = u.last_login_at,
                CreatedAt = u.created_at,
                UpdatedAt = u.updated_at,
                CustomerFullName = u.customer_profile != null ? u.customer_profile.full_name : null,
                IdentityNumber = u.customer_profile != null ? u.customer_profile.identity_number : null,
                EmployeeFullName = u.employee_profile != null ? u.employee_profile.full_name : null,
                EmployeeCode = u.employee_profile != null ? u.employee_profile.employee_code : null,
                EmploymentStatus = u.employee_profile != null ? u.employee_profile.employment_status : null,
                Roles = u.user_roleusers.Select(ur => ur.role.code).ToList(),
                ActiveAssignments = (u.employee_profile != null &&
                                     u.employee_profile.employment_status == "active" &&
                                     u.user_roleusers.Any(ur => ur.role.code == "facility_manager" || ur.role.code == "facility_staff"))
                    ? u.employee_profile.staff_facility_assignments
                        .Where(a => a.starts_at <= now &&
                                    (a.ends_at == null || a.ends_at > now) &&
                                    ((a.assignment_role == "facility_manager" && u.user_roleusers.Any(ur => ur.role.code == "facility_manager")) ||
                                     (a.assignment_role == "facility_staff")))
                        .Select(a => new StaffAssignmentSnapshotKhoiVTH
                        {
                            Id = a.id,
                            EmployeeId = a.employee_id,
                            FacilityId = a.facility_id,
                            FacilityCode = a.facility.code,
                            FacilityName = a.facility.name,
                            AssignmentRole = a.assignment_role,
                            StartsAt = a.starts_at,
                            EndsAt = a.ends_at,
                            CreatedAt = a.created_at
                        }).ToList()
                    : new List<StaffAssignmentSnapshotKhoiVTH>()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserIdentitySnapshotKhoiVTH?> GetIdentitySnapshotByEmailAsync(
        string normalizedEmail,
        DateTimeOffset? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = asOfUtc ?? DateTimeOffset.UtcNow;

        return await _dbSet
            .AsNoTracking()
            .Where(u => u.email == normalizedEmail)
            .Select(u => new UserIdentitySnapshotKhoiVTH
            {
                UserId = u.id,
                Email = u.email,
                PhoneNumber = u.phone_number,
                PasswordHash = u.password_hash,
                Status = u.status,
                LastLoginAt = u.last_login_at,
                CreatedAt = u.created_at,
                UpdatedAt = u.updated_at,
                CustomerFullName = u.customer_profile != null ? u.customer_profile.full_name : null,
                IdentityNumber = u.customer_profile != null ? u.customer_profile.identity_number : null,
                EmployeeFullName = u.employee_profile != null ? u.employee_profile.full_name : null,
                EmployeeCode = u.employee_profile != null ? u.employee_profile.employee_code : null,
                EmploymentStatus = u.employee_profile != null ? u.employee_profile.employment_status : null,
                Roles = u.user_roleusers.Select(ur => ur.role.code).ToList(),
                ActiveAssignments = (u.employee_profile != null &&
                                     u.employee_profile.employment_status == "active" &&
                                     u.user_roleusers.Any(ur => ur.role.code == "facility_manager" || ur.role.code == "facility_staff"))
                    ? u.employee_profile.staff_facility_assignments
                        .Where(a => a.starts_at <= now &&
                                    (a.ends_at == null || a.ends_at > now) &&
                                    ((a.assignment_role == "facility_manager" && u.user_roleusers.Any(ur => ur.role.code == "facility_manager")) ||
                                     (a.assignment_role == "facility_staff")))
                        .Select(a => new StaffAssignmentSnapshotKhoiVTH
                        {
                            Id = a.id,
                            EmployeeId = a.employee_id,
                            FacilityId = a.facility_id,
                            FacilityCode = a.facility.code,
                            FacilityName = a.facility.name,
                            AssignmentRole = a.assignment_role,
                            StartsAt = a.starts_at,
                            EndsAt = a.ends_at,
                            CreatedAt = a.created_at
                        }).ToList()
                    : new List<StaffAssignmentSnapshotKhoiVTH>()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateLastLoginAtAsync(
        long userId,
        DateTimeOffset lastLoginAt,
        CancellationToken cancellationToken = default)
    {
        var trackedUser = _context.ChangeTracker.Entries<user>().FirstOrDefault(e => e.Entity.id == userId)?.Entity;
        if (trackedUser != null)
        {
            trackedUser.last_login_at = lastLoginAt;
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var user = await _dbSet.FirstOrDefaultAsync(u => u.id == userId, cancellationToken);
        if (user != null)
        {
            user.last_login_at = lastLoginAt;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<(List<user> Users, int TotalCount)> GetPagedUsersAsync(
        string? searchTerm,
        string? status,
        string? roleCode,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<user> query = _dbSet
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .Include(u => u.customer_profile)
            .Include(u => u.employee_profile)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(u =>
                u.email.Contains(term) ||
                (u.phone_number != null && u.phone_number.Contains(term)) ||
                (u.customer_profile != null && u.customer_profile.full_name.Contains(term)) ||
                (u.employee_profile != null && (u.employee_profile.full_name.Contains(term) || u.employee_profile.employee_code.Contains(term)))
            );
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim().ToLower();
            query = query.Where(u => u.status == st);
        }

        if (!string.IsNullOrWhiteSpace(roleCode))
        {
            var rc = roleCode.Trim().ToLower();
            query = query.Where(u => u.user_roleusers.Any(ur => ur.role.code == rc));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var users = await query
            .OrderByDescending(u => u.created_at)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (users, totalCount);
    }

    public async Task<bool> EmailExistsAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(u => u.email == normalizedEmail, cancellationToken);
    }

    public async Task<bool> PhoneNumberExistsAsync(
        string phoneNumber,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = phoneNumber.Trim();
        var query = _dbSet.Where(u => u.phone_number == trimmed);
        if (excludeUserId.HasValue)
        {
            query = query.Where(u => u.id != excludeUserId.Value);
        }
        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> EmployeeCodeExistsAsync(
        string employeeCode,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = employeeCode.Trim();
        var query = _context.employee_profiles.Where(ep => ep.employee_code == trimmed);
        if (excludeUserId.HasValue)
        {
            query = query.Where(ep => ep.user_id != excludeUserId.Value);
        }
        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> IdentityNumberExistsAsync(
        string identityNumber,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = identityNumber.Trim();
        var query = _context.customer_profiles.Where(cp => cp.identity_number == trimmed);
        if (excludeUserId.HasValue)
        {
            query = query.Where(cp => cp.user_id != excludeUserId.Value);
        }
        return await query.AnyAsync(cancellationToken);
    }

    public async Task<int> CountActiveSystemAdministratorsAsync(
        long? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Where(u => u.status == "active" && u.user_roleusers.Any(ur => ur.role.code == "system_administrator"));

        if (excludeUserId.HasValue)
        {
            query = query.Where(u => u.id != excludeUserId.Value);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsRelational())
        {
            return await _context.Database.BeginTransactionAsync(cancellationToken);
        }

        return new NoOpDbContextTransaction();
    }

    private sealed class NoOpDbContextTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();

        public void Commit() { }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Rollback() { }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
