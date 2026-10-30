using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Admin;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

/// <summary>
/// Core implementation of account authentication and identity snapshot querying.
/// Completely decoupled from JWT token generation so web cookie authentication
/// does not require a JWT secret.
/// </summary>
public class AccountIdentityService : IAccountIdentityService
{
    private readonly IUserRepository _userRepository;
    private readonly IRepository<role> _roleRepository;
    private readonly IRepository<user_role> _userRoleRepository;
    private readonly IRepository<customer_profile> _customerProfileRepository;
    private readonly IRepository<login_history> _loginHistoryRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IFacilityScopeService _facilityScopeService;
    private readonly IAuditService _auditService;
    private readonly ILogger<AccountIdentityService> _logger;

    public AccountIdentityService(
        IUserRepository userRepository,
        IRepository<role> roleRepository,
        IRepository<user_role> userRoleRepository,
        IRepository<customer_profile> customerProfileRepository,
        IRepository<login_history> loginHistoryRepository,
        IPasswordHasher passwordHasher,
        IFacilityScopeService facilityScopeService,
        IAuditService auditService,
        ILogger<AccountIdentityService> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _customerProfileRepository = customerProfileRepository;
        _loginHistoryRepository = loginHistoryRepository;
        _passwordHasher = passwordHasher;
        _facilityScopeService = facilityScopeService;
        _auditService = auditService;
        _logger = logger;
    }

    public Task<AuthenticatedAccountResponse> AuthenticateAsync(
        LoginRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AuthenticateAsync(request.Email, request.Password, ipAddress, userAgent, cancellationToken);
    }

    public async Task<AuthenticatedAccountResponse> AuthenticateAsync(
        string email,
        string password,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var normalizedEmail = email.Trim().ToLowerInvariant();

        // Query fresh user identity from database using projection/AsNoTracking
        var snapshot = await _userRepository.GetIdentitySnapshotByEmailAsync(normalizedEmail, DateTimeOffset.UtcNow, cancellationToken);

        long? userId = snapshot?.UserId;
        string? storedPasswordHash = snapshot?.PasswordHash;
        string? status = snapshot?.Status;
        string displayName = snapshot?.DisplayName ?? normalizedEmail;
        string? phoneNumber = snapshot?.PhoneNumber;
        List<string> roleCodes = snapshot?.Roles ?? new List<string>();
        string? userType = snapshot?.UserType;
        string? employeeCode = snapshot?.EmployeeCode;
        string? employmentStatus = snapshot?.EmploymentStatus;
        bool hasEmployeeProfile = snapshot?.HasEmployeeProfile ?? false;

        // Fallback to entity lookup if snapshot was not provided (e.g., custom repository mock in unit tests)
        if (snapshot == null)
        {
            var user = await _userRepository.GetByEmailWithRolesAndProfilesAsNoTrackingAsync(normalizedEmail, cancellationToken)
                       ?? await _userRepository.GetByEmailWithRolesAndProfilesAsync(normalizedEmail, cancellationToken);

            if (user != null)
            {
                userId = user.id;
                storedPasswordHash = user.password_hash;
                status = user.status;
                phoneNumber = user.phone_number;
                roleCodes = user.user_roleusers.Select(ur => ur.role.code).ToList();
                displayName = user.customer_profile?.full_name
                              ?? user.employee_profile?.full_name
                              ?? user.email;
                employeeCode = user.employee_profile?.employee_code;
                hasEmployeeProfile = user.employee_profile != null;
                employmentStatus = user.employee_profile?.employment_status;
                if (roleCodes.Contains(RoleConstants.SystemAdministrator))
                {
                    userType = "admin";
                }
                else if (user.employee_profile != null)
                {
                    userType = "employee";
                }
                else if (user.customer_profile != null)
                {
                    userType = "customer";
                }
            }
        }

        if (userId == null || string.IsNullOrEmpty(storedPasswordHash) || string.IsNullOrEmpty(status))
        {
            await RecordLoginHistoryAsync(null, normalizedEmail, LoginHistoryResultConstants.Failed, ipAddress, userAgent, cancellationToken);
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (status != UserStatusConstants.Active)
        {
            var resultStatus = status == UserStatusConstants.Locked
                ? LoginHistoryResultConstants.Locked
                : LoginHistoryResultConstants.Blocked;

            await RecordLoginHistoryAsync(userId, normalizedEmail, resultStatus, ipAddress, userAgent, cancellationToken);
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Nhân viên có employee profile nhưng employment_status khác active thì không được phép đăng nhập
        if (hasEmployeeProfile && !string.Equals(employmentStatus, EmploymentStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
        {
            await RecordLoginHistoryAsync(userId, normalizedEmail, LoginHistoryResultConstants.Blocked, ipAddress, userAgent, cancellationToken);
            throw new UnauthorizedException("Invalid email or password.");
        }

        var isPasswordValid = _passwordHasher.VerifyPassword(storedPasswordHash, password);
        if (!isPasswordValid)
        {
            await RecordLoginHistoryAsync(userId, normalizedEmail, LoginHistoryResultConstants.Failed, ipAddress, userAgent, cancellationToken);
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Login succeeded - update last_login_at and record login history exactly once
        var now = DateTimeOffset.UtcNow;
        await _userRepository.UpdateLastLoginAtAsync(userId.Value, now, cancellationToken);
        await RecordLoginHistoryAsync(userId, normalizedEmail, LoginHistoryResultConstants.Succeeded, ipAddress, userAgent, cancellationToken);

        return new AuthenticatedAccountResponse
        {
            UserId = userId.Value,
            DisplayName = displayName,
            Email = normalizedEmail,
            PhoneNumber = phoneNumber,
            Status = status,
            Roles = roleCodes,
            UserType = userType,
            EmployeeCode = employeeCode,
            LastLoginAt = now
        };
    }

    public async Task<CustomerRegisterResponse> RegisterCustomerAsync(
        CustomerRegisterRequest request,
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

        if (!string.IsNullOrWhiteSpace(request.IdentityNumber) &&
            await _userRepository.IdentityNumberExistsAsync(request.IdentityNumber, null, cancellationToken))
        {
            throw new ConflictException("Identity number is already associated with another customer.");
        }

        var customerRole = await _roleRepository.FirstOrDefaultAsync(
            r => r.code == RoleConstants.StorageCustomer,
            cancellationToken);

        if (customerRole == null)
        {
            throw new InvalidOperationException("System role 'storage_customer' was not found in the database.");
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

            var customerProfile = new customer_profile
            {
                user_id = newUser.id,
                full_name = request.FullName.Trim(),
                identity_number = string.IsNullOrWhiteSpace(request.IdentityNumber) ? null : request.IdentityNumber.Trim(),
                date_of_birth = request.DateOfBirth,
                address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
                emergency_contact_name = string.IsNullOrWhiteSpace(request.EmergencyContactName) ? null : request.EmergencyContactName.Trim(),
                emergency_contact_phone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone) ? null : request.EmergencyContactPhone.Trim(),
                created_at = now,
                updated_at = now
            };

            await _customerProfileRepository.AddAsync(customerProfile, cancellationToken);

            var userRole = new user_role
            {
                user_id = newUser.id,
                role_id = customerRole.id,
                granted_at = now
            };

            await _userRoleRepository.AddAsync(userRole, cancellationToken);
            await _customerProfileRepository.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            await _auditService.LogAsync(
                newUser.id,
                RoleConstants.StorageCustomer,
                "users",
                newUser.id.ToString(),
                "REGISTER_CUSTOMER",
                null,
                new { Email = newUser.email, FullName = customerProfile.full_name },
                null,
                null,
                cancellationToken);

            return new CustomerRegisterResponse
            {
                UserId = newUser.id,
                Email = newUser.email,
                FullName = customerProfile.full_name,
                CreatedAt = newUser.created_at
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Transaction rolled back during customer registration for email {Email}", normalizedEmail);
            throw;
        }
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        // Read fresh snapshot using projection / AsNoTracking to avoid returning stale cached tracked entities
        var snapshot = await _userRepository.GetIdentitySnapshotByIdAsync(userId, DateTimeOffset.UtcNow, cancellationToken);

        if (snapshot != null)
        {
            if (snapshot.Status != UserStatusConstants.Active)
            {
                return null;
            }

            // Nếu người dùng có hồ sơ nhân sự nhưng employment_status khác active (leave, terminated), từ chối ngay lập tức
            if (snapshot.HasEmployeeProfile && !string.Equals(snapshot.EmploymentStatus, EmploymentStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var isEmployeeActive = string.Equals(snapshot.EmploymentStatus, EmploymentStatusConstants.Active, StringComparison.OrdinalIgnoreCase);
            var isManager = snapshot.Roles.Contains(RoleConstants.FacilityManager);
            var isStaff = snapshot.Roles.Contains(RoleConstants.FacilityStaff);

            List<FacilityAssignmentDto> assignments;
            if (!isEmployeeActive || (!isManager && !isStaff))
            {
                assignments = new List<FacilityAssignmentDto>();
            }
            else
            {
                assignments = snapshot.ActiveAssignments
                    .Where(a => isManager
                        ? (a.AssignmentRole == RoleConstants.FacilityManager || a.AssignmentRole == RoleConstants.FacilityStaff)
                        : (a.AssignmentRole == RoleConstants.FacilityStaff))
                    .Select(a => new FacilityAssignmentDto
                    {
                        Id = a.Id,
                        EmployeeId = a.EmployeeId,
                        FacilityId = a.FacilityId,
                        FacilityCode = a.FacilityCode,
                        FacilityName = a.FacilityName,
                        AssignmentRole = a.AssignmentRole,
                        StartsAt = a.StartsAt,
                        EndsAt = a.EndsAt,
                        CreatedAt = a.CreatedAt
                    }).ToList();
            }

            return new CurrentUserResponse
            {
                UserId = snapshot.UserId,
                DisplayName = snapshot.DisplayName,
                Email = snapshot.Email,
                PhoneNumber = snapshot.PhoneNumber,
                Status = snapshot.Status,
                Roles = snapshot.Roles,
                UserType = snapshot.UserType,
                EmployeeCode = snapshot.EmployeeCode,
                EmploymentStatus = snapshot.EmploymentStatus,
                ActiveAssignments = assignments
            };
        }

        // Fallback if repository mock does not implement projection query
        var user = await _userRepository.GetByIdWithRolesAndProfilesAsNoTrackingAsync(userId, cancellationToken)
                   ?? await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);

        if (user == null || user.status != UserStatusConstants.Active)
        {
            return null;
        }

        // Nếu người dùng có hồ sơ nhân sự nhưng employment_status khác active, từ chối ngay lập tức
        if (user.employee_profile != null && !string.Equals(user.employee_profile.employment_status, EmploymentStatusConstants.Active, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var roleCodes = user.user_roleusers
            .Select(ur => ur.role.code)
            .ToList();

        var displayName = user.customer_profile?.full_name
                          ?? user.employee_profile?.full_name
                          ?? user.email;

        var activeAssignments = await _facilityScopeService.GetActiveAssignmentsForUserAsync(userId, cancellationToken);

        string? userType = null;
        if (roleCodes.Contains(RoleConstants.SystemAdministrator))
        {
            userType = "admin";
        }
        else if (user.employee_profile != null)
        {
            userType = "employee";
        }
        else if (user.customer_profile != null)
        {
            userType = "customer";
        }

        return new CurrentUserResponse
        {
            UserId = user.id,
            DisplayName = displayName,
            Email = user.email,
            PhoneNumber = user.phone_number,
            Status = user.status,
            Roles = roleCodes,
            UserType = userType,
            EmployeeCode = user.employee_profile?.employee_code,
            EmploymentStatus = user.employee_profile?.employment_status,
            ActiveAssignments = activeAssignments
        };
    }

    private async Task RecordLoginHistoryAsync(
        long? userId,
        string attemptedEmail,
        string result,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        try
        {
            var history = new login_history
            {
                user_id = userId,
                attempted_email = attemptedEmail,
                result = result,
                ip_address = ipAddress,
                user_agent = userAgent,
                occurred_at = DateTimeOffset.UtcNow
            };

            await _loginHistoryRepository.AddAsync(history, cancellationToken);
            await _loginHistoryRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record login history for email {Email}", attemptedEmail);
        }
    }
}
