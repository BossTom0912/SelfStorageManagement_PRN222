using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class DemoAccountBootstrapServiceKhoiVTH : IDemoAccountBootstrapService
{
    private readonly SelfStorageDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoAccountBootstrapServiceKhoiVTH> _logger;

    private const string PlaceholderHash = "external-auth-demo-only";
    private const string LegacyPlaceholderPrefix = "PH:v1:";

    public DemoAccountBootstrapServiceKhoiVTH(
        SelfStorageDbContext context,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ILogger<DemoAccountBootstrapServiceKhoiVTH> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task BootstrapDemoAccountsAsync(CancellationToken cancellationToken = default)
    {
        var adminEmail = _configuration["AdminAccount:Email"];

        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            throw new InvalidOperationException(
                "Admin account identity configuration is missing. " +
                "Please configure 'AdminAccount:Email' in appsettings.json or configuration.");
        }

        var adminUser = await _context.users
            .Include(u => u.user_roleusers)
                .ThenInclude(ur => ur.role)
            .FirstOrDefaultAsync(u => u.email == adminEmail, cancellationToken);

        if (adminUser == null)
        {
            throw new InvalidOperationException(
                $"Admin user with email '{adminEmail}' specified in 'AdminAccount:Email' does not exist in the database.");
        }

        var hasAdminRole = adminUser.user_roleusers
            .Any(ur => ur.role != null && ur.role.code == RoleConstants.SystemAdministrator);

        if (!hasAdminRole)
        {
            throw new InvalidOperationException(
                $"Configured admin user '{adminEmail}' exists in the database but does not have the required role '{RoleConstants.SystemAdministrator}'.");
        }

        var adminPassword = _configuration["AdminAccount:Password"]
                            ?? Environment.GetEnvironmentVariable("ADMIN_ACCOUNT_PASSWORD");

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException(
                "Admin account bootstrapping is enabled, but admin password is not configured. " +
                "Please configure a password via User Secrets ('dotnet user-secrets set \"AdminAccount:Password\" \"<password>\"') " +
                "or the 'ADMIN_ACCOUNT_PASSWORD' environment variable (never commit passwords to appsettings.json).");
        }

        var validAdminHash = _passwordHasher.HashPassword(adminPassword);
        adminUser.password_hash = validAdminHash;
        adminUser.updated_at = DateTimeOffset.UtcNow;
        _logger.LogInformation("[Bootstrap] Verified role '{Role}' and updated password hash for admin account '{Email}'.", RoleConstants.SystemAdministrator, adminEmail);

        var demoUsersWithPlaceholderHash = await _context.users
            .Where(u => u.id != adminUser.id && (u.password_hash == PlaceholderHash || u.password_hash.StartsWith(LegacyPlaceholderPrefix)))
            .ToListAsync(cancellationToken);

        if (demoUsersWithPlaceholderHash.Count > 0)
        {
            var demoPassword = _configuration["DemoAccounts:DefaultPassword"]
                               ?? Environment.GetEnvironmentVariable("DEMO_DEFAULT_PASSWORD")
                               ?? adminPassword;

            var validDemoHash = _passwordHasher.HashPassword(demoPassword);
            foreach (var user in demoUsersWithPlaceholderHash)
            {
                user.password_hash = validDemoHash;
                user.updated_at = DateTimeOffset.UtcNow;
                _logger.LogInformation("[Bootstrap] Replaced placeholder hash for demo user '{Email}'", user.email);
            }
        }

        await EnsureDemoProfilesAsync(cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[Bootstrap] Successfully bootstrapped admin and {Count} demo accounts.", demoUsersWithPlaceholderHash.Count);
    }

    private async Task EnsureDemoProfilesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var firstFacility = await _context.facilities.FirstOrDefaultAsync(cancellationToken);

        var manager = await _context.users
            .Include(u => u.employee_profile)
            .FirstOrDefaultAsync(u => u.email == "manager.d1@selfstorage.local", cancellationToken);

        if (manager != null)
        {
            if (manager.employee_profile == null)
            {
                _context.employee_profiles.Add(new employee_profile
                {
                    user_id = manager.id,
                    employee_code = "MGR-D1-001",
                    full_name = "Manager District 1",
                    hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                    employment_status = EmploymentStatusConstants.Active,
                    created_at = now,
                    updated_at = now
                });
            }

            if (firstFacility != null)
            {
                var hasAssignment = await _context.staff_facility_assignments
                    .AnyAsync(a => a.employee_id == manager.id, cancellationToken);
                if (!hasAssignment)
                {
                    _context.staff_facility_assignments.Add(new staff_facility_assignment
                    {
                        employee_id = manager.id,
                        facility_id = firstFacility.id,
                        assignment_role = RoleConstants.FacilityManager,
                        starts_at = now.AddMonths(-1),
                        created_at = now
                    });
                }
            }
        }

        var staff = await _context.users
            .Include(u => u.employee_profile)
            .FirstOrDefaultAsync(u => u.email == "staff.d1@selfstorage.local", cancellationToken);

        if (staff != null)
        {
            if (staff.employee_profile == null)
            {
                _context.employee_profiles.Add(new employee_profile
                {
                    user_id = staff.id,
                    employee_code = "STF-D1-001",
                    full_name = "Staff District 1",
                    hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                    employment_status = EmploymentStatusConstants.Active,
                    created_at = now,
                    updated_at = now
                });
            }

            if (firstFacility != null)
            {
                var hasAssignment = await _context.staff_facility_assignments
                    .AnyAsync(a => a.employee_id == staff.id, cancellationToken);
                if (!hasAssignment)
                {
                    _context.staff_facility_assignments.Add(new staff_facility_assignment
                    {
                        employee_id = staff.id,
                        facility_id = firstFacility.id,
                        assignment_role = RoleConstants.FacilityStaff,
                        starts_at = now.AddMonths(-1),
                        created_at = now
                    });
                }
            }
        }

        var customer = await _context.users
            .Include(u => u.customer_profile)
            .FirstOrDefaultAsync(u => u.email == "customer1@example.com", cancellationToken);

        if (customer != null && customer.customer_profile == null)
        {
            _context.customer_profiles.Add(new customer_profile
            {
                user_id = customer.id,
                full_name = "Customer One",
                identity_number = "DEMO-CUSTOMER-003",
                date_of_birth = new DateOnly(2000, 1, 1),
                address = "Ho Chi Minh City",
                created_at = now,
                updated_at = now
            });
        }
    }
}
