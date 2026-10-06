using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SelfStorageManagementSystem.WpfClient.Models;

namespace SelfStorageManagementSystem.WpfClient.Services;

public class ApiClient
{
    private static readonly Lazy<ApiClient> _instance = new(() => new ApiClient());
    public static ApiClient Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string BaseUrl { get; set; } = "https://localhost:7031";

    public event Action? SessionExpired;

    private ApiClient()
    {
        var handler = new HttpClientHandler
        {
            // Allow dev local self-signed certs for testing
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        _httpClient = new HttpClient(handler);
    }

    private void EnsureAuthorizationHeader()
    {
        if (!string.IsNullOrEmpty(SessionStore.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", SessionStore.AccessToken);
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(string email, string password)
    {
        var request = new LoginModel
        {
            Email = email,
            Password = password
        };

        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/auth/login", request);
        var result = await HandleResponseAsync<LoginResponse>(response);

        if (result.Success && result.Data != null)
        {
            SessionStore.SetSession(result.Data.AccessToken, result.Data.ExpiresAt);
        }

        return result;
    }

    public async Task<ApiResponse<CustomerRegisterResponse>> RegisterCustomerAsync(CustomerRegisterModel model)
    {
        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/auth/register-customer", model);
        return await HandleResponseAsync<CustomerRegisterResponse>(response);
    }

    public async Task<ApiResponse<CurrentUserResponse>> GetMeAsync()
    {
        EnsureAuthorizationHeader();
        var response = await _httpClient.GetAsync($"{BaseUrl}/api/auth/me");
        var result = await HandleResponseAsync<CurrentUserResponse>(response);

        if (result.Success && result.Data != null)
        {
            SessionStore.SetCurrentUser(result.Data);
        }

        return result;
    }

    public async Task<ApiResponse<PagedResult<UserAccountModel>>> GetAccountsAsync(
        string? searchTerm,
        string? status,
        string? roleCode,
        int pageNumber,
        int pageSize)
    {
        EnsureAuthorizationHeader();
        var queryParams = new List<string>
        {
            $"pageNumber={pageNumber}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrWhiteSpace(searchTerm))
            queryParams.Add($"searchTerm={Uri.EscapeDataString(searchTerm)}");
        if (!string.IsNullOrWhiteSpace(status))
            queryParams.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(roleCode))
            queryParams.Add($"roleCode={Uri.EscapeDataString(roleCode)}");

        var queryString = string.Join("&", queryParams);
        var response = await _httpClient.GetAsync($"{BaseUrl}/api/admin/accounts?{queryString}");
        return await HandleResponseAsync<PagedResult<UserAccountModel>>(response);
    }

    public async Task<ApiResponse<UserAccountModel>> CreateStaffAccountAsync(CreateStaffModel model)
    {
        EnsureAuthorizationHeader();
        var payload = new
        {
            email = model.Email,
            password = model.Password,
            phoneNumber = model.PhoneNumber,
            fullName = model.FullName,
            employeeCode = model.EmployeeCode,
            hireDate = model.HireDate.ToString("yyyy-MM-dd"),
            roleCode = model.RoleCode,
            initialFacilityId = model.InitialFacilityId
        };

        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/admin/accounts/staff", payload);
        return await HandleResponseAsync<UserAccountModel>(response);
    }

    public async Task<ApiResponse<UserAccountModel>> UpdateUserStatusAsync(long userId, string status)
    {
        EnsureAuthorizationHeader();
        var payload = new { status };
        var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/api/admin/accounts/{userId}/status")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await _httpClient.SendAsync(request);
        return await HandleResponseAsync<UserAccountModel>(response);
    }

    public async Task<ApiResponse<UserAccountModel>> ManageUserRolesAsync(long userId, List<string> roleCodes)
    {
        EnsureAuthorizationHeader();
        var payload = new { roleCodes };
        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/admin/accounts/{userId}/roles", payload);
        return await HandleResponseAsync<UserAccountModel>(response);
    }

    public async Task<ApiResponse<FacilityAssignmentModel>> AssignFacilityAsync(
        long userId,
        long facilityId,
        string assignmentRole,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt)
    {
        EnsureAuthorizationHeader();
        var payload = new
        {
            facilityId,
            assignmentRole,
            startsAt,
            endsAt
        };

        var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/admin/accounts/{userId}/facility-assignments", payload);
        return await HandleResponseAsync<FacilityAssignmentModel>(response);
    }

    public async Task<ApiResponse<object?>> TerminateFacilityAssignmentAsync(long assignmentId)
    {
        EnsureAuthorizationHeader();
        var response = await _httpClient.PostAsync($"{BaseUrl}/api/admin/accounts/facility-assignments/{assignmentId}/terminate", null);
        return await HandleResponseAsync<object?>(response);
    }

    public async Task<ApiResponse<List<FacilityLookupModel>>> GetFacilitiesAsync()
    {
        EnsureAuthorizationHeader();
        var response = await _httpClient.GetAsync($"{BaseUrl}/api/admin/facilities");
        return await HandleResponseAsync<List<FacilityLookupModel>>(response);
    }

    public void Logout()
    {
        SessionStore.Clear();
        EnsureAuthorizationHeader();
    }

    private async Task<ApiResponse<T>> HandleResponseAsync<T>(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            SessionStore.Clear();
            SessionExpired?.Invoke();
            var unauthorizedResp = await TryReadApiResponse<T>(response);
            return unauthorizedResp ?? new ApiResponse<T>
            {
                Success = false,
                Message = "Session expired or invalid credentials (401 Unauthorized)."
            };
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = "Access denied: you do not possess permission for this operation (403 Forbidden)."
            };
        }

        var result = await TryReadApiResponse<T>(response);
        if (result != null)
        {
            return result;
        }

        return new ApiResponse<T>
        {
            Success = response.IsSuccessStatusCode,
            Message = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
        };
    }

    private static async Task<ApiResponse<T>?> TryReadApiResponse<T>(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
