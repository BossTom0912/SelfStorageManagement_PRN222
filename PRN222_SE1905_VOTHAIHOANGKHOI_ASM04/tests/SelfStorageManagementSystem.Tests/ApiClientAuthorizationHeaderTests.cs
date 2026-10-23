using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SelfStorageManagementSystem.WpfClient.Models;
using SelfStorageManagementSystem.WpfClient.Services;
using Xunit;

namespace SelfStorageManagementSystem.Tests;

public class ApiClientAuthorizationHeaderTests : IDisposable
{
    private readonly RecordingHttpMessageHandler _handler;
    private readonly ApiClient _apiClient;

    public ApiClientAuthorizationHeaderTests()
    {
        SessionStore.Clear();
        _handler = new RecordingHttpMessageHandler();
        _apiClient = new ApiClient(_handler)
        {
            BaseUrl = "https://test.api.local"
        };
    }

    public void Dispose()
    {
        SessionStore.Clear();
    }

    [Fact]
    public async Task LoginAsync_Then_GetMeAsync_Then_GetCatalogFacilitiesAsync_VerifiesHeaderIntegrity()
    {
        // Arrange
        const string mockToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.dummyPayload.signature";

        _handler.ResponseFactory = req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;

            if (path.EndsWith("/api/auth/login"))
            {
                var body = JsonSerializer.Serialize(new ApiResponse<LoginResponse>
                {
                    Success = true,
                    Data = new LoginResponse
                    {
                        AccessToken = mockToken,
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(2)
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }

            if (path.EndsWith("/api/auth/me"))
            {
                var body = JsonSerializer.Serialize(new ApiResponse<CurrentUserResponse>
                {
                    Success = true,
                    Data = new CurrentUserResponse
                    {
                        UserId = 1,
                        Email = "testuser@example.com",
                        DisplayName = "Test User",
                        Roles = new List<string> { "storage_customer" }
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }

            if (path.EndsWith("/api/facilities"))
            {
                var body = JsonSerializer.Serialize(new ApiResponse<PagedResult<FacilityCatalogModel>>
                {
                    Success = true,
                    Data = new PagedResult<FacilityCatalogModel>
                    {
                        Items = new List<FacilityCatalogModel>(),
                        TotalCount = 0,
                        PageNumber = 1,
                        PageSize = 10
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // 1. Step 1: LoginAsync
        var loginResult = await _apiClient.LoginAsync("testuser@example.com", "SecurePassword123!");
        Assert.True(loginResult.Success);
        Assert.Single(_handler.RecordedRequests);
        Assert.Null(_handler.RecordedRequests[0].Authorization);
        Assert.Equal(mockToken, SessionStore.AccessToken);

        // 2. Step 2: GetMeAsync
        var meResult = await _apiClient.GetMeAsync();
        Assert.True(meResult.Success);
        Assert.Equal(2, _handler.RecordedRequests.Count);
        Assert.NotNull(_handler.RecordedRequests[1].Authorization);
        Assert.Equal("Bearer", _handler.RecordedRequests[1].Authorization!.Scheme);
        Assert.Equal(mockToken, _handler.RecordedRequests[1].Authorization!.Parameter);

        // 3. Step 3: GetCatalogFacilitiesAsync (Public catalog request)
        var catalogResult = await _apiClient.GetCatalogFacilitiesAsync(null, null, null, 1, 10);
        Assert.True(catalogResult.Success);
        Assert.Equal(3, _handler.RecordedRequests.Count);
        // CRITICAL CHECK: Public catalog request MUST NOT contain Authorization header
        Assert.Null(_handler.RecordedRequests[2].Authorization);
    }

    [Fact]
    public async Task LoginAsync_And_RegisterCustomerAsync_DoNotSendAuthorization_EvenIfPriorSessionExists()
    {
        // Arrange - Seed an existing active session
        SessionStore.SetSession("stale-leftover-token", DateTimeOffset.UtcNow.AddHours(1));

        _handler.ResponseFactory = req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/api/auth/login"))
            {
                var body = JsonSerializer.Serialize(new ApiResponse<LoginResponse>
                {
                    Success = true,
                    Data = new LoginResponse
                    {
                        AccessToken = "new-fresh-token",
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(2)
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }

            if (path.EndsWith("/api/auth/register-customer"))
            {
                var body = JsonSerializer.Serialize(new ApiResponse<CustomerRegisterResponse>
                {
                    Success = true,
                    Data = new CustomerRegisterResponse
                    {
                        UserId = 99,
                        Email = "newbie@example.com",
                        FullName = "Newbie Customer"
                    }
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
            };
        };

        // Act & Assert for Login
        var loginResult = await _apiClient.LoginAsync("newbie@example.com", "Password123!");
        Assert.True(loginResult.Success);
        Assert.Null(_handler.RecordedRequests[0].Authorization);

        // Act & Assert for Register
        var registerResult = await _apiClient.RegisterCustomerAsync(new CustomerRegisterModel
        {
            Email = "newbie2@example.com",
            Password = "Password123!",
            FullName = "Newbie 2",
            PhoneNumber = "0901234567"
        });
        Assert.True(registerResult.Success);
        Assert.Null(_handler.RecordedRequests[1].Authorization);
    }

    [Fact]
    public async Task After401Unauthorized_SubsequentPublicRequestDoesNotSendOldToken()
    {
        // Arrange
        SessionStore.SetSession("expired-or-revoked-token", DateTimeOffset.UtcNow.AddMinutes(30));
        var sessionExpiredFired = false;
        _apiClient.SessionExpired += () => sessionExpiredFired = true;

        _handler.ResponseFactory = req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/api/auth/me"))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{\"success\":false,\"message\":\"Unauthorized\"}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"data\":{\"items\":[],\"totalCount\":0,\"pageNumber\":1,\"pageSize\":10}}", Encoding.UTF8, "application/json")
            };
        };

        // Act 1: Call authenticated API which returns 401
        var meResult = await _apiClient.GetMeAsync();
        Assert.False(meResult.Success);
        Assert.True(sessionExpiredFired);
        Assert.Null(SessionStore.AccessToken);
        Assert.NotNull(_handler.RecordedRequests[0].Authorization);

        // Act 2: Subsequent public catalog call
        var catalogResult = await _apiClient.GetCatalogFacilitiesAsync(null, null, null, 1, 10);
        Assert.True(catalogResult.Success);
        Assert.Equal(2, _handler.RecordedRequests.Count);
        Assert.Null(_handler.RecordedRequests[1].Authorization);
    }

    [Fact]
    public async Task AllCatalogEndpoints_DoNotSendAuthorization_WhenUserIsLoggedIn()
    {
        // Arrange
        const string activeToken = "valid-active-user-token";
        SessionStore.SetSession(activeToken, DateTimeOffset.UtcNow.AddHours(1));

        _handler.ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
        };

        // Act
        await _apiClient.GetCatalogFacilitiesAsync("Hanoi", "Cau Giay", "Store", 1, 10);
        await _apiClient.GetFacilityUnitTypesAsync(1, null, null, null, null);
        await _apiClient.GetAvailableUnitsAsync(1, null, null, null, null, null, null, 1, 10);
        await _apiClient.GetFacilityFloorMapAsync(1, null, null, null);

        // Assert
        Assert.Equal(4, _handler.RecordedRequests.Count);
        foreach (var req in _handler.RecordedRequests)
        {
            Assert.Null(req.Authorization);
        }
    }

    [Fact]
    public async Task InsecureHttpUrl_IsBlockedSynchronouslyBeforeSending()
    {
        // Arrange
        _apiClient.BaseUrl = "http://insecure-api.local";

        // Act
        var result = await _apiClient.GetCatalogFacilitiesAsync(null, null, null, 1, 10);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ApiClient.InsecureOrInvalidUrlErrorMessage, result.Message);
        Assert.Empty(_handler.RecordedRequests);
    }

    private class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public List<RecordedRequest> RecordedRequests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest
            {
                Method = request.Method,
                RequestUri = request.RequestUri,
                Authorization = request.Headers.Authorization
            };
            RecordedRequests.Add(recorded);

            if (ResponseFactory != null)
            {
                return Task.FromResult(ResponseFactory(request));
            }

            var defaultResp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(defaultResp);
        }
    }

    private class RecordedRequest
    {
        public HttpMethod Method { get; set; } = null!;
        public Uri? RequestUri { get; set; }
        public AuthenticationHeaderValue? Authorization { get; set; }
    }
}
