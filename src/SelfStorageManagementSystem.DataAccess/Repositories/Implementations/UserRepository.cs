using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.DataAccess.Repositories.Implementations;

public class UserRepository : GenericRepository<user>, IUserRepository
{
    public UserRepository(SelfStorageDbContext context) : base(context)
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
        return await _context.Database.BeginTransactionAsync(cancellationToken);
    }
}
