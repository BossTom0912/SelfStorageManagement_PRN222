using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

namespace SelfStorageManagementSystem.Web.Extensions;

/// <summary>
/// Cấu hình Cookie Authentication độc lập và an toàn cho Web App:
/// - Cookie HttpOnly, Secure Always, SameSite=Lax, thời hạn 5 phút, sliding expiration.
/// - ValidatePrincipal đọc snapshot identity mới từ IAccountIdentityService: từ chối user disabled, cập nhật role & scope hiện hành.
/// - AJAX và SignalR negotiate nhận 401/403 (JSON) thay vì bị chuyển hướng HTML sang /Account/Login hoặc /Account/Forbidden.
/// </summary>
public static class CookieAuthExtensions
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string CookieName = "SelfStoragePRN222.Web.Auth";
    public const string LoginPath = "/Account/Login";
    public const string AccessDeniedPath = "/Account/Forbidden";

    public static IServiceCollection AddWebCookieAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieScheme)
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.LoginPath = LoginPath;
                options.AccessDeniedPath = AccessDeniedPath;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = true;

                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = async context =>
                    {
                        var principal = context.Principal;
                        if (principal == null)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieScheme);
                            return;
                        }

                        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (!long.TryParse(userIdStr, out var userId))
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieScheme);
                            return;
                        }

                        var accountIdentityService = context.HttpContext.RequestServices.GetRequiredService<IAccountIdentityService>();
                        var currentUser = await accountIdentityService.GetCurrentUserAsync(userId, context.HttpContext.RequestAborted);

                        var isEmploymentInactive = currentUser?.EmploymentStatus != null &&
                            !string.Equals(currentUser.EmploymentStatus, EmploymentStatusConstants.Active, StringComparison.OrdinalIgnoreCase);

                        // Từ chối và hủy phiên nếu người dùng không tồn tại, trạng thái tài khoản khác active, hoặc hồ sơ nhân sự không active
                        if (currentUser == null ||
                            currentUser.Status != UserStatusConstants.Active ||
                            isEmploymentInactive)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieScheme);
                            return;
                        }

                        // Kiểm tra và đồng bộ vai trò (Roles) cùng phạm vi cơ sở (Facility Scopes) mới nhất
                        if (principal.Identity is ClaimsIdentity identity)
                        {
                            var existingRoleClaims = identity.FindAll(ClaimTypes.Role).ToList();
                            var existingRoles = existingRoleClaims.Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                            var newRoles = currentUser.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

                            var existingFacilityClaims = identity.FindAll("facility_scope").ToList();
                            var existingFacilities = existingFacilityClaims.Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                            var newFacilities = currentUser.ActiveAssignments.Select(a => a.FacilityId.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);

                            var existingName = identity.FindFirst(ClaimTypes.Name)?.Value;
                            var existingEmail = identity.FindFirst(ClaimTypes.Email)?.Value;

                            bool rolesChanged = !existingRoles.SetEquals(newRoles);
                            bool scopesChanged = !existingFacilities.SetEquals(newFacilities);
                            bool infoChanged = existingName != currentUser.DisplayName || existingEmail != currentUser.Email;

                            if (rolesChanged || scopesChanged || infoChanged)
                            {
                                // Xóa claims vai trò cũ và nạp danh sách vai trò mới nhất
                                foreach (var rc in existingRoleClaims)
                                {
                                    identity.RemoveClaim(rc);
                                }
                                foreach (var role in currentUser.Roles)
                                {
                                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                                }

                                // Xóa claims phạm vi cơ sở cũ và nạp danh sách cơ sở phân công mới nhất
                                foreach (var fc in existingFacilityClaims)
                                {
                                    identity.RemoveClaim(fc);
                                }
                                foreach (var assignment in currentUser.ActiveAssignments)
                                {
                                    identity.AddClaim(new Claim("facility_scope", assignment.FacilityId.ToString()));
                                }

                                // Cập nhật Tên hiển thị và Email nếu có thay đổi
                                if (existingName != currentUser.DisplayName)
                                {
                                    var nc = identity.FindFirst(ClaimTypes.Name);
                                    if (nc != null) identity.RemoveClaim(nc);
                                    identity.AddClaim(new Claim(ClaimTypes.Name, currentUser.DisplayName));
                                }
                                if (existingEmail != currentUser.Email)
                                {
                                    var ec = identity.FindFirst(ClaimTypes.Email);
                                    if (ec != null) identity.RemoveClaim(ec);
                                    identity.AddClaim(new Claim(ClaimTypes.Email, currentUser.Email));
                                }

                                // Đánh dấu cập nhật cookie để browser nhận phiên mới nhất
                                context.ShouldRenew = true;
                            }
                        }
                    },

                    OnRedirectToLogin = async context =>
                    {
                        if (IsAjaxOrApiOrSignalR(context.Request))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync("{\"statusCode\":401,\"message\":\"Yêu cầu xác thực phiên đăng nhập (Unauthorized).\"}");
                            return;
                        }

                        context.Response.Redirect(context.RedirectUri);
                    },

                    OnRedirectToAccessDenied = async context =>
                    {
                        if (IsAjaxOrApiOrSignalR(context.Request))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsync("{\"statusCode\":403,\"message\":\"Từ chối quyền truy cập tài nguyên (Forbidden).\"}");
                            return;
                        }

                        context.Response.Redirect(context.RedirectUri);
                    }
                };
            });

        return services;
    }

    /// <summary>
    /// Nhận diện yêu cầu từ AJAX, SignalR Hub negotiate/invocations, hoặc Web API.
    /// </summary>
    public static bool IsAjaxOrApiOrSignalR(HttpRequest request)
    {
        if (request == null) return false;

        // Header XMLHttpRequest từ AJAX / jQuery
        if (string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Header Accept mong muốn định dạng JSON
        var accept = request.Headers["Accept"].ToString();
        if (!string.IsNullOrEmpty(accept) && accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Đường dẫn SignalR Hub (negotiate, ping, websocket upgrade)
        if (request.Path.StartsWithSegments("/storageUnitHub", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Đường dẫn API (nếu có)
        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
