namespace SelfStorageManagementSystem.BusinessLogic.Common;

/// <summary>
/// Standard response envelope for API endpoints.
/// Represents the payload body only and does not contain HTTP status codes or framework types.
/// </summary>
/// <typeparam name="T">Payload data type</typeparam>
public class ApiResponse<T>
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public T? Data { get; set; }

    public object? Errors { get; set; }

    public ApiResponse()
    {
    }

    public ApiResponse(bool success, string message, T? data = default, object? errors = null)
    {
        Success = success;
        Message = message;
        Data = data;
        Errors = errors;
    }

    public static ApiResponse<T> SuccessResponse(T? data, string message = "Success")
    {
        return new ApiResponse<T>(true, message, data);
    }

    public static ApiResponse<T> FailureResponse(string message, object? errors = null, T? data = default)
    {
        return new ApiResponse<T>(false, message, data, errors);
    }
}
