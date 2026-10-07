using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SelfStorageManagementSystem.WpfClient.Models;

namespace SelfStorageManagementSystem.WpfClient.Services;

public class ApiClient
{
    public const string CertificateUntrustedErrorMessage =
        "The SSL/TLS certificate is not trusted. If you are developing locally, please trust the development certificate by running: 'dotnet dev-certs https --trust' in your terminal.";

    public const string InsecureOrInvalidUrlErrorMessage =
        "Insecure or invalid API URL: The API URL must be an absolute URL using HTTPS. HTTP or relative URLs are not permitted.";

    private static readonly Lazy<ApiClient> _instance = new(() => new ApiClient());
    public static ApiClient Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string BaseUrl { get; set; } = "https://localhost:7031";

    public event Action? SessionExpired;

    public ApiClient() : this(new HttpClient())
    {
    }

    public ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public ApiClient(HttpMessageHandler handler, bool disposeHandler = true)
        : this(new HttpClient(handler, disposeHandler))
    {
    }

    public static bool IsCertificateTrustException(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            if (current is System.Security.Authentication.AuthenticationException)
            {
                return true;
            }

            var message = current.Message;
            if (message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("PKIX", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("remote certificate is invalid", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("untrusted", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    /// <summary>
    /// Centralized URL validation ensuring only absolute HTTPS endpoints are allowed.
    /// Blocks network traffic before any transmission if the URL is insecure or invalid.
    /// </summary>
    public ApiResponse<T>? ValidateBaseUrl<T>(out Uri? validUri)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) ||
            !Uri.TryCreate(BaseUrl, UriKind.Absolute, out validUri) ||
            !string.Equals(validUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            validUri = null;
            return new ApiResponse<T>
            {
                Success = false,
                Message = InsecureOrInvalidUrlErrorMessage
            };
        }

        return null;
    }

    /// <summary>
    /// Centralized dispatch for all HTTP operations ensuring HTTPS enforcement, per-request authorization headers,
    /// certificate trust error handling, proper disposal, and uniform response extraction.
    /// </summary>
    private async Task<ApiResponse<T>> SendRequestAsync<T>(
        HttpMethod method,
        string relativePathAndQuery,
        object? jsonBody = null,
        bool requiresAuth = true)
    {
        var validationError = ValidateBaseUrl<T>(out var validUri);
        if (validationError != null)
        {
            return validationError;
        }

        try
        {
            var cleanBaseUrl = validUri!.ToString().TrimEnd('/');
            var requestUri = $"{cleanBaseUrl}/" + relativePathAndQuery.TrimStart('/');

            using var request = new HttpRequestMessage(method, requestUri);

            if (jsonBody != null)
            {
                if (jsonBody is HttpContent httpContent)
                {
                    request.Content = httpContent;
                }
                else
                {
                    request.Content = JsonContent.Create(jsonBody);
                }
            }

            if (requiresAuth && !string.IsNullOrWhiteSpace(SessionStore.AccessToken))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", SessionStore.AccessToken);
            }

            using var response = await _httpClient.SendAsync(request);
            return await HandleResponseAsync<T>(response);
        }
        catch (HttpRequestException ex) when (IsCertificateTrustException(ex))
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = CertificateUntrustedErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = "Connection error: " + ex.Message
            };
        }
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(string email, string password)
    {
        var request = new LoginModel
        {
            Email = email,
            Password = password
        };

        var result = await SendRequestAsync<LoginResponse>(
            HttpMethod.Post,
            "api/auth/login",
            request,
            requiresAuth: false);

        if (result.Success && result.Data != null)
        {
            SessionStore.SetSession(result.Data.AccessToken, result.Data.ExpiresAt);
        }

        return result;
    }

    public Task<ApiResponse<CustomerRegisterResponse>> RegisterCustomerAsync(CustomerRegisterModel model)
    {
        return SendRequestAsync<CustomerRegisterResponse>(
            HttpMethod.Post,
            "api/auth/register-customer",
            model,
            requiresAuth: false);
    }

    public async Task<ApiResponse<CurrentUserResponse>> GetMeAsync()
    {
        var result = await SendRequestAsync<CurrentUserResponse>(
            HttpMethod.Get,
            "api/auth/me",
            jsonBody: null,
            requiresAuth: true);

        if (result.Success && result.Data != null)
        {
            SessionStore.SetCurrentUser(result.Data);
        }

        return result;
    }

    public Task<ApiResponse<PagedResult<UserAccountModel>>> GetAccountsAsync(
        string? searchTerm,
        string? status,
        string? roleCode,
        int pageNumber,
        int pageSize)
    {
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
        return SendRequestAsync<PagedResult<UserAccountModel>>(
            HttpMethod.Get,
            $"api/admin/accounts?{queryString}",
            jsonBody: null,
            requiresAuth: true);
    }

    public Task<ApiResponse<UserAccountModel>> CreateStaffAccountAsync(CreateStaffModel model)
    {
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

        return SendRequestAsync<UserAccountModel>(
            HttpMethod.Post,
            "api/admin/accounts/staff",
            payload,
            requiresAuth: true);
    }

    public Task<ApiResponse<UserAccountModel>> UpdateUserStatusAsync(long userId, string status)
    {
        var payload = new { status };
        return SendRequestAsync<UserAccountModel>(
            HttpMethod.Patch,
            $"api/admin/accounts/{userId}/status",
            payload,
            requiresAuth: true);
    }

    public Task<ApiResponse<UserAccountModel>> ManageUserRolesAsync(long userId, List<string> roleCodes)
    {
        var payload = new { roleCodes };
        return SendRequestAsync<UserAccountModel>(
            HttpMethod.Post,
            $"api/admin/accounts/{userId}/roles",
            payload,
            requiresAuth: true);
    }

    public Task<ApiResponse<FacilityAssignmentModel>> AssignFacilityAsync(
        long userId,
        long facilityId,
        string assignmentRole,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt)
    {
        var payload = new
        {
            facilityId,
            assignmentRole,
            startsAt,
            endsAt
        };

        return SendRequestAsync<FacilityAssignmentModel>(
            HttpMethod.Post,
            $"api/admin/accounts/{userId}/facility-assignments",
            payload,
            requiresAuth: true);
    }

    public Task<ApiResponse<object?>> TerminateFacilityAssignmentAsync(long assignmentId)
    {
        return SendRequestAsync<object?>(
            HttpMethod.Post,
            $"api/admin/accounts/facility-assignments/{assignmentId}/terminate",
            jsonBody: null,
            requiresAuth: true);
    }

    public Task<ApiResponse<List<FacilityLookupModel>>> GetFacilitiesAsync()
    {
        return SendRequestAsync<List<FacilityLookupModel>>(
            HttpMethod.Get,
            "api/admin/facilities",
            jsonBody: null,
            requiresAuth: true);
    }

    public Task<ApiResponse<PagedResult<FacilityCatalogModel>>> GetCatalogFacilitiesAsync(
        string? city,
        string? district,
        string? searchTerm,
        int pageNumber,
        int pageSize)
    {
        var queryParams = new List<string>
        {
            $"pageNumber={pageNumber}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrWhiteSpace(city))
            queryParams.Add($"city={Uri.EscapeDataString(city)}");
        if (!string.IsNullOrWhiteSpace(district))
            queryParams.Add($"district={Uri.EscapeDataString(district)}");
        if (!string.IsNullOrWhiteSpace(searchTerm))
            queryParams.Add($"searchTerm={Uri.EscapeDataString(searchTerm)}");

        var queryString = string.Join("&", queryParams);
        return SendRequestAsync<PagedResult<FacilityCatalogModel>>(
            HttpMethod.Get,
            $"api/facilities?{queryString}",
            jsonBody: null,
            requiresAuth: false);
    }

    public Task<ApiResponse<List<FacilityUnitTypeCatalogModel>>> GetFacilityUnitTypesAsync(
        long facilityId,
        DateOnly? startDate,
        DateOnly? endDate,
        decimal? maxPrice,
        bool? climateControlled,
        decimal? minAreaM2 = null,
        decimal? maxAreaM2 = null)
    {
        var queryParams = new List<string>();
        if (startDate.HasValue)
            queryParams.Add($"rentalStartDate={startDate.Value:yyyy-MM-dd}");
        if (endDate.HasValue)
            queryParams.Add($"rentalEndDate={endDate.Value:yyyy-MM-dd}");
        if (maxPrice.HasValue)
            queryParams.Add($"maxPrice={maxPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (minAreaM2.HasValue)
            queryParams.Add($"minAreaM2={minAreaM2.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (maxAreaM2.HasValue)
            queryParams.Add($"maxAreaM2={maxAreaM2.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (climateControlled.HasValue)
            queryParams.Add($"climateControlled={climateControlled.Value}");

        var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;
        return SendRequestAsync<List<FacilityUnitTypeCatalogModel>>(
            HttpMethod.Get,
            $"api/facilities/{facilityId}/unit-types{queryString}",
            jsonBody: null,
            requiresAuth: false);
    }

    public Task<ApiResponse<PagedResult<AvailableStorageUnitModel>>> GetAvailableUnitsAsync(
        long facilityId,
        long? unitTypeId,
        long? areaId,
        bool? climateControlled,
        decimal? maxPrice,
        DateOnly? startDate,
        DateOnly? endDate,
        int pageNumber,
        int pageSize)
    {
        return GetAvailableUnitsAsync(
            facilityId,
            unitTypeId,
            areaId,
            climateControlled,
            maxPrice,
            null,
            null,
            startDate,
            endDate,
            pageNumber,
            pageSize);
    }

    public Task<ApiResponse<PagedResult<AvailableStorageUnitModel>>> GetAvailableUnitsAsync(
        long facilityId,
        long? unitTypeId,
        long? areaId,
        bool? climateControlled,
        decimal? maxPrice,
        decimal? minAreaM2,
        decimal? maxAreaM2,
        DateOnly? startDate,
        DateOnly? endDate,
        int pageNumber,
        int pageSize)
    {
        var queryParams = new List<string>
        {
            $"pageNumber={pageNumber}",
            $"pageSize={pageSize}"
        };

        if (unitTypeId.HasValue)
            queryParams.Add($"unitTypeId={unitTypeId.Value}");
        if (areaId.HasValue)
            queryParams.Add($"areaId={areaId.Value}");
        if (climateControlled.HasValue)
            queryParams.Add($"climateControlled={climateControlled.Value}");
        if (maxPrice.HasValue)
            queryParams.Add($"maxPrice={maxPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (minAreaM2.HasValue)
            queryParams.Add($"minAreaM2={minAreaM2.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (maxAreaM2.HasValue)
            queryParams.Add($"maxAreaM2={maxAreaM2.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (startDate.HasValue)
            queryParams.Add($"rentalStartDate={startDate.Value:yyyy-MM-dd}");
        if (endDate.HasValue)
            queryParams.Add($"rentalEndDate={endDate.Value:yyyy-MM-dd}");

        var queryString = string.Join("&", queryParams);
        return SendRequestAsync<PagedResult<AvailableStorageUnitModel>>(
            HttpMethod.Get,
            $"api/facilities/{facilityId}/units/available?{queryString}",
            jsonBody: null,
            requiresAuth: false);
    }

    public Task<ApiResponse<FacilityFloorMapModel>> GetFacilityFloorMapAsync(
        long facilityId,
        long? areaId,
        DateOnly? startDate,
        DateOnly? endDate)
    {
        var queryParams = new List<string>();
        if (areaId.HasValue)
            queryParams.Add($"areaId={areaId.Value}");
        if (startDate.HasValue)
            queryParams.Add($"rentalStartDate={startDate.Value:yyyy-MM-dd}");
        if (endDate.HasValue)
            queryParams.Add($"rentalEndDate={endDate.Value:yyyy-MM-dd}");

        var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;
        return SendRequestAsync<FacilityFloorMapModel>(
            HttpMethod.Get,
            $"api/facilities/{facilityId}/floor-map{queryString}",
            jsonBody: null,
            requiresAuth: false);
    }

    public Task<ApiResponse<ReservationDetailClientModel>> CreateReservationHoldAsync(
        CreateReservationClientRequest request)
    {
        return SendRequestAsync<ReservationDetailClientModel>(
            HttpMethod.Post,
            "api/reservations",
            jsonBody: request,
            requiresAuth: true);
    }

    public Task<ApiResponse<ReservationDetailClientModel>> GetReservationByIdAsync(long id)
    {
        return SendRequestAsync<ReservationDetailClientModel>(
            HttpMethod.Get,
            $"api/reservations/{id}",
            jsonBody: null,
            requiresAuth: true);
    }

    public Task<ApiResponse<PagedResult<ReservationListItemClientModel>>> GetMyReservationsAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string? status = null)
    {
        var queryParams = new List<string>
        {
            $"pageNumber={pageNumber}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrWhiteSpace(status))
        {
            queryParams.Add($"status={Uri.EscapeDataString(status.Trim())}");
        }

        var queryString = string.Join("&", queryParams);
        return SendRequestAsync<PagedResult<ReservationListItemClientModel>>(
            HttpMethod.Get,
            $"api/reservations/mine?{queryString}",
            jsonBody: null,
            requiresAuth: true);
    }

    public Task<ApiResponse<ReservationDetailClientModel>> CancelReservationAsync(
        long id,
        string? reason = null)
    {
        var body = new CancelReservationClientRequest { Reason = reason };
        return SendRequestAsync<ReservationDetailClientModel>(
            HttpMethod.Post,
            $"api/reservations/{id}/cancel",
            jsonBody: body,
            requiresAuth: true);
    }

    public void Logout()
    {
        SessionStore.Clear();
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
