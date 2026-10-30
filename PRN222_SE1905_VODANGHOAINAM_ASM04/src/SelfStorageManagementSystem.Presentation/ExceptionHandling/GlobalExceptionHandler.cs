using Microsoft.AspNetCore.Diagnostics;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;

namespace SelfStorageManagementSystem.Presentation.ExceptionHandling;

/// <summary>
/// Centralized exception handler conforming to .NET 8 IExceptionHandler pattern.
/// Maps domain/application exceptions to appropriate HTTP status codes and formats
/// error responses using the uniform ApiResponse envelope without leaking sensitive details.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (httpContext.Response.HasStarted)
        {
            _logger.LogWarning("Response has already started, cannot write error response for {Path}", httpContext.Request.Path);
            return false;
        }

        // Handle client cancellation cleanly without treating as server failure
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Request was cancelled by the client: {Path}", httpContext.Request.Path);
            // The client has disconnected; do not attempt to write a response.
            return true;
        }

        var (statusCode, message) = exception switch
        {
            BadRequestException badRequest => (StatusCodes.Status400BadRequest, badRequest.Message),
            UnauthorizedException unauthorized => (StatusCodes.Status401Unauthorized, unauthorized.Message),
            ForbiddenException forbidden => (StatusCodes.Status403Forbidden, forbidden.Message),
            NotFoundException notFound => (StatusCodes.Status404NotFound, notFound.Message),
            ConflictException conflict => (StatusCodes.Status409Conflict, conflict.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception occurred while processing request {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                "Application exception ({StatusCode}) on {Method} {Path}: {Message}",
                statusCode,
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.Message);
        }

        var response = new ApiResponse<object?>
        {
            Success = false,
            Message = message,
            Data = null,
            Errors = null
        };

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
        return true;
    }
}
