using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SelfStorageManagementSystem.Web.Extensions;

/// <summary>
/// T16: Middleware bảo vệ nguồn gốc (Origin Security Gate) cho Realtime SignalR Hub (/storageUnitHub).
/// Tuân thủ nghiêm ngặt đặc tả cùng nguồn (Same-Origin Policy - RFC 6454):
/// - So sánh chính xác bộ 3 (Scheme, Host, Effective Port) giữa Origin header và request đích.
/// - Không cho phép ngoại lệ chéo giữa localhost và 127.0.0.1 (khác biệt định danh host).
/// - Từ chối khi sai scheme (http vs https), sai host (bao gồm loopback alias), hoặc sai port.
/// - Cho phép các client trực tiếp / phi trình duyệt không mang header Origin (RFC 6455).
/// - Từ chối dứt điểm (403 Forbidden) các yêu cầu Cross-Origin trên cả HTTP Negotiate và WebSocket handshake.
/// </summary>
public static class HubOriginSecurityExtensions
{
    public const string HubPath = "/storageUnitHub";

    public static IApplicationBuilder UseStorageUnitHubOriginRestriction(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(HubPath))
            {
                var origin = context.Request.Headers.Origin.ToString();
                if (!string.IsNullOrWhiteSpace(origin) && !IsAllowedHubOrigin(context.Request, origin))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync("{\"statusCode\":403,\"message\":\"Cross-origin access to /storageUnitHub is forbidden.\"}");
                    return;
                }
            }

            await next();
        });
    }

    public static bool IsAllowedHubOrigin(HttpRequest request, string origin)
    {
        // Cho phép các client trực tiếp không mang header Origin (non-browser clients, desktop app, worker nội bộ)
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        // Tính toán effective port chuẩn xác theo RFC 6454
        int requestEffectivePort = request.Host.Port ?? (string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        int originEffectivePort = originUri.IsDefaultPort
            ? (string.Equals(originUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : originUri.Port;

        // So khớp chính xác toàn bộ bộ ba (Scheme, Host, Effective Port)
        bool schemeMatches = string.Equals(originUri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase);
        bool hostMatches = string.Equals(originUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase);
        bool portMatches = originEffectivePort == requestEffectivePort;

        return schemeMatches && hostMatches && portMatches;
    }
}
