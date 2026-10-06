using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRepository<role>> _roleRepoMock = new();
    private readonly Mock<IRepository<user_role>> _userRoleRepoMock = new();
    private readonly Mock<IRepository<customer_profile>> _customerProfileRepoMock = new();
    private readonly Mock<IRepository<login_history>> _loginHistoryRepoMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGeneratorMock = new();
    private readonly Mock<IFacilityScopeService> _facilityScopeMock = new();
    private readonly Mock<IAuditService> _auditMock = new();
    private readonly Mock<ILogger<AuthService>> _loggerMock = new();

    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _authService = new AuthService(
            _userRepoMock.Object,
            _roleRepoMock.Object,
            _userRoleRepoMock.Object,
            _customerProfileRepoMock.Object,
            _loginHistoryRepoMock.Object,
            _passwordHasherMock.Object,
            _jwtGeneratorMock.Object,
            _facilityScopeMock.Object,
            _auditMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldSucceedAndRecordSucceededHistory()
    {
        var email = "user@example.test";
        var password = "ValidPassword123";
        var user = new user
        {
            id = 10,
            email = email,
            password_hash = "hashed_pw",
            status = UserStatusConstants.Active,
            customer_profile = new customer_profile { full_name = "Minh Anh" }
        };
        var role = new role { id = 1, code = RoleConstants.StorageCustomer };
        user.user_roleusers.Add(new user_role { user_id = 10, role_id = 1, role = role });

        _userRepoMock.Setup(r => r.GetByEmailWithRolesAndProfilesAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock.Setup(h => h.VerifyPassword("hashed_pw", password))
            .Returns(true);

        _jwtGeneratorMock.Setup(g => g.GenerateToken(user, It.IsAny<IEnumerable<string>>()))
            .Returns(("dummy_token", DateTimeOffset.UtcNow.AddHours(1)));

        var response = await _authService.LoginAsync(new LoginRequest { Email = email, Password = password });

        Assert.NotNull(response);
        Assert.Equal("Minh Anh", response.DisplayName);
        Assert.Equal("dummy_token", response.AccessToken);
        Assert.Contains(RoleConstants.StorageCustomer, response.Roles);

        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Succeeded && lh.user_id == 10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentEmail_ShouldThrowUnauthorizedAndRecordFailedHistory()
    {
        var email = "nonexistent@example.test";
        _userRepoMock.Setup(r => r.GetByEmailWithRolesAndProfilesAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((user?)null);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _authService.LoginAsync(new LoginRequest { Email = email, Password = "AnyPassword" }));

        Assert.Equal("Invalid email or password.", ex.Message);

        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Failed && lh.attempted_email == email && lh.user_id == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ShouldThrowSameUnauthorizedMessageAndRecordFailedHistory()
    {
        var email = "user@example.test";
        var user = new user
        {
            id = 10,
            email = email,
            password_hash = "hashed_pw",
            status = UserStatusConstants.Active
        };

        _userRepoMock.Setup(r => r.GetByEmailWithRolesAndProfilesAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock.Setup(h => h.VerifyPassword("hashed_pw", "WrongPassword"))
            .Returns(false);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _authService.LoginAsync(new LoginRequest { Email = email, Password = "WrongPassword" }));

        // Public message must be identical to non-existent email
        Assert.Equal("Invalid email or password.", ex.Message);

        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Failed && lh.user_id == 10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithLockedAccount_ShouldThrowUnauthorizedAndRecordLockedHistory()
    {
        var email = "locked@example.test";
        var user = new user
        {
            id = 20,
            email = email,
            password_hash = "hashed_pw",
            status = UserStatusConstants.Locked
        };

        _userRepoMock.Setup(r => r.GetByEmailWithRolesAndProfilesAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _authService.LoginAsync(new LoginRequest { Email = email, Password = "ValidPassword" }));

        Assert.Contains("locked", ex.Message, StringComparison.OrdinalIgnoreCase);

        _loginHistoryRepoMock.Verify(h => h.AddAsync(
            It.Is<login_history>(lh => lh.result == LoginHistoryResultConstants.Locked && lh.user_id == 20),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterCustomerAsync_WithDuplicateEmail_ShouldThrowConflictException()
    {
        var email = "existing@example.test";
        _userRepoMock.Setup(r => r.EmailExistsAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new CustomerRegisterRequest
        {
            Email = email,
            Password = "Password123!",
            FullName = "Nguyen Van A"
        };

        await Assert.ThrowsAsync<ConflictException>(() =>
            _authService.RegisterCustomerAsync(request));
    }
}
