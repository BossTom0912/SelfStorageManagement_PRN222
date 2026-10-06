using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRepository<role> _roleRepository;
    private readonly IRepository<user_role> _userRoleRepository;
    private readonly IRepository<customer_profile> _customerProfileRepository;
    private readonly IRepository<login_history> _loginHistoryRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IFacilityScopeService _facilityScopeService;
    private readonly IAuditService _auditService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IRepository<role> roleRepository,
        IRepository<user_role> userRoleRepository,
        IRepository<customer_profile> customerProfileRepository,
        IRepository<login_history> loginHistoryRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IFacilityScopeService facilityScopeService,
        IAuditService auditService,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _customerProfileRepository = customerProfileRepository;
        _loginHistoryRepository = loginHistoryRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _facilityScopeService = facilityScopeService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _userRepository.GetByEmailWithRolesAndProfilesAsync(normalizedEmail, cancellationToken);

        if (user == null)
        {
            await RecordLoginHistoryAsync(null, normalizedEmail, LoginHistoryResultConstants.Failed, ipAddress, userAgent, cancellationToken);
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (user.status != UserStatusConstants.Active)
        {
            var resultStatus = user.status == UserStatusConstants.Locked
                ? LoginHistoryResultConstants.Locked
                : LoginHistoryResultConstants.Blocked;

            await RecordLoginHistoryAsync(user.id, normalizedEmail, resultStatus, ipAddress, userAgent, cancellationToken);
            throw new UnauthorizedException("Account is inactive or locked. Please contact system administrator.");
        }

        var isPasswordValid = _passwordHasher.VerifyPassword(user.password_hash, request.Password);
        if (!isPasswordValid)
        {
            await RecordLoginHistoryAsync(user.id, normalizedEmail, LoginHistoryResultConstants.Failed, ipAddress, userAgent, cancellationToken);
            throw new UnauthorizedException("Invalid email or password.");
        }

        // Login succeeded
        user.last_login_at = DateTimeOffset.UtcNow;
        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        await RecordLoginHistoryAsync(user.id, normalizedEmail, LoginHistoryResultConstants.Succeeded, ipAddress, userAgent, cancellationToken);

        var roleCodes = user.user_roleusers
            .Select(ur => ur.role.code)
            .ToList();

        var displayName = user.customer_profile?.full_name
                          ?? user.employee_profile?.full_name
                          ?? user.email;

        var (token, expiresAt) = _jwtTokenGenerator.GenerateToken(user, roleCodes);

        return new LoginResponse
        {
            UserId = user.id,
            DisplayName = displayName,
            Email = user.email,
            Roles = roleCodes,
            AccessToken = token,
            ExpiresAt = expiresAt
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

    public async Task<CurrentUserResponse> GetCurrentUserAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdWithRolesAndProfilesAsync(userId, cancellationToken);
        if (user == null || user.status != UserStatusConstants.Active)
        {
            throw new UnauthorizedException("User not found or account is deactivated.");
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
