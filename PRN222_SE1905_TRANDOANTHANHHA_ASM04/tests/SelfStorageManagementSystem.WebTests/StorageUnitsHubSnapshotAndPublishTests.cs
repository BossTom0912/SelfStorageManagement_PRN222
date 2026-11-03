using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Hubs;
using SelfStorageManagementSystem.Web.Realtime;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Unit tests for StorageUnitHub:
/// 1. Verifies HubDelete_StorageUnit correctly publishes PublishDeletedAsync on hard delete
///    and PublishUpdatedAsync on unlisting only.
/// 2. Verifies database commit result is preserved even if realtime publish degrades/fails.
/// 3. Verifies HubGet_StorageUnitsTableSnapshot enforces role-based CanManage (false for Staff, true for Manager),
///    validates facility scope, converts IDs to safe strings for JavaScript bigint compatibility,
///    clamps pagination parameters, and echoes ClientGeneration.
/// </summary>
public class StorageUnitsHubSnapshotAndPublishTests
{
    private readonly Mock<IStorageUnitAdminService> _mockService = new();
    private readonly Mock<IStorageUnitRealtimePublisher> _mockPublisher = new();
    private readonly Mock<IFacilityScopeService> _mockFacilityScope = new();
    private readonly Mock<IStorageUnitHubConnectionTracker> _mockTracker = new();
    private readonly Mock<IAccountIdentityService> _mockIdentity = new();
    private readonly Mock<IHubContext<StorageUnitHub>> _mockHubContext = new();

    private StorageUnitHub CreateHub()
    {
        var scopeRevocation = new StorageUnitHubScopeRevocationService(
            _mockHubContext.Object,
            _mockTracker.Object,
            _mockFacilityScope.Object,
            _mockIdentity.Object,
            NullLogger<StorageUnitHubScopeRevocationService>.Instance
        );

        return new StorageUnitHub(
            _mockService.Object,
            _mockPublisher.Object,
            _mockFacilityScope.Object,
            _mockTracker.Object,
            scopeRevocation,
            NullLogger<StorageUnitHub>.Instance
        );
    }

    private static void SetCaller(Hub hub, long userId, string role)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, $"User_{userId}"),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(c => c.User).Returns(principal);
        mockContext.Setup(c => c.ConnectionId).Returns("conn-test-1");

        hub.Context = mockContext.Object;
    }

    [Fact]
    public async Task HubDelete_HardDeleted_PublishesDeletedEvent_AndNotUpdatedEvent()
    {
        var hub = CreateHub();
        SetCaller(hub, 10, RoleConstants.FacilityManager);

        var existingDto = new StorageUnitAdminDetailDto
        {
            Id = 55,
            FacilityId = 1,
            FacilityName = "Cơ Sở 1",
            UnitCode = "UNIT-55",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _mockService.Setup(s => s.GetByIdAsync(55, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDto);

        var deleteResult = new DeleteStorageUnitResult
        {
            Success = true,
            IsUnlistedOnly = false,
            UnitId = 55,
            UnitCode = "UNIT-55",
            Message = "STORAGE_UNIT_DELETED"
        };
        _mockService.Setup(s => s.DeleteAsync(55, It.IsAny<DateTimeOffset>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleteResult);

        var returnedId = await hub.HubDelete_StorageUnit("55");

        Assert.Equal("55", returnedId);
        _mockPublisher.Verify(p => p.PublishDeletedAsync(55, 1, It.IsAny<CancellationToken>()), Times.Once);
        _mockPublisher.Verify(p => p.PublishUpdatedAsync(It.IsAny<StorageUnitAdminDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HubDelete_UnlistedOnly_PublishesUpdatedEvent_AndNotDeletedEvent()
    {
        var hub = CreateHub();
        SetCaller(hub, 10, RoleConstants.FacilityManager);

        var existingDto = new StorageUnitAdminDetailDto
        {
            Id = 77,
            FacilityId = 2,
            FacilityName = "Cơ Sở 2",
            UnitCode = "UNIT-77",
            IsListed = true,
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var updatedDto = new StorageUnitAdminDetailDto
        {
            Id = 77,
            FacilityId = 2,
            FacilityName = "Cơ Sở 2",
            UnitCode = "UNIT-77",
            IsListed = false,
            PhysicalStatus = "available",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockService.SetupSequence(s => s.GetByIdAsync(77, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDto)
            .ReturnsAsync(updatedDto);

        var deleteResult = new DeleteStorageUnitResult
        {
            Success = true,
            IsUnlistedOnly = true,
            UnitId = 77,
            UnitCode = "UNIT-77",
            Message = "STORAGE_UNIT_UNLISTED"
        };
        _mockService.Setup(s => s.DeleteAsync(77, It.IsAny<DateTimeOffset>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleteResult);

        var returnedId = await hub.HubDelete_StorageUnit("77");

        Assert.Equal("77", returnedId);
        _mockPublisher.Verify(p => p.PublishUpdatedAsync(It.Is<StorageUnitAdminDto>(u => u.Id == 77 && !u.IsListed), It.IsAny<CancellationToken>()), Times.Once);
        _mockPublisher.Verify(p => p.PublishDeletedAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockPublisher.Verify(p => p.PublishDeletedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HubDelete_RealtimeBroadcastFails_DoesNotFailDatabaseResult()
    {
        var hub = CreateHub();
        SetCaller(hub, 10, RoleConstants.FacilityManager);

        var existingDto = new StorageUnitAdminDetailDto
        {
            Id = 88,
            FacilityId = 1,
            UnitCode = "UNIT-88",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _mockService.Setup(s => s.GetByIdAsync(88, 10, It.IsAny<CancellationToken>())).ReturnsAsync(existingDto);

        var deleteResult = new DeleteStorageUnitResult
        {
            Success = true,
            IsUnlistedOnly = false,
            UnitId = 88,
            UnitCode = "UNIT-88"
        };
        _mockService.Setup(s => s.DeleteAsync(88, It.IsAny<DateTimeOffset>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleteResult);

        _mockPublisher.Setup(p => p.PublishDeletedAsync(88, 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RealtimePublishDegradedException("Delete", "Degraded broadcast channel"));

        var returnedId = await hub.HubDelete_StorageUnit("88");

        Assert.Equal("88", returnedId);
    }

    [Fact]
    public async Task HubDelete_StaffCaller_ThrowsHubException()
    {
        var hub = CreateHub();
        SetCaller(hub, 20, RoleConstants.FacilityStaff);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.HubDelete_StorageUnit("55"));
        Assert.Contains("Nhân viên thường không có quyền xóa", ex.Message);
    }

    [Fact]
    public async Task HubGetSnapshot_StaffCaller_ReturnsCanManageFalse_AndStringIds()
    {
        var hub = CreateHub();
        SetCaller(hub, 30, RoleConstants.FacilityStaff);

        var items = new List<StorageUnitAdminDto>
        {
            new()
            {
                Id = 987654321098765432L,
                UnitCode = "U-SAFE-ID",
                FacilityName = "Kho 1",
                UnitTypeName = "Small",
                AreaName = "A1",
                FloorLabel = "T1",
                ZoneLabel = "Z1",
                PhysicalStatus = "available",
                IsListed = true,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var pagedResult = new PagedResult<StorageUnitAdminDto>(items, 1, 1, 10);
        _mockService.Setup(s => s.ListAsync(It.IsAny<StorageUnitFilterRequest>(), 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        var query = new StorageUnitTableQueryRequest
        {
            PageNumber = 1,
            PageSize = 10,
            ClientGeneration = 777
        };

        var snapshot = await hub.HubGet_StorageUnitsTableSnapshot(query);

        Assert.NotNull(snapshot);
        Assert.False(snapshot.CanManage); // Staff cannot manage
        Assert.Equal(777, snapshot.ClientGeneration);
        Assert.Single(snapshot.Items);
        Assert.Equal("987654321098765432", snapshot.Items[0].Id); // bigint safe string
        Assert.Equal("U-SAFE-ID", snapshot.Items[0].UnitCode);
    }

    [Fact]
    public async Task HubGetSnapshot_ManagerCaller_ReturnsCanManageTrue()
    {
        var hub = CreateHub();
        SetCaller(hub, 40, RoleConstants.FacilityManager);

        var pagedResult = new PagedResult<StorageUnitAdminDto>(new List<StorageUnitAdminDto>(), 0, 1, 10);
        _mockService.Setup(s => s.ListAsync(It.IsAny<StorageUnitFilterRequest>(), 40, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pagedResult);

        var snapshot = await hub.HubGet_StorageUnitsTableSnapshot(new StorageUnitTableQueryRequest());

        Assert.NotNull(snapshot);
        Assert.True(snapshot.CanManage);
    }

    [Fact]
    public async Task HubGetSnapshot_FacilityScopeViolation_ThrowsHubException()
    {
        var hub = CreateHub();
        SetCaller(hub, 50, RoleConstants.FacilityStaff);

        _mockService.Setup(s => s.ListAsync(It.IsAny<StorageUnitFilterRequest>(), 50, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Bạn không có quyền truy cập dữ liệu cơ sở này."));

        var query = new StorageUnitTableQueryRequest { FacilityId = "99" };

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.HubGet_StorageUnitsTableSnapshot(query));
        Assert.Contains("Bạn không có quyền truy cập dữ liệu cơ sở này.", ex.Message);
    }

    [Fact]
    public async Task HubGetSnapshot_ClampsExcessivePageSizeAndZeroPageNumber()
    {
        var hub = CreateHub();
        SetCaller(hub, 60, RoleConstants.FacilityManager);

        StorageUnitFilterRequest? capturedRequest = null;
        _mockService.Setup(s => s.ListAsync(It.IsAny<StorageUnitFilterRequest>(), 60, It.IsAny<CancellationToken>()))
            .Callback<StorageUnitFilterRequest, long, CancellationToken>((req, uid, ct) => capturedRequest = req)
            .ReturnsAsync(new PagedResult<StorageUnitAdminDto>(new List<StorageUnitAdminDto>(), 0, 1, 100));

        var query = new StorageUnitTableQueryRequest
        {
            PageNumber = -5,
            PageSize = 999
        };

        var snapshot = await hub.HubGet_StorageUnitsTableSnapshot(query);

        Assert.NotNull(snapshot);
        Assert.NotNull(capturedRequest);
        Assert.Equal(1, capturedRequest.PageNumber);
        Assert.Equal(100, capturedRequest.PageSize);
    }
}
