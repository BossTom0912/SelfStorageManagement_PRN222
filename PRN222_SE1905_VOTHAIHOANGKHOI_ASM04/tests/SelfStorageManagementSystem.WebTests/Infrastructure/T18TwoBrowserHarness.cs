using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Services.Implementations;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Hubs;
using SelfStorageManagementSystem.Web.Realtime;
using SelfStorageManagementSystem.Web.Workers;

namespace SelfStorageManagementSystem.WebTests.Infrastructure;

/// <summary>
/// Dedicated E2E Two-Browser Test Harness for Task T18.
/// Hosts the full ASP.NET Core Web Application on a real dynamic Kestrel port with real HTTP & WebSocket transports.
/// Connects to the verified dedicated E2E database (SelfStoragePRN222_WebE2eTest), with automated resilient in-memory fallback.
/// Launches and coordinates TWO completely isolated Microsoft Edge browser contexts (with distinct user-data-dirs,
/// separate CDP WebSocket debugger endpoints, and independent cookie jars).
/// Provides real-time network, console, WebSocket monitoring, and sanitized screenshot evidence capturing.
/// </summary>
public sealed class T18TwoBrowserHarness : IAsyncDisposable
{
    private readonly WebApplication _app;
    public string ServerUrl { get; }
    public string DatabaseName { get; }
    public bool IsSqlServer { get; }
    public IServiceProvider Services => _app.Services;

    public T18BrowserContext Context1 { get; private set; } = null!;
    public T18BrowserContext Context2 { get; private set; } = null!;

    private static readonly Regex SecretSanitizerRegex = new(
        @"(password|pin|hashSecret|token|secret|hmac)[=:\s""']+([A-Za-z0-9_\-\+\/]{4,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return SecretSanitizerRegex.Replace(text, "$1=[REDACTED]");
    }

    private T18TwoBrowserHarness(WebApplication app, string serverUrl, string dbName, bool isSqlServer)
    {
        _app = app;
        ServerUrl = serverUrl;
        DatabaseName = dbName;
        IsSqlServer = isSqlServer;
    }

    public static async Task<T18TwoBrowserHarness> StartAsync(string? explicitDbName = null)
    {
        var targetDb = explicitDbName ?? "SelfStoragePRN222_WebE2eTest";
        var sqlConnStr = $"Server=(localdb)\\mssqllocaldb;Database={targetDb};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        // Strictly verify connection to the dedicated SQL Server database; In-Memory fallback is prohibited
        var testOptions = new DbContextOptionsBuilder<SelfStorageDbContext>()
            .UseSqlServer(sqlConnStr)
            .Options;

        using (var testDb = new SelfStorageDbContext(testOptions))
        {
            if (!await testDb.Database.CanConnectAsync())
            {
                throw new InvalidOperationException(
                    $"Dedicated SQL Server database '{targetDb}' on (localdb)\\mssqllocaldb could not be reached. " +
                    "T18 E2E tests strictly require a real SQL Server database; In-Memory fallback is disallowed.");
            }
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "../../../../../src/SelfStorageManagementSystem.Web")),
            Path.GetFullPath(Path.Combine(baseDir, "../../../../src/SelfStorageManagementSystem.Web")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src/SelfStorageManagementSystem.Web")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../src/SelfStorageManagementSystem.Web"))
        };
        var webRoot = candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException($"Could not locate SelfStorageManagementSystem.Web directory. Checked: {string.Join(", ", candidates)}");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = webRoot,
            WebRootPath = Path.Combine(webRoot, "wwwroot"),
            EnvironmentName = "Testing",
            Args = Array.Empty<string>()
        });

        // 1. Kestrel on dynamic loopback port
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0);
        });

        // Configure connection string to dedicated E2E database and test settings
        builder.Configuration["ConnectionStrings:DefaultConnection"] = sqlConnStr;
        builder.Configuration["Payment:AllowDemoSimulator"] = "true";
        builder.Configuration["ReservationWorker:Enabled"] = "false";
        builder.Configuration["CheckIn:PinHmacSecret"] = "IntegrationTestPinSecretMustBeLongEnough123456789!";

        // 2. Register BLL & Core Services
        builder.Services.AddSelfStorageWebComposition(builder.Configuration);

        builder.Services.RemoveAll(typeof(DbContextOptions<SelfStorageDbContext>));
        builder.Services.RemoveAll(typeof(SelfStorageDbContext));

        builder.Services.AddDbContext<SelfStorageDbContext>(options =>
        {
            options.UseSqlServer(sqlConnStr);
        });

        // T18 Finding 1: Register test password hasher that authenticates seed password "TestPass123!" against "external-auth-demo-only"
        // without mutating core.users table in the dedicated E2E database.
        builder.Services.RemoveAll(typeof(IPasswordHasher));
        builder.Services.AddSingleton<IPasswordHasher, T18TestPasswordHasher>();

        // 3. Controllers, Views, Razor Pages, SignalR
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(SelfStorageManagementSystem.Web.Pages.StorageUnits.IndexModel).Assembly);
        builder.Services.AddRazorPages()
            .AddApplicationPart(typeof(SelfStorageManagementSystem.Web.Pages.StorageUnits.IndexModel).Assembly);
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IStorageUnitHubConnectionTracker, StorageUnitHubConnectionTracker>();
        builder.Services.AddScoped<StorageUnitRealtimePublisher>();
        builder.Services.AddScoped<IStorageUnitRealtimePublisher>(sp => sp.GetRequiredService<StorageUnitRealtimePublisher>());
        builder.Services.AddScoped<IWebRealtimePublisher>(sp => sp.GetRequiredService<StorageUnitRealtimePublisher>());
        builder.Services.AddScoped<StorageUnitHubScopeRevocationService>();
        builder.Services.AddScoped<IUserScopeChangedNotifier>(sp => sp.GetRequiredService<StorageUnitHubScopeRevocationService>());
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton(HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All));

        // 4. Background Workers & Realtime Hook
        builder.Services.AddScoped<IReservationExpirationHook, ReservationExpirationRealtimeHook>();

        // 5. Cookie Auth & Policies
        builder.Services.AddWebCookieAuthentication();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("CustomerOnly", policy => policy.RequireRole(RoleConstants.StorageCustomer));
            options.AddPolicy("StaffOnly", policy => policy.RequireRole(
                RoleConstants.FacilityStaff,
                RoleConstants.FacilityManager,
                RoleConstants.BusinessOperationsManager,
                RoleConstants.SystemAdministrator));
            options.AddPolicy("ManagerOnly", policy => policy.RequireRole(
                RoleConstants.FacilityManager,
                RoleConstants.BusinessOperationsManager,
                RoleConstants.SystemAdministrator));
            options.AddPolicy("AdminOnly", policy => policy.RequireRole(RoleConstants.SystemAdministrator));
        });

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "SelfStoragePRN222.Web.Antiforgery.T18";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        var app = builder.Build();

        app.UseStaticFiles();
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        // Test login endpoint allowing headless browser to acquire real authentication cookie
        app.MapGet("/test-auth-login", async (HttpContext ctx, string? role, long? userId, string? name) =>
        {
            var userRole = role ?? RoleConstants.FacilityManager;
            var uid = userId ?? 1001L;
            var userName = name ?? $"User_{uid}";

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, uid.ToString()),
                new Claim(ClaimTypes.Name, userName),
                new Claim(ClaimTypes.Role, userRole),
                new Claim(ClaimTypes.Email, $"{userName.ToLowerInvariant()}@example.test"),
                new Claim("UserStatus", UserStatusConstants.Active)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Ok(new { success = true, role = userRole, userId = uid });
        });

        app.MapHub<StorageUnitHub>("/storageUnitHub");
        app.MapDefaultControllerRoute();
        app.MapRazorPages();
        app.MapControllers();

        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        var serverUrl = addresses!.Addresses.First();

        var harness = new T18TwoBrowserHarness(app, serverUrl, targetDb, true);
        return harness;
    }

    public async Task InitializeBrowserSessionsAsync(
        string role1 = RoleConstants.FacilityManager,
        long userId1 = 4L,
        string role2 = RoleConstants.StorageCustomer,
        long userId2 = 1L)
    {
        Context1 = await T18BrowserContext.LaunchAsync("Context1_Staff", role1, userId1, ServerUrl);
        Context2 = await T18BrowserContext.LaunchAsync("Context2_Customer", role2, userId2, ServerUrl);
    }

    public IServiceScope CreateScope() => _app.Services.CreateScope();

    public async ValueTask DisposeAsync()
    {
        if (Context1 != null)
        {
            try { await Context1.DisposeAsync(); } catch { }
        }
        if (Context2 != null)
        {
            try { await Context2.DisposeAsync(); } catch { }
        }

        try
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        catch { }
    }
}

/// <summary>
/// Encapsulates an independent Microsoft Edge browser instance controlled via Chrome DevTools Protocol (CDP).
/// Maintains an isolated temporary user-data-dir, independent cookies, and separate WebSocket connection to CDP.
/// Captures sanitized screenshots, network responses, and console events.
/// </summary>
public sealed class T18BrowserContext : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _tempDir;
    private readonly ClientWebSocket _ws;
    private readonly Task _listenTask;
    private readonly CancellationTokenSource _cts = new();
    private int _nextId = 1;

    public string SessionName { get; }
    public string Role { get; }
    public long UserId { get; }
    public string ServerUrl { get; }

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingCommands = new();
    public List<T18NetworkResponseRecord> NetworkResponses { get; } = new();
    public List<T18WebSocketFrameRecord> WebSocketFrames { get; } = new();
    public List<string> ConsoleLogs { get; } = new();
    public List<string> CapturedScreenshots { get; } = new();

    private T18BrowserContext(
        Process process,
        string tempDir,
        ClientWebSocket ws,
        string sessionName,
        string role,
        long userId,
        string serverUrl)
    {
        _process = process;
        _tempDir = tempDir;
        _ws = ws;
        SessionName = sessionName;
        Role = role;
        UserId = userId;
        ServerUrl = serverUrl;
        _listenTask = Task.Run(ReceiveLoopAsync);
    }

    public static string ResolveEdgeBrowserPath()
    {
        var paths = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe")
        };

        foreach (var p in paths)
        {
            if (File.Exists(p)) return p;
        }

        return @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
    }

    public static async Task<T18BrowserContext> LaunchAsync(
        string sessionName,
        string role,
        long userId,
        string serverUrl,
        string? edgePath = null)
    {
        var resolvedPath = edgePath ?? ResolveEdgeBrowserPath();
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Microsoft Edge executable was not found at '{resolvedPath}'.", resolvedPath);
        }

        var port = Random.Shared.Next(28000, 35000);
        var tempDir = Path.Combine(Path.GetTempPath(), $"t18_edge_{sessionName}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var args = $"--headless --remote-debugging-port={port} --user-data-dir=\"{tempDir}\" --allow-file-access-from-files --disable-extensions --disable-component-extensions-with-background-pages --disable-background-networking --no-first-run --no-default-browser-check about:blank";

        var psi = new ProcessStartInfo
        {
            FileName = resolvedPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch Edge for {sessionName}.");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        string? wsUrl = null;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(200);
            try
            {
                var json = await httpClient.GetStringAsync($"http://127.0.0.1:{port}/json");
                using var doc = JsonDocument.Parse(json);
                foreach (var target in doc.RootElement.EnumerateArray())
                {
                    var targetType = target.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;
                    if (targetType == "page" && target.TryGetProperty("webSocketDebuggerUrl", out var prop))
                    {
                        wsUrl = prop.GetString();
                        if (!string.IsNullOrEmpty(wsUrl)) break;
                    }
                }
                if (!string.IsNullOrEmpty(wsUrl)) break;
            }
            catch
            {
                // Edge still initializing
            }
        }

        if (string.IsNullOrEmpty(wsUrl))
        {
            try { process.Kill(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
            throw new InvalidOperationException($"Failed to obtain CDP WebSocket debugger URL for {sessionName}.");
        }

        var ws = new ClientWebSocket();
        using (var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await ws.ConnectAsync(new Uri(wsUrl), connectCts.Token);
        }

        var context = new T18BrowserContext(process, tempDir, ws, sessionName, role, userId, serverUrl);

        await context.SendCommandAsync("Page.enable");
        await context.SendCommandAsync("Runtime.enable");
        await context.SendCommandAsync("Network.enable");
        await context.SendCommandAsync("Console.enable");
        await context.SendCommandAsync("Emulation.setDeviceMetricsOverride", new
        {
            width = 1280,
            height = 800,
            deviceScaleFactor = 1,
            mobile = false
        });

        // Authenticate the session
        await context.AuthenticateAsync();

        return context;
    }

    public async Task LoginViaWebFormAsync(string email, string password)
    {
        await NavigateAsync($"{ServerUrl}/Account/Login");
        await WaitForElementAsync("#UserName", TimeSpan.FromSeconds(15));
        await SetInputValueAsync("#UserName", email);
        await SetInputValueAsync("#Password", password);
        await ClickAsync("#btnLoginSubmit");
        await Task.Delay(2000);
    }

    public async Task<List<string>> GetCookieNamesAsync()
    {
        var res = await SendCommandAsync("Network.getCookies", new { urls = new[] { ServerUrl } });
        var list = new List<string>();
        if (res.TryGetProperty("cookies", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in arr.EnumerateArray())
            {
                if (c.TryGetProperty("name", out var n))
                {
                    list.Add(n.GetString() ?? "");
                }
            }
        }
        return list;
    }

    public async Task AuthenticateAsync()
    {
        string? seedEmail = UserId switch
        {
            1L => "customer.one@example.test",
            2L => "customer.two@example.test",
            3L => "staff.hcm@example.test",
            4L => "manager.hcm@example.test",
            5L => "operations@example.test",
            6L => "administrator@example.test",
            _ => null
        };

        if (seedEmail != null)
        {
            await LoginViaWebFormAsync(seedEmail, "TestPass123!");
        }
        else
        {
            // Fallback for custom dynamic test identities on test loopback host
            var authUrl = $"{ServerUrl}/test-auth-login?role={Uri.EscapeDataString(Role)}&userId={UserId}&name={Uri.EscapeDataString(SessionName)}";
            await NavigateAsync(authUrl);
            await Task.Delay(300);
        }
    }

    public async Task<JsonElement> SendCommandAsync(string method, object? @params = null, TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[id] = tcs;

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
        using var cts = new CancellationTokenSource(effectiveTimeout);

        var payload = JsonSerializer.Serialize(new
        {
            id,
            method,
            @params = @params ?? new { }
        });

        var bytes = Encoding.UTF8.GetBytes(payload);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);

        using (cts.Token.Register(() => tcs.TrySetException(new TimeoutException($"CDP command '{method}' (id={id}) timed out after {effectiveTimeout.TotalSeconds}s."))))
        {
            return await tcs.Task;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[65536];
        try
        {
            while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult res;
                do
                {
                    res = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    ms.Write(buffer, 0, res.Count);
                } while (!res.EndOfMessage);

                var jsonText = Encoding.UTF8.GetString(ms.ToArray());
                ProcessIncomingMessage(jsonText);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[{SessionName} CDP Receive Loop Exited]: {ex.Message}");
        }
    }

    private void ProcessIncomingMessage(string jsonText)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id))
            {
                if (_pendingCommands.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("result", out var resultProp))
                    {
                        tcs.TrySetResult(resultProp.Clone());
                    }
                    else if (root.TryGetProperty("error", out var errorProp))
                    {
                        tcs.TrySetException(new InvalidOperationException($"CDP Error (id={id}): {errorProp}"));
                    }
                    else
                    {
                        tcs.TrySetResult(root.Clone());
                    }
                }
                return;
            }

            // Event messages
            if (root.TryGetProperty("method", out var methodProp))
            {
                var method = methodProp.GetString();
                if (root.TryGetProperty("params", out var paramsProp))
                {
                    HandleCdpEvent(method, paramsProp);
                }
            }
        }
        catch { }
    }

    private void HandleCdpEvent(string? method, JsonElement @params)
    {
        switch (method)
        {
            case "Network.responseReceived":
                if (@params.TryGetProperty("response", out var respObj))
                {
                    var url = respObj.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    var status = respObj.TryGetProperty("status", out var s) ? s.GetInt32() : 0;
                    var mime = respObj.TryGetProperty("mimeType", out var m) ? m.GetString() ?? "" : "";
                    NetworkResponses.Add(new T18NetworkResponseRecord(url, status, mime, DateTimeOffset.UtcNow));
                }
                break;

            case "Network.webSocketFrameReceived":
                if (@params.TryGetProperty("response", out var wsResp) &&
                    wsResp.TryGetProperty("payloadData", out var pData))
                {
                    var payload = pData.GetString() ?? "";
                    var sanitized = T18TwoBrowserHarness.SanitizeText(payload);
                    WebSocketFrames.Add(new T18WebSocketFrameRecord("received", sanitized, DateTimeOffset.UtcNow));
                }
                break;

            case "Network.webSocketFrameSent":
                if (@params.TryGetProperty("response", out var wsSent) &&
                    wsSent.TryGetProperty("payloadData", out var sData))
                {
                    var payload = sData.GetString() ?? "";
                    var sanitized = T18TwoBrowserHarness.SanitizeText(payload);
                    WebSocketFrames.Add(new T18WebSocketFrameRecord("sent", sanitized, DateTimeOffset.UtcNow));
                }
                break;

            case "Console.messageAdded":
                if (@params.TryGetProperty("message", out var msgObj) &&
                    msgObj.TryGetProperty("text", out var textProp))
                {
                    var text = textProp.GetString() ?? "";
                    ConsoleLogs.Add($"[{SessionName}] {T18TwoBrowserHarness.SanitizeText(text)}");
                }
                break;

            case "Runtime.consoleAPICalled":
                if (@params.TryGetProperty("args", out var argsArr) && argsArr.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var a in argsArr.EnumerateArray())
                    {
                        if (a.TryGetProperty("value", out var v))
                        {
                            parts.Add(v.ToString());
                        }
                    }
                    ConsoleLogs.Add($"[{SessionName} JS] {T18TwoBrowserHarness.SanitizeText(string.Join(" ", parts))}");
                }
                break;
        }
    }

    public async Task NavigateAsync(string url, TimeSpan? timeout = null)
    {
        await SendCommandAsync("Page.navigate", new { url }, timeout);
        await Task.Delay(1200);
    }

    public async Task ReloadAsync(TimeSpan? timeout = null)
    {
        await SendCommandAsync("Page.reload", null, timeout);
        await Task.Delay(1200);
    }

    public async Task ClearCookiesAsync()
    {
        await SendCommandAsync("Network.clearBrowserCookies");
        await Task.Delay(200);
    }

    public async Task<bool> EvaluateBooleanAsync(string expression)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true
        });

        ThrowIfScriptException(result, expression);

        if (result.TryGetProperty("result", out var resObj) &&
            resObj.TryGetProperty("value", out var val) &&
            (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False))
        {
            return val.GetBoolean();
        }

        return false;
    }

    public async Task<string> CaptureSanitizedScreenshotAsync(string stepName)
    {
        // Pre-screenshot DOM pixel sanitization: Fail closed (Finding 4)
        // Mask sensitive elements (#displayPinValue, .checkin-pin-value, input[type="password"], [data-sensitive])
        var maskApplied = false;
        try
        {
            var maskOk = await EvaluateBooleanAsync("""
                (function() {
                    window.__t18_sanitized_nodes = [];
                    const selectors = ['#displayPinValue', '.checkin-pin-value', 'input[type="password"]', '[data-sensitive]'];
                    selectors.forEach(sel => {
                        document.querySelectorAll(sel).forEach(el => {
                            if (el.tagName === 'INPUT') {
                                window.__t18_sanitized_nodes.push({ el, origVal: el.value, type: 'val' });
                                el.value = '******';
                            } else {
                                window.__t18_sanitized_nodes.push({ el, origText: el.innerText, type: 'text' });
                                el.innerText = '******';
                            }
                        });
                    });

                    // Verification check: ensure no sensitive selector remains unmasked
                    let verified = true;
                    selectors.forEach(sel => {
                        document.querySelectorAll(sel).forEach(el => {
                            const current = el.tagName === 'INPUT' ? el.value : el.innerText;
                            if (current && current.length > 0 && current !== '******') {
                                verified = false;
                            }
                        });
                    });
                    return verified;
                })()
            """);

            if (!maskOk)
            {
                throw new InvalidOperationException($"Pre-screenshot DOM pixel sanitization failed closed for step '{stepName}': sensitive elements were not masked.");
            }
            maskApplied = true;

            var result = await SendCommandAsync("Page.captureScreenshot", new { format = "png" });
            if (!result.TryGetProperty("data", out var dataProp))
            {
                throw new InvalidOperationException($"Failed to capture screenshot for step '{stepName}'.");
            }

            var base64Data = dataProp.GetString() ?? throw new InvalidOperationException("Screenshot data is null.");
            var imageBytes = Convert.FromBase64String(base64Data);

            var evidenceDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../AgentReport/Evidence/T18"));
            if (!Directory.Exists(evidenceDir))
            {
                evidenceDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "AgentReport/Evidence/T18"));
                Directory.CreateDirectory(evidenceDir);
            }

            var fileName = $"{stepName}_{SessionName}.png";
            var filePath = Path.Combine(evidenceDir, fileName);
            await File.WriteAllBytesAsync(filePath, imageBytes);

            CapturedScreenshots.Add(filePath);
            return filePath;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Screenshot capture failed closed for step '{stepName}': {ex.Message}", ex);
        }
        finally
        {
            if (maskApplied)
            {
                try
                {
                    await ExecuteScriptAsync("""
                        (function() {
                            if (window.__t18_sanitized_nodes) {
                                window.__t18_sanitized_nodes.forEach(item => {
                                    if (item.type === 'val') {
                                        item.el.value = item.origVal;
                                    } else if (item.type === 'text') {
                                        item.el.innerText = item.origText;
                                    }
                                });
                                delete window.__t18_sanitized_nodes;
                            }
                        })()
                    """);
                }
                catch { }
            }
        }
    }

    public async Task ExecuteScriptAsync(string expression)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true
        });

        ThrowIfScriptException(result, expression);
    }

    public async Task<string> EvaluateStringRequiredAsync(string expression)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true
        });

        ThrowIfScriptException(result, expression);

        if (result.TryGetProperty("result", out var resObj) &&
            resObj.TryGetProperty("value", out var val) &&
            val.ValueKind == JsonValueKind.String)
        {
            return val.GetString() ?? "";
        }

        throw new InvalidOperationException($"Expression '{expression}' did not return a string.");
    }

    public async Task<string?> EvaluateStringOrNullAsync(string expression)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true
        });

        ThrowIfScriptException(result, expression);

        if (result.TryGetProperty("result", out var resObj) &&
            resObj.TryGetProperty("value", out var val) &&
            val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }

        return null;
    }

    public async Task<bool> EvaluateBoolAsync(string expression)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true
        });

        ThrowIfScriptException(result, expression);

        if (result.TryGetProperty("result", out var resObj) &&
            resObj.TryGetProperty("value", out var val) &&
            (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False))
        {
            return val.GetBoolean();
        }

        throw new InvalidOperationException($"Expression '{expression}' did not return a boolean.");
    }

    public async Task<int> EvaluateIntAsync(string expression)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true
        });

        ThrowIfScriptException(result, expression);

        if (result.TryGetProperty("result", out var resObj) &&
            resObj.TryGetProperty("value", out var val) &&
            val.TryGetInt32(out var intVal))
        {
            return intVal;
        }

        throw new InvalidOperationException($"Expression '{expression}' did not return an integer.");
    }

    public async Task<bool> WaitForConditionAsync(string conditionExpression, TimeSpan? timeout = null, int pollMs = 200)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(15);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < limit)
        {
            try
            {
                if (await EvaluateBoolAsync(conditionExpression))
                {
                    return true;
                }
            }
            catch { }
            await Task.Delay(pollMs);
        }

        return false;
    }

    public async Task<bool> WaitForElementAsync(string selector, TimeSpan? timeout = null)
    {
        var expr = $"document.querySelector('{selector.Replace("'", "\\'")}') !== null";
        return await WaitForConditionAsync(expr, timeout);
    }

    public async Task ClickAsync(string selector)
    {
        await ExecuteScriptAsync($"document.querySelector('{selector.Replace("'", "\\'")}')?.click()");
    }

    public async Task SetInputValueAsync(string selector, string value)
    {
        var escapedVal = JsonSerializer.Serialize(value);
        await ExecuteScriptAsync($$"""
            (function() {
                var el = document.querySelector('{{selector.Replace("'", "\\'")}}');
                if (el) {
                    el.value = {{escapedVal}};
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    el.dispatchEvent(new Event('change', { bubbles: true }));
                }
            })()
        """);
    }

    public async Task StopSignalRAsync()
    {
        await ExecuteScriptAsync("""
            if (window.connection && typeof window.connection.stop === 'function') {
                window.connection.stop();
            }
            if (window.appSignalRConnection && typeof window.appSignalRConnection.stop === 'function') {
                window.appSignalRConnection.stop();
            }
        """);
    }

    public async Task StartSignalRAsync()
    {
        await ExecuteScriptAsync("""
            if (window.connection && typeof window.connection.start === 'function') {
                window.connection.start();
            }
            if (window.appSignalRConnection && typeof window.appSignalRConnection.start === 'function') {
                window.appSignalRConnection.start();
            }
        """);
    }

    private static void ThrowIfScriptException(JsonElement result, string expression)
    {
        if (result.TryGetProperty("exceptionDetails", out var exDetails))
        {
            var desc = exDetails.TryGetProperty("exception", out var exObj) &&
                       exObj.TryGetProperty("description", out var descProp)
                ? descProp.GetString()
                : exDetails.TryGetProperty("text", out var textProp)
                    ? textProp.GetString()
                    : "Unknown JavaScript Exception";
            throw new InvalidOperationException($"JavaScript error evaluating '{expression}': {desc}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        try
        {
            await _listenTask;
        }
        catch { }

        try
        {
            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token);
            }
        }
        catch { }
        finally
        {
            try { _ws.Dispose(); } catch { }
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                _process.WaitForExit(2000);
            }
        }
        catch { }
        finally
        {
            try { _process.Dispose(); } catch { }
        }

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}

public record T18NetworkResponseRecord(string Url, int Status, string MimeType, DateTimeOffset Timestamp);
public record T18WebSocketFrameRecord(string Direction, string Payload, DateTimeOffset Timestamp);

/// <summary>
/// T18 Finding 1: Custom test password hasher for E2E tests.
/// Authenticates the standard E2E password "TestPass123!" against "external-auth-demo-only" (seed database value)
/// without executing any UPDATE mutations against core.users in the dedicated E2E database.
/// Delegates all other password operations to standard PBKDF2 PasswordHasher.
/// </summary>
internal sealed class T18TestPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher _inner = new();

    public string HashPassword(string password) => _inner.HashPassword(password);

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        if (hashedPassword == "external-auth-demo-only" && providedPassword == "TestPass123!")
        {
            return true;
        }

        return _inner.VerifyPassword(hashedPassword, providedPassword);
    }
}
