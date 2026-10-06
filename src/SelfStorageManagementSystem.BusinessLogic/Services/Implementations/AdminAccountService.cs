using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Admin;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class AdminAccountService : IAdminAccountService
{
    private readonly SelfStorageDbContext _context;
    private readonly IUserRepository _userRepository;
    private readonly IRepository<role> _roleRepository;
    private readonly IRepository<user_role> _userRoleRepository;
    private readonly IRepository<employee_profile> _employeeProfileRepository;
    private readonly IRepository<staff_facility_assignment> _assignmentRepository;
    private readonly IRepository<facility> _facilityRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _auditService;
    private readonly ILogger<AdminAccountService> _logger;

    public AdminAccountService(
        SelfStorageDbContext context,
        IUserRepository userRepository,
        IRepository<role> roleRepository,
        IRepository<user_role> userRoleRepository,
        IRepository<employee_profile> employeeProfileRepository,
        IRepository<staff_facility_assignment> assignmentRepository,
        IRepository<facility> facilityRepository,
        IPasswordHasher passwordHasher,
        IAuditService auditService,
        ILogger<AdminAccountService> logger)
    {
        _context = context;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _employeeProfileRepository = employeeProfileRepository;
        _assignmentRepository = assignmentRepository;
        _facilityRepository = facilityRepository;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<PagedResult<UserAccountDto>> GetAccountsAsync(
        GetAccountsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (users, totalCount) = await _userRepository.GetPagedUsersAsync(
            request.SearchTerm,
            request.Status,
            request.RoleCode,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        var userIds = users.Select(u => u.id).ToList();
        var assignments = await _context.staff_facility_assignments
            .Include(a => a.facility)
            .Where(a => userIds.Contains(a.employee_id))
            .ToListAsync(cancellationToken);

        var items = users.Select(u => MapToDto(u, assignments.Where(a => a.employee_id == u.id).ToList())).ToList();

        return new PagedResult<UserAccountDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }

    public async Task<UserAccountDto> GetAccountByIdAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException($"User with ID {userId} was not found.");
        }

        var assignments = await _context.staff_facility_assignments
            .Include(a => a.facility)
            .Where(a => a.employee_id == userId)
            .ToListAsync(cancellationToken);

        return MapToDto(user, assignments);
    }

    public async Task<UserAccountDto> CreateStaffAccountAsync(
        CreateStaffAccountRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _userRepository.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            throw new ConflictException("Email is already registered.");
        }

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) &&
            await _userRepository.PhoneNumberExistsAsync(request.PhoneNumber, null, cancellationToken))
        {
            throw new ConflictException("Phone number is already associated with another account.");
        }

        var trimmedEmpCode = request.EmployeeCode.Trim();
        if (await _userRepository.EmployeeCodeExistsAsync(trimmedEmpCode, null, cancellationToken))
        {
            throw new ConflictException("Employee code is already in use.");
        }

        if (!RoleConstants.IsEmployeeRole(request.RoleCode))
        {
            throw new BadRequestException($"Invalid employee role '{request.RoleCode}'. Must be one of: {string.Join(", ", RoleConstants.EmployeeRoles)}.");
        }

        var roleEntity = await _roleRepository.FirstOrDefaultAsync(r => r.code == request.RoleCode, cancellationToken);
        if (roleEntity == null)
        {
            throw new NotFoundException($"Role '{request.RoleCode}' was not found in the database.");
        }

        if (request.InitialFacilityId.HasValue)
        {
            var facilityExists = await _facilityRepository.AnyAsync(f => f.id == request.InitialFacilityId.Value, cancellationToken);
            if (!facilityExists)
            {
                throw new NotFoundException($"Facility with ID {request.InitialFacilityId.Value} does not exist.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var hashedPassword = _passwordHasher.HashPassword(request.Password);

        await using var transaction = await _userRepository.BeginTransactionAsync(cancellationToken);
        try
        {
            var newUser = new user
            {
                email = normalizedEmail,
                phone_number = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
                password_hash = hashedPassword,
                status = UserStatusConstants.Active,
                created_at = now,
                updated_at = now
            };

            await _userRepository.AddAsync(newUser, cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            var profile = new employee_profile
            {
                user_id = newUser.id,
                employee_code = trimmedEmpCode,
                full_name = request.FullName.Trim(),
                hire_date = request.HireDate,
                employment_status = EmploymentStatusConstants.Active,
                created_at = now,
                updated_at = now
            };

            await _employeeProfileRepository.AddAsync(profile, cancellationToken);

            var userRole = new user_role
            {
                user_id = newUser.id,
                role_id = roleEntity.id,
                granted_by = actorUserId,
                granted_at = now
            };

            await _userRoleRepository.AddAsync(userRole, cancellationToken);

            staff_facility_assignment? initialAssignment = null;
            if (request.InitialFacilityId.HasValue &&
                (request.RoleCode == RoleConstants.FacilityStaff || request.RoleCode == RoleConstants.FacilityManager))
            {
                initialAssignment = new staff_facility_assignment
                {
                    employee_id = newUser.id,
                    facility_id = request.InitialFacilityId.Value,
                    assignment_role = request.RoleCode,
                    starts_at = now,
                    ends_at = null,
                    assigned_by = actorUserId,
                    created_at = now
                };

                await _assignmentRepository.AddAsync(initialAssignment, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await _auditService.LogAsync(
                actorUserId,
                RoleConstants.SystemAdministrator,
                "users",
                newUser.id.ToString(),
                "CREATE_STAFF_ACCOUNT",
                null,
                new
                {
                    Email = newUser.email,
                    Role = request.RoleCode,
                    EmployeeCode = profile.employee_code,
                    FullName = profile.full_name,
                    FacilityId = request.InitialFacilityId
                },
                ipAddress,
                requestId,
                cancellationToken);

            var assignmentsList = initialAssignment != null
                ? new List<staff_facility_assignment> { initialAssignment }
                : new List<staff_facility_assignment>();

            newUser.employee_profile = profile;
            newUser.user_roleusers.Add(userRole);

            return MapToDto(newUser, assignmentsList);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Transaction rolled back while creating staff account for email {Email}", normalizedEmail);
            throw;
        }
    }

    public async Task<UserAccountDto> UpdateUserStatusAsync(
        long userId,
        UpdateUserStatusRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedStatus = request.Status.Trim().ToLowerInvariant();
        if (!UserStatusConstants.IsValidStatus(normalizedStatus))
        {
            throw new BadRequestException($"Invalid status '{request.Status}'. Must be one of: {string.Join(", ", UserStatusConstants.AllStatuses)}.");
        }

        var user = await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException($"User with ID {userId} was not found.");
        }

        var isSystemAdmin = user.user_roleusers.Any(ur => ur.role.code == RoleConstants.SystemAdministrator);

        // Safety rule: Prevent locking or deactivating the last active administrator
        if (isSystemAdmin && normalizedStatus != UserStatusConstants.Active)
        {
            var remainingAdmins = await _userRepository.CountActiveSystemAdministratorsAsync(user.id, cancellationToken);
            if (remainingAdmins < 1)
            {
                throw new BadRequestException("Cannot deactivate or lock the only remaining active system administrator account.");
            }
        }

        var oldStatus = user.status;
        user.status = normalizedStatus;
        user.updated_at = DateTimeOffset.UtcNow;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            actorUserId,
            RoleConstants.SystemAdministrator,
            "users",
            user.id.ToString(),
            "UPDATE_USER_STATUS",
            new { Status = oldStatus },
            new { Status = normalizedStatus },
            ipAddress,
            requestId,
            cancellationToken);

        var assignments = await _context.staff_facility_assignments
            .Include(a => a.facility)
            .Where(a => a.employee_id == userId)
            .ToListAsync(cancellationToken);

        return MapToDto(user, assignments);
    }

    public async Task<UserAccountDto> ManageUserRolesAsync(
        long userId,
        ManageUserRolesRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RoleCodes == null || request.RoleCodes.Count == 0)
        {
            throw new BadRequestException("At least one role must be assigned to the user.");
        }

        var normalizedRoles = request.RoleCodes
            .Select(r => r.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        foreach (var r in normalizedRoles)
        {
            if (!RoleConstants.IsValidRole(r))
            {
                throw new BadRequestException($"Invalid role '{r}'. Valid roles are: {string.Join(", ", RoleConstants.AllRoles)}.");
            }
        }

        var user = await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException($"User with ID {userId} was not found.");
        }

        var currentRoleCodes = user.user_roleusers.Select(ur => ur.role.code).ToList();

        // Safety rule: If revoking system_administrator role, ensure there's at least one remaining active admin
        var hadAdmin = currentRoleCodes.Contains(RoleConstants.SystemAdministrator);
        var willHaveAdmin = normalizedRoles.Contains(RoleConstants.SystemAdministrator);

        if (hadAdmin && !willHaveAdmin)
        {
            var remainingAdmins = await _userRepository.CountActiveSystemAdministratorsAsync(user.id, cancellationToken);
            if (remainingAdmins < 1)
            {
                throw new BadRequestException("Cannot revoke the system administrator role from the only remaining system administrator.");
            }
        }

        // Safety rule: Cannot assign employee roles without employee profile
        var hasEmployeeRole = normalizedRoles.Any(RoleConstants.IsEmployeeRole);
        if (hasEmployeeRole && user.employee_profile == null)
        {
            throw new BadRequestException("Cannot assign employee roles to an account without an employee profile.");
        }

        var allRoles = await _roleRepository.GetAllAsync(cancellationToken);

        // Remove unassigned roles
        var toRemove = user.user_roleusers
            .Where(ur => !normalizedRoles.Contains(ur.role.code))
            .ToList();

        foreach (var ur in toRemove)
        {
            _userRoleRepository.Delete(ur);
        }

        // Add newly assigned roles
        var existingRoleCodes = user.user_roleusers.Select(ur => ur.role.code).ToHashSet();
        var now = DateTimeOffset.UtcNow;

        foreach (var roleCode in normalizedRoles)
        {
            if (!existingRoleCodes.Contains(roleCode))
            {
                var roleEntity = allRoles.First(r => r.code == roleCode);
                var newUr = new user_role
                {
                    user_id = user.id,
                    role_id = roleEntity.id,
                    granted_by = actorUserId,
                    granted_at = now
                };
                await _userRoleRepository.AddAsync(newUr, cancellationToken);
            }
        }

        user.updated_at = now;
        _userRepository.Update(user);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            actorUserId,
            RoleConstants.SystemAdministrator,
            "user_roles",
            user.id.ToString(),
            "MANAGE_USER_ROLES",
            new { Roles = currentRoleCodes },
            new { Roles = normalizedRoles },
            ipAddress,
            requestId,
            cancellationToken);

        var refreshedUser = await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);
        var assignments = await _context.staff_facility_assignments
            .Include(a => a.facility)
            .Where(a => a.employee_id == userId)
            .ToListAsync(cancellationToken);

        return MapToDto(refreshedUser!, assignments);
    }

    public async Task<FacilityAssignmentDto> AssignFacilityAsync(
        long userId,
        CreateFacilityAssignmentRequest request,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException($"User with ID {userId} was not found.");
        }

        if (user.employee_profile == null || user.employee_profile.employment_status != EmploymentStatusConstants.Active)
        {
            throw new BadRequestException("User does not have an active employee profile.");
        }

        var normalizedRole = request.AssignmentRole.Trim().ToLowerInvariant();
        if (normalizedRole != RoleConstants.FacilityStaff && normalizedRole != RoleConstants.FacilityManager)
        {
            throw new BadRequestException("Assignment role must be either 'facility_staff' or 'facility_manager'.");
        }

        var userRoles = user.user_roleusers.Select(ur => ur.role.code).ToList();
        if (!userRoles.Contains(normalizedRole))
        {
            throw new BadRequestException($"User does not possess the role '{normalizedRole}' required for this assignment.");
        }

        var facility = await _facilityRepository.GetByIdAsync(new object[] { request.FacilityId }, cancellationToken);
        if (facility == null)
        {
            throw new NotFoundException($"Facility with ID {request.FacilityId} does not exist.");
        }

        if (request.EndsAt.HasValue && request.EndsAt.Value <= request.StartsAt)
        {
            throw new BadRequestException("EndsAt must be greater than StartsAt.");
        }

        // Check overlapping assignment for same employee, facility, and role
        var maxDate = DateTimeOffset.MaxValue;
        var reqStart = request.StartsAt;
        var reqEnd = request.EndsAt ?? maxDate;

        var hasOverlap = await _context.staff_facility_assignments.AnyAsync(a =>
            a.employee_id == userId &&
            a.facility_id == request.FacilityId &&
            a.assignment_role == normalizedRole &&
            reqStart < (a.ends_at ?? maxDate) &&
            reqEnd > a.starts_at,
            cancellationToken);

        if (hasOverlap)
        {
            throw new ConflictException("An employee cannot have overlapping assignments for the same facility and role.");
        }

        var now = DateTimeOffset.UtcNow;
        var assignment = new staff_facility_assignment
        {
            employee_id = userId,
            facility_id = request.FacilityId,
            assignment_role = normalizedRole,
            starts_at = request.StartsAt,
            ends_at = request.EndsAt,
            assigned_by = actorUserId,
            created_at = now
        };

        await _assignmentRepository.AddAsync(assignment, cancellationToken);
        await _assignmentRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            actorUserId,
            RoleConstants.SystemAdministrator,
            "staff_facility_assignments",
            assignment.id.ToString(),
            "ASSIGN_FACILITY",
            null,
            new
            {
                EmployeeId = userId,
                FacilityId = request.FacilityId,
                Role = normalizedRole,
                StartsAt = request.StartsAt,
                EndsAt = request.EndsAt
            },
            ipAddress,
            requestId,
            cancellationToken);

        return new FacilityAssignmentDto
        {
            Id = assignment.id,
            EmployeeId = assignment.employee_id,
            FacilityId = assignment.facility_id,
            FacilityCode = facility.code,
            FacilityName = facility.name,
            AssignmentRole = assignment.assignment_role,
            StartsAt = assignment.starts_at,
            EndsAt = assignment.ends_at,
            CreatedAt = assignment.created_at
        };
    }

    public async Task TerminateFacilityAssignmentAsync(
        long assignmentId,
        long actorUserId,
        string? ipAddress = null,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentRepository.GetByIdAsync(new object[] { assignmentId }, cancellationToken);
        if (assignment == null)
        {
            throw new NotFoundException($"Facility assignment with ID {assignmentId} was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        if (assignment.ends_at.HasValue && assignment.ends_at.Value <= now)
        {
            throw new BadRequestException("This assignment has already ended.");
        }

        var previousEnd = assignment.ends_at;
        assignment.ends_at = now;

        _assignmentRepository.Update(assignment);
        await _assignmentRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            actorUserId,
            RoleConstants.SystemAdministrator,
            "staff_facility_assignments",
            assignment.id.ToString(),
            "TERMINATE_FACILITY_ASSIGNMENT",
            new { EndsAt = previousEnd },
            new { EndsAt = now },
            ipAddress,
            requestId,
            cancellationToken);
    }

    public async Task<List<FacilityLookupDto>> GetFacilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var facilities = await _facilityRepository.GetAllAsync(cancellationToken);
        return facilities.Select(f => new FacilityLookupDto
        {
            Id = f.id,
            Code = f.code,
            Name = f.name,
            AddressLine = f.address_line,
            City = f.city,
            Status = f.status
        }).OrderBy(f => f.Name).ToList();
    }

    private static UserAccountDto MapToDto(user u, List<staff_facility_assignment> assignments)
    {
        return new UserAccountDto
        {
            Id = u.id,
            Email = u.email,
            PhoneNumber = u.phone_number,
            Status = u.status,
            LastLoginAt = u.last_login_at,
            CreatedAt = u.created_at,
            Roles = u.user_roleusers.Select(ur => ur.role.code).ToList(),
            FullName = u.customer_profile?.full_name ?? u.employee_profile?.full_name,
            IdentityNumber = u.customer_profile?.identity_number,
            EmployeeCode = u.employee_profile?.employee_code,
            EmploymentStatus = u.employee_profile?.employment_status,
            HireDate = u.employee_profile?.hire_date,
            FacilityAssignments = assignments.Select(a => new FacilityAssignmentDto
            {
                Id = a.id,
                EmployeeId = a.employee_id,
                FacilityId = a.facility_id,
                FacilityCode = a.facility?.code ?? string.Empty,
                FacilityName = a.facility?.name ?? string.Empty,
                AssignmentRole = a.assignment_role,
                StartsAt = a.starts_at,
                EndsAt = a.ends_at,
                CreatedAt = a.created_at
            }).ToList()
        };
    }
}
