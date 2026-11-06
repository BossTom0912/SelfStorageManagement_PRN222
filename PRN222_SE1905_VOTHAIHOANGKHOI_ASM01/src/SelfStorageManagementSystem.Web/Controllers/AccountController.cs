using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.Web.Models;
using CustomerRegisterRequest = SelfStorageManagementSystem.BusinessLogic.DTOs.Requests.Auth.CustomerRegisterRequest;

namespace SelfStorageManagementSystem.Web.Controllers;

/// <summary>
/// Controller xử lý xác thực phiên làm việc Cookie cho ứng dụng Web:
/// - Kế thừa cấu trúc từ TeacherCode (AccountController.cs) và chuẩn hóa bảo mật theo tiêu chuẩn PRN222.
/// - Đăng nhập bằng Cookie 5 phút độc lập với JWT, sliding expiration.
/// - Map UserName sang email BLL; lấy Role từ ClaimsIdentity/Database, tuyệt đối không lấy từ form hay auxiliary cookies.
/// - Login/Register/Logout POST có kiểm tra Antiforgery token; GET Logout không thực hiện thay đổi trạng thái xác thực.
/// - Kiểm tra và chỉ chấp nhận ReturnUrl nội bộ cục bộ (Url.IsLocalUrl) chống lỗi bảo mật Open Redirect.
/// </summary>
public class AccountController : Controller
{
    private readonly IAccountIdentityService _userAccountService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IAccountIdentityService systemUserAccountService,
        ILogger<AccountController> logger)
    {
        _userAccountService = systemUserAccountService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginRequest { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginRequest request, string? returnUrl = null)
    {
        var targetReturnUrl = !string.IsNullOrWhiteSpace(returnUrl)
            ? returnUrl
            : request?.ReturnUrl;

        ViewData["ReturnUrl"] = targetReturnUrl;

        if (request == null || !ModelState.IsValid)
        {
            return View(request ?? new LoginRequest());
        }

        try
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers.UserAgent.ToString();

            // Map UserName sang email cho BLL Core AuthenticateAsync
            var account = await _userAccountService.AuthenticateAsync(
                request.UserName,
                request.Password,
                ipAddress,
                userAgent,
                HttpContext.RequestAborted);

            if (account != null)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, account.UserId.ToString()),
                    new(ClaimTypes.Name, account.DisplayName),
                    new(ClaimTypes.Email, account.Email)
                };

                if (!string.IsNullOrWhiteSpace(account.UserType))
                {
                    claims.Add(new Claim("user_type", account.UserType));
                }

                if (!string.IsNullOrWhiteSpace(account.EmployeeCode))
                {
                    claims.Add(new Claim("employee_code", account.EmployeeCode));
                }

                // Gán toàn bộ mã vai trò thật (Real Role Codes)
                foreach (var role in account.Roles)
                {
                    if (!string.IsNullOrWhiteSpace(role))
                    {
                        claims.Add(new Claim(ClaimTypes.Role, role));
                    }
                }

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = request.RememberMe,
                    IssuedUtc = DateTimeOffset.UtcNow,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                    AllowRefresh = true
                };

                // Thiết lập phiên đăng nhập an toàn bằng Cookie Authentication
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    authProperties);

                // AN TOÀN BẢO MẬT: Tuyệt đối KHÔNG gán cookie Role phụ (Response.Cookies.Append("Role", ...))
                // Quyền hạn được bảo vệ nghiêm ngặt bên trong Cookie xác thực đã được mã hóa của ASP.NET Core.

                _logger.LogInformation("Người dùng {Email} (ID: {UserId}) đã đăng nhập thành công qua Cookie.", account.Email, account.UserId);
                TempData["SuccessMessage"] = $"Đăng nhập thành công! Chào mừng {account.DisplayName}.";

                // Kiểm tra ReturnUrl: Chỉ chấp nhận đường dẫn nội bộ cục bộ chống Open Redirect
                if (!string.IsNullOrWhiteSpace(targetReturnUrl) && Url.IsLocalUrl(targetReturnUrl))
                {
                    return Redirect(targetReturnUrl);
                }

                return RedirectToAction("Index", "Home");
            }
        }
        catch (UnauthorizedException ex)
        {
            _logger.LogWarning(ex, "Đăng nhập thất bại cho tên tài khoản: {UserName}", request.UserName);
            ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không chính xác, hoặc tài khoản đã bị khóa/vô hiệu hóa.");
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Lỗi kiểm tra tính hợp lệ khi đăng nhập cho {UserName}: {Message}", request.UserName, ex.Message);
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Đã xảy ra lỗi ngoài dự kiến khi xử lý đăng nhập cho {UserName}", request.UserName);
            ModelState.AddModelError(string.Empty, "Đã xảy ra lỗi hệ thống khi xử lý yêu cầu. Vui lòng thử lại sau.");
        }

        // Luôn await SignOutAsync khi thất bại để dọn sạch phiên dở dang
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return View(request);
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new RegisterViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        var targetReturnUrl = !string.IsNullOrWhiteSpace(returnUrl)
            ? returnUrl
            : model?.ReturnUrl;

        ViewData["ReturnUrl"] = targetReturnUrl;

        if (model == null || !ModelState.IsValid)
        {
            return View(model ?? new RegisterViewModel());
        }

        try
        {
            var request = new CustomerRegisterRequest
            {
                Email = model.Email.Trim(),
                Password = model.Password,
                FullName = model.FullName.Trim(),
                PhoneNumber = model.PhoneNumber?.Trim(),
                IdentityNumber = model.IdentityNumber?.Trim(),
                DateOfBirth = model.DateOfBirth,
                Address = model.Address?.Trim(),
                EmergencyContactName = model.EmergencyContactName?.Trim(),
                EmergencyContactPhone = model.EmergencyContactPhone?.Trim()
            };

            var response = await _userAccountService.RegisterCustomerAsync(
                request,
                HttpContext.RequestAborted);

            _logger.LogInformation("Khách hàng mới đã đăng ký thành công: {Email} (ID: {UserId})", response.Email, response.UserId);
            TempData["SuccessMessage"] = $"Đăng ký tài khoản thành công cho {response.FullName}! Quý khách có thể đăng nhập ngay bây giờ.";

            return RedirectToAction(nameof(Login), new { returnUrl = targetReturnUrl });
        }
        catch (BadRequestException ex)
        {
            _logger.LogWarning(ex, "Lỗi kiểm tra dữ liệu đăng ký khách hàng cho {Email}: {Message}", model.Email, ex.Message);
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (ConflictException ex)
        {
            _logger.LogWarning(ex, "Xung đột tài khoản khi đăng ký {Email}: {Message}", model.Email, ex.Message);
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi đăng ký tài khoản khách hàng cho {Email}", model.Email);
            ModelState.AddModelError(string.Empty, "Đã xảy ra lỗi khi tạo tài khoản. Vui lòng kiểm tra lại thông tin và thử lại.");
        }

        return View(model);
    }

    /// <summary>
    /// GET Logout KHÔNG thực hiện thay đổi trạng thái đăng nhập để chống CSRF Logout.
    /// Hiển thị trang xác nhận hoặc chuyển hướng an toàn.
    /// </summary>
    [HttpGet]
    public IActionResult Logout()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction(nameof(Login));
        }

        return View();
    }

    /// <summary>
    /// POST Logout bắt buộc Antiforgery Token để hủy phiên đăng nhập an toàn.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(string? returnUrl = null)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        TempData["InfoMessage"] = "Quý khách đã đăng xuất an toàn khỏi hệ thống.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Forbidden()
    {
        return View();
    }

    /// <summary>
    /// Trang thông tin hồ sơ tài khoản cá nhân yêu cầu phiên xác thực Cookie.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(userIdStr, out var userId))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        var currentUser = await _userAccountService.GetCurrentUserAsync(userId, HttpContext.RequestAborted);
        if (currentUser == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        return View(currentUser);
    }

    /// <summary>
    /// Endpoint dữ liệu bảo mật phục vụ kiểm tra AJAX xác thực và SignalR handler.
    /// </summary>
    [Authorize]
    [HttpGet]
    public IActionResult SecuredData()
    {
        return Json(new
        {
            status = "success",
            userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            name = User.Identity?.Name,
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()
        });
    }

    /// <summary>
    /// Endpoint quản trị hệ thống phục vụ kiểm tra phân quyền vai trò system_administrator.
    /// </summary>
    [Authorize(Roles = RoleConstants.SystemAdministrator)]
    [HttpGet]
    public IActionResult AdminCheck()
    {
        return Json(new
        {
            status = "authorized",
            message = "System administrator access verified.",
            adminUser = User.Identity?.Name
        });
    }
}
