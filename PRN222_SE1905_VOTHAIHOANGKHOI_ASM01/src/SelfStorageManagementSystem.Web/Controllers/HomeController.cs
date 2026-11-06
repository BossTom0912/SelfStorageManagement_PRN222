using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SelfStorageManagementSystem.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// Centralized unhandled exception handler.
    /// Handles requests re-executed by UseExceptionHandler with any HTTP method (GET, POST, PUT, DELETE, etc.).
    /// Explicitly sets Response.StatusCode to 500 to preserve the error status code.
    /// </summary>
    [Route("Home/Error")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        _logger.LogError("Unhandled error handled for RequestId: {RequestId}", requestId);

        return View(new ErrorViewModel
        {
            RequestId = requestId,
            StatusCode = 500,
            Message = "Đã xảy ra lỗi hệ thống trong quá trình xử lý yêu cầu. Vui lòng thử lại sau."
        });
    }

    /// <summary>
    /// Centralized HTTP status code handler (404, 403, etc.).
    /// Handles requests re-executed by UseStatusCodePagesWithReExecute with any HTTP method (GET, POST, PUT, DELETE, etc.).
    /// Explicitly sets Response.StatusCode to the original status code to prevent 405 Method Not Allowed or 200 OK.
    /// </summary>
    [Route("Home/StatusCode/{code?}")]
    [ActionName("StatusCode")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult HandleStatusCode(int? code)
    {
        var statusCode = code ?? 500;
        Response.StatusCode = statusCode;
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        var message = statusCode switch
        {
            404 => "Trang bạn yêu cầu không tồn tại hoặc đã bị di chuyển.",
            403 => "Bạn không có quyền truy cập vào tài nguyên này.",
            401 => "Vui lòng đăng nhập để tiếp tục.",
            _ => "Yêu cầu không thể hoàn tất do lỗi phía máy chủ."
        };

        return View("StatusCode", new ErrorViewModel
        {
            RequestId = requestId,
            StatusCode = statusCode,
            Message = message
        });
    }
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    public int StatusCode { get; set; } = 500;
    public string Message { get; set; } = string.Empty;
}
