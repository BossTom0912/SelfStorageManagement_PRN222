using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class FacilityScopeService : IFacilityScopeService
{
    private readonly SelfStorageDbContext _context;
    private readonly IUserRepository _userRepository;

    public FacilityScopeService(
        SelfStorageDbContext context,
        IUserRepository userRepository)
    {
        _context = context;
        _userRepository = userRepository;
    }

    public async Task<bool> HasAccessToFacilityAsync(
        long userId,
        long facilityId,
        string requiredRole,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);
        if (user == null || user.status != UserStatusConstants.Active)
        {
            return false;
        }

        var roles = user.user_roleusers.Select(ur => ur.role.code).ToList();

        // System Admin and Business Operations Manager have system-wide access
        if (roles.Contains(RoleConstants.SystemAdministrator) ||
            roles.Contains(RoleConstants.BusinessOperationsManager))
        {
            return true;
        }

        // Must have an active employee profile
        if (user.employee_profile == null || user.employee_profile.employment_status != EmploymentStatusConstants.Active)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        var query = _context.staff_facility_assignments
            .Where(a => a.employee_id == userId &&
                        a.facility_id == facilityId &&
                        a.starts_at <= now &&
                        (a.ends_at == null || a.ends_at > now));

        if (requiredRole == RoleConstants.FacilityStaff)
        {
            var isManager = roles.Contains(RoleConstants.FacilityManager);
            var isStaff = roles.Contains(RoleConstants.FacilityStaff);

            if (!isManager && !isStaff)
            {
                return false;
            }

            if (isManager)
            {
                // Manager role allows operating at staff level with either staff or manager assignment
                return await query.AnyAsync(a => a.assignment_role == RoleConstants.FacilityStaff ||
                                                 a.assignment_role == RoleConstants.FacilityManager,
                                            cancellationToken);
            }

            // Staff role cannot perform operations via manager assignments; requires staff assignment
            return await query.AnyAsync(a => a.assignment_role == RoleConstants.FacilityStaff,
                                        cancellationToken);
        }
        else if (requiredRole == RoleConstants.FacilityManager)
        {
            // Requires active manager role on the account (staff cannot access manager operations)
            if (!roles.Contains(RoleConstants.FacilityManager))
            {
                return false;
            }

            return await query.AnyAsync(a => a.assignment_role == RoleConstants.FacilityManager,
                                        cancellationToken);
        }

        return false;
    }

    public async Task<List<long>> GetAccessibleFacilityIdsAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);
        if (user == null || user.status != UserStatusConstants.Active)
        {
            return new List<long>();
        }

        var roles = user.user_roleusers.Select(ur => ur.role.code).ToList();

        // System Admin & Business Operations Manager can access all active facilities
        if (roles.Contains(RoleConstants.SystemAdministrator) ||
            roles.Contains(RoleConstants.BusinessOperationsManager))
        {
            return await _context.facilities
                .Select(f => f.id)
                .ToListAsync(cancellationToken);
        }

        if (user.employee_profile == null || user.employee_profile.employment_status != EmploymentStatusConstants.Active)
        {
            return new List<long>();
        }

        var isManager = roles.Contains(RoleConstants.FacilityManager);
        var isStaff = roles.Contains(RoleConstants.FacilityStaff);

        if (!isManager && !isStaff)
        {
            return new List<long>();
        }

        var now = DateTimeOffset.UtcNow;
        var query = _context.staff_facility_assignments
            .Where(a => a.employee_id == userId &&
                        a.starts_at <= now &&
                        (a.ends_at == null || a.ends_at > now));

        if (isManager)
        {
            return await query
                .Where(a => a.assignment_role == RoleConstants.FacilityManager ||
                            a.assignment_role == RoleConstants.FacilityStaff)
                .Select(a => a.facility_id)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        return await query
            .Where(a => a.assignment_role == RoleConstants.FacilityStaff)
            .Select(a => a.facility_id)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<FacilityAssignmentDto>> GetActiveAssignmentsForUserAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);
        if (user == null || user.status != UserStatusConstants.Active)
        {
            return new List<FacilityAssignmentDto>();
        }

        if (user.employee_profile == null || user.employee_profile.employment_status != EmploymentStatusConstants.Active)
        {
            return new List<FacilityAssignmentDto>();
        }

        var roles = user.user_roleusers.Select(ur => ur.role.code).ToList();
        var isManager = roles.Contains(RoleConstants.FacilityManager);
        var isStaff = roles.Contains(RoleConstants.FacilityStaff);

        if (!isManager && !isStaff)
        {
            return new List<FacilityAssignmentDto>();
        }

        var now = DateTimeOffset.UtcNow;

        var query = _context.staff_facility_assignments
            .Include(a => a.facility)
            .Where(a => a.employee_id == userId &&
                        a.starts_at <= now &&
                        (a.ends_at == null || a.ends_at > now));

        if (isManager)
        {
            query = query.Where(a => a.assignment_role == RoleConstants.FacilityManager ||
                                     a.assignment_role == RoleConstants.FacilityStaff);
        }
        else
        {
            query = query.Where(a => a.assignment_role == RoleConstants.FacilityStaff);
        }

        return await query
            .Select(a => new FacilityAssignmentDto
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
            })
            .ToListAsync(cancellationToken);
    }
}
