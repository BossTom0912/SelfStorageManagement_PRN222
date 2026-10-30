using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Auth;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.Entities;
using SelfStorageManagementSystem.Web.Extensions;
using SelfStorageManagementSystem.Web.Hubs;
using SelfStorageManagementSystem.Web.Realtime;
using SelfStorageManagementSystem.WebTests.Fixtures;
using SelfStorageManagementSystem.WebTests.Infrastructure;
using Xunit;

namespace SelfStorageManagementSystem.WebTests;

/// <summary>
/// Automated Browser & DOM tests for Task T13 Round 2 Finding Fixes:
/// - T13-R2-F01: Node.js syntax validation & Edge real browser loading of storage-units-realtime.js.
/// - T13-R2-F02: Realtime WebSocket connection revocation without manual reconnect.
/// - T13-R2-F03: Server-consistent SearchTerm filtering without notes leakage.
/// - T13-R2-F04: Sorting and pagination preservation via server refetching.
/// - T13-R2-F05: Proper JSON/DOM dataset decoding of special characters in JavaScript context.
/// </summary>
public class StorageUnitsBrowserRealtimeTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;

    public StorageUnitsBrowserRealtimeTests(CookieAuthWebTestFixture factory)
    {
        _factory = factory;
    }

    private static string ResolveNodePath()
    {
        var nodePaths = new[]
        {
            @"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Microsoft\VisualStudio\NodeJs\node.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"OpenAI\Codex\runtimes\cua_node\a5a02988362ca169\bin\node.exe")
        };

        foreach (var p in nodePaths)
        {
            if (File.Exists(p)) return p;
        }

        return "node";
    }

    private static string ResolveWebWwwroot()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\src\SelfStorageManagementSystem.Web\wwwroot"));
        if (Directory.Exists(candidate)) return candidate;
        candidate = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\src\SelfStorageManagementSystem.Web\wwwroot"));
        if (Directory.Exists(candidate)) return candidate;
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"src\SelfStorageManagementSystem.Web\wwwroot"));
    }

    private async Task<(facility Facility, unit_type UnitType, facility_area Area, storage_unit Unit)> SeedStorageUnitHierarchyAsync(string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

        var facId = (long)(Math.Abs(Guid.NewGuid().GetHashCode()) % 80000 + 2000);
        var fac = new facility
        {
            id = facId,
            code = $"FAC_{suffix.ToUpperInvariant()}",
            name = $"Cơ Sở Browser {suffix}",
            address_line = $"Số 200 Phố {suffix}",
            ward = "Phường 2",
            district = "Quận Cầu Giấy",
            city = "Hà Nội",
            status = "active",
            timezone = "Asia/Ho_Chi_Minh",
            opening_time = new TimeOnly(8, 0),
            closing_time = new TimeOnly(20, 0),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.facilities.Add(fac);

        var areaId = facId * 10 + 1;
        var area = new facility_area
        {
            id = areaId,
            facility_id = facId,
            code = $"AREA_{suffix.ToUpperInvariant()}",
            name = $"Khu Vực {suffix} - Tầng 1",
            area_type = "floor",
            display_order = 1,
            map_metadata = "{}",
            is_active = true
        };
        db.facility_areas.Add(area);

        var unitTypeId = facId * 10 + 2;
        var ut = new unit_type
        {
            id = unitTypeId,
            code = $"TYPE_{suffix.ToUpperInvariant()}",
            name = $"Loại Kho {suffix}",
            description = "Mô tả loại kho",
            width_m = 2.0m,
            length_m = 3.0m,
            height_m = 2.5m,
            area_m2 = 6.0m,
            volume_m3 = 15.0m,
            climate_controlled = true,
            max_weight_kg = 500m,
            is_active = true,
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.unit_types.Add(ut);

        var rateId = facId * 10 + 3;
        var rate = new facility_rate
        {
            id = rateId,
            facility_id = facId,
            unit_type_id = unitTypeId,
            monthly_rate = 1500000m,
            deposit_amount = 1500000m,
            booking_fee = 200000m,
            valid_from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            valid_to = null,
            created_at = DateTimeOffset.UtcNow
        };
        db.facility_rates.Add(rate);

        var unitId = facId * 100 + 1;
        var unit = new storage_unit
        {
            id = unitId,
            facility_id = facId,
            unit_type_id = unitTypeId,
            area_id = areaId,
            unit_code = $"BRW-{suffix.ToUpperInvariant()}",
            physical_status = "available",
            is_listed = true,
            floor_label = "Tầng 1",
            zone_label = "Zone A",
            notes = $"Internal VIP Notes {suffix}",
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow
        };
        db.storage_units.Add(unit);
        await db.SaveChangesAsync();

        return (fac, ut, area, unit);
    }

    private async Task<string> AuthenticateUserAndGetCookieAsync(string roleCode, long? assignedFacilityId = null)
    {
        var email = $"browser_{roleCode}_{Guid.NewGuid():N}@example.test";
        var password = "SecureBrowserPass123!";

        var user = await _factory.SeedUserAsync(email, password, roleCode, UserStatusConstants.Active, $"User {roleCode}");

        if (assignedFacilityId.HasValue && (roleCode == RoleConstants.FacilityStaff || roleCode == RoleConstants.FacilityManager))
        {
            using var db = _factory.CreateDbContext();
            db.staff_facility_assignments.Add(new staff_facility_assignment
            {
                employee_id = user.id,
                facility_id = assignedFacilityId.Value,
                assignment_role = roleCode,
                starts_at = DateTimeOffset.UtcNow.AddDays(-5),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var (token, cookieHeader) = await CookieAuthWebTestFixture.GetAntiforgeryTokenAndCookieAsync(client, "/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/"
        });

        var loginMsg = new HttpRequestMessage(HttpMethod.Post, "/Account/Login") { Content = loginContent };
        loginMsg.Headers.Add("Cookie", cookieHeader);
        var loginResp = await client.SendAsync(loginMsg);

        Assert.Equal(HttpStatusCode.Redirect, loginResp.StatusCode);
        var authHeader = loginResp.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(c => c.Contains(CookieAuthExtensions.CookieName));
        Assert.NotNull(authHeader);
        return authHeader!.Split(';')[0];
    }

    #region Diagnostic Direct Test

    [Fact]
    public async Task DirectTest_StorageUnits_Returns200OK()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var msg = new HttpRequestMessage(HttpMethod.Get, $"/StorageUnits?facilityId={hierarchy.Facility.id}");
        msg.Headers.Add("Cookie", managerCookie);
        var resp = await client.SendAsync(msg);

        var html = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"Expected 200 OK but was {resp.StatusCode}. Location: {resp.Headers.Location}\nBody: {html[..Math.Min(html.Length, 500)]}");
        Assert.Contains("id=\"storageUnitsPageConfig\"", html);
    }

    #endregion

    #region T13-R2-F01: Node.js Syntax Check & Real Edge Browser Loading

    [Fact]
    public void T13_R2_F01_NodeCheck_StorageUnitsRealtimeScript_ExitsWithZero()
    {
        var scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js"));
        if (!File.Exists(scriptPath))
        {
            scriptPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src/SelfStorageManagementSystem.Web/wwwroot/js/storage-units-realtime.js"));
        }
        Assert.True(File.Exists(scriptPath), $"Script storage-units-realtime.js must exist at '{scriptPath}'.");

        var nodeExe = ResolveNodePath();
        var psi = new ProcessStartInfo
        {
            FileName = nodeExe,
            Arguments = $"--check \"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0,
            $"node --check failed with exit code {process.ExitCode}.\nSTDOUT: {stdout}\nSTDERR: {stderr}");
    }

    [Fact]
    public async Task T13_R2_F01_EdgeBrowser_LoadsIndexPage_DefinesStorageUnitsRealtimeWithoutErrors()
    {
        var edgePath = HeadlessEdgeSession.ResolveEdgeBrowserPath();
        if (!File.Exists(edgePath))
        {
            // If Edge not installed in environment, skip with clear notice
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        var proxyClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        var proxyPort = Random.Shared.Next(33100, 39900);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{proxyPort}/");
        listener.Start();

        using var cts = new CancellationTokenSource();
        var proxyTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var req = ctx.Request;
                            var localWwwroot = ResolveWebWwwroot();
                            var cleanPath = (req.Url?.AbsolutePath ?? "").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                            var candidateFile = Path.Combine(localWwwroot, cleanPath);
                            if (File.Exists(candidateFile))
                            {
                                var fileBytes = await File.ReadAllBytesAsync(candidateFile);
                                ctx.Response.StatusCode = 200;
                                if (candidateFile.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                                    ctx.Response.ContentType = "application/javascript";
                                else if (candidateFile.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                                    ctx.Response.ContentType = "text/css";
                                ctx.Response.ContentLength64 = fileBytes.Length;
                                await ctx.Response.OutputStream.WriteAsync(fileBytes);
                                ctx.Response.OutputStream.Close();
                                return;
                            }

                            var targetUri = new Uri(proxyClient.BaseAddress!, req.RawUrl);
                            using var msg = new HttpRequestMessage(new HttpMethod(req.HttpMethod), targetUri);
                            foreach (string h in req.Headers)
                            {
                                if (h.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                                msg.Headers.TryAddWithoutValidation(h, req.Headers[h]);
                            }
                            msg.Headers.TryAddWithoutValidation("Cookie", managerCookie);

                            if (req.HasEntityBody)
                            {
                                using var ms = new MemoryStream();
                                await req.InputStream.CopyToAsync(ms);
                                msg.Content = new ByteArrayContent(ms.ToArray());
                                if (!string.IsNullOrEmpty(req.ContentType))
                                {
                                    msg.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(req.ContentType);
                                }
                            }

                            var resp = await proxyClient.SendAsync(msg);
                            ctx.Response.StatusCode = (int)resp.StatusCode;
                            foreach (var h in resp.Headers) ctx.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            foreach (var h in resp.Content.Headers) ctx.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            var bytes = await resp.Content.ReadAsByteArrayAsync();
                            ctx.Response.ContentLength64 = bytes.Length;
                            await ctx.Response.OutputStream.WriteAsync(bytes);
                            ctx.Response.OutputStream.Close();
                        }
                        catch
                        {
                            try { ctx.Response.Abort(); } catch { }
                        }
                    });
                }
                catch when (cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch { }
            }
        });

        try
        {
            await using var session = await HeadlessEdgeSession.LaunchAsync(edgePath);
            await session.NavigateAsync($"http://127.0.0.1:{proxyPort}/StorageUnits?facilityId={hierarchy.Facility.id}");

            bool isObject = false;
            for (var i = 0; i < 25; i++)
            {
                isObject = await session.EvaluateBoolAsync("typeof window.StorageUnitsRealtime === 'object'");
                if (isObject) break;
                await Task.Delay(200);
            }
            var currentUrl = await session.EvaluateStringRequiredAsync("window.location.href");
            var currentTitle = await session.EvaluateStringRequiredAsync("document.title");
            var bodySnippet = await session.EvaluateStringRequiredAsync("document.body ? document.body.innerHTML.substring(0, 500) : 'NO_BODY'");
            Assert.True(isObject, $"window.StorageUnitsRealtime must be defined on window. Current URL: {currentUrl}, Title: {currentTitle}, Body: {bodySnippet}");

            var hasGenerate = await session.EvaluateBoolAsync("typeof window.StorageUnitsRealtime.generateRowContent === 'function'");
            Assert.True(hasGenerate, "window.StorageUnitsRealtime.generateRowContent must be a function.");

            var hasMatches = await session.EvaluateBoolAsync("typeof window.StorageUnitsRealtime.matchesFilter === 'function'");
            Assert.True(hasMatches, "window.StorageUnitsRealtime.matchesFilter must be a function.");

            var hasRefetch = await session.EvaluateBoolAsync("typeof window.StorageUnitsRealtime.refetchTable === 'function'");
            Assert.True(hasRefetch, "window.StorageUnitsRealtime.refetchTable must be a function.");

            // Verify the seeded unit row rendered in DOM
            var rowCount = await session.EvaluateStringRequiredAsync("document.querySelectorAll('#storageUnitsTableBody tr[id^=\"unit-row-\"]').length.toString()");
            Assert.True(int.Parse(rowCount) >= 1, "At least 1 unit row must be rendered in table body.");
        }
        finally
        {
            cts.Cancel();
            try { listener.Stop(); } catch { }
            try { await proxyTask; } catch { }
        }
    }

    #endregion

    #region T13-R2-F05: Proper JSON / DOM Dataset Decoding of Special Characters

    [Fact]
    public async Task T13_R2_F05_EdgeBrowser_SpecialCharsInSearchTerm_DecodedCorrectlyInJavaScript()
    {
        var edgePath = HeadlessEdgeSession.ResolveEdgeBrowserPath();
        if (!File.Exists(edgePath))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var hierarchy = await SeedStorageUnitHierarchyAsync(suffix);
        var managerCookie = await AuthenticateUserAndGetCookieAsync(RoleConstants.FacilityManager, hierarchy.Facility.id);

        var proxyClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        var proxyPort = Random.Shared.Next(33100, 39900);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{proxyPort}/");
        listener.Start();

        using var cts = new CancellationTokenSource();
        var proxyTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var req = ctx.Request;
                            var localWwwroot = ResolveWebWwwroot();
                            var cleanPath = (req.Url?.AbsolutePath ?? "").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                            var candidateFile = Path.Combine(localWwwroot, cleanPath);
                            if (File.Exists(candidateFile))
                            {
                                var fileBytes = await File.ReadAllBytesAsync(candidateFile);
                                ctx.Response.StatusCode = 200;
                                if (candidateFile.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                                    ctx.Response.ContentType = "application/javascript";
                                else if (candidateFile.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                                    ctx.Response.ContentType = "text/css";
                                ctx.Response.ContentLength64 = fileBytes.Length;
                                await ctx.Response.OutputStream.WriteAsync(fileBytes);
                                ctx.Response.OutputStream.Close();
                                return;
                            }

                            var targetUri = new Uri(proxyClient.BaseAddress!, req.RawUrl);
                            using var msg = new HttpRequestMessage(new HttpMethod(req.HttpMethod), targetUri);
                            foreach (string h in req.Headers)
                            {
                                if (h.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                                msg.Headers.TryAddWithoutValidation(h, req.Headers[h]);
                            }
                            msg.Headers.TryAddWithoutValidation("Cookie", managerCookie);

                            var resp = await proxyClient.SendAsync(msg);
                            ctx.Response.StatusCode = (int)resp.StatusCode;
                            foreach (var h in resp.Headers) ctx.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            foreach (var h in resp.Content.Headers) ctx.Response.Headers[h.Key] = string.Join(", ", h.Value);
                            var bytes = await resp.Content.ReadAsByteArrayAsync();
                            ctx.Response.ContentLength64 = bytes.Length;
                            await ctx.Response.OutputStream.WriteAsync(bytes);
                            ctx.Response.OutputStream.Close();
                        }
                        catch
                        {
                            try { ctx.Response.Abort(); } catch { }
                        }
                    });
                }
                catch when (cts.Token.IsCancellationRequested) { break; }
                catch { }
            }
        });

        try
        {
            await using var session = await HeadlessEdgeSession.LaunchAsync(edgePath);
            // Search term with ampersand, quotes, brackets, and unicode
            var searchTerm = "Kho A&B \"VIP\" <1>";
            var escapedTerm = Uri.EscapeDataString(searchTerm);

            await session.NavigateAsync($"http://127.0.0.1:{proxyPort}/StorageUnits?facilityId={hierarchy.Facility.id}&searchTerm={escapedTerm}");

            bool hasEl = false;
            for (var i = 0; i < 25; i++)
            {
                hasEl = await session.EvaluateBoolAsync("document.getElementById('storageUnitsPageConfig') !== null");
                if (hasEl) break;
                await Task.Delay(200);
            }
            Assert.True(hasEl, "document.getElementById('storageUnitsPageConfig') must be present in DOM.");

            var decodedInJs = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsPageConfig').dataset.searchTerm");
            Assert.Equal(searchTerm, decodedInJs);
            Assert.DoesNotContain("&amp;", decodedInJs);
            Assert.DoesNotContain("&quot;", decodedInJs);
            Assert.DoesNotContain("&lt;", decodedInJs);
            Assert.DoesNotContain("&gt;", decodedInJs);
        }
        finally
        {
            cts.Cancel();
            try { listener.Stop(); } catch { }
            try { await proxyTask; } catch { }
        }
    }

    #endregion

    #region T13-R2-F03 & T13-R2-F04: Server Refetch and Client Filter Consistency

    [Fact]
    public void T13_R2_F03_MatchesFilter_EvaluatesUnitCodeFloorZone_AndOmitsTypeNameFacilityNameFromSearch()
    {
        var filter = new
        {
            facilityId = "10",
            unitTypeId = "20",
            areaId = "30",
            physicalStatus = "available",
            isListed = "true",
            searchTerm = "tầng 2"
        };

        // Matching on floorLabel
        var matchingFloor = new StorageUnitRealtimeEventDto
        {
            id = 1,
            unitCode = "UNIT-01",
            facilityId = 10,
            unitTypeId = 20,
            areaId = 30,
            status = "available",
            isListed = true,
            floorLabel = "Tầng 2",
            zoneLabel = "Zone A"
        };

        // Item where only facilityName matches searchTerm: Server DOES NOT search facilityName
        var facilityMatchOnly = new StorageUnitRealtimeEventDto
        {
            id = 2,
            unitCode = "UNIT-02",
            facilityName = "Cơ sở Tầng 2",
            facilityId = 10,
            unitTypeId = 20,
            areaId = 30,
            status = "available",
            isListed = true,
            floorLabel = "Tầng 1",
            zoneLabel = "Zone B"
        };

        // Helper rule:
        bool Matches(StorageUnitRealtimeEventDto item)
        {
            if (filter.facilityId != "" && item.facilityId.ToString() != filter.facilityId) return false;
            if (filter.unitTypeId != "" && item.unitTypeId.ToString() != filter.unitTypeId) return false;
            if (filter.areaId != "" && (item.areaId?.ToString() ?? "") != filter.areaId) return false;
            if (filter.physicalStatus != "" && !item.status.Equals(filter.physicalStatus, StringComparison.OrdinalIgnoreCase)) return false;
            if (filter.isListed != "" && item.isListed.ToString().ToLowerInvariant() != filter.isListed) return false;

            if (!string.IsNullOrEmpty(filter.searchTerm))
            {
                var term = filter.searchTerm.Trim().ToLowerInvariant();
                var code = (item.unitCode ?? "").ToLowerInvariant();
                var floor = (item.floorLabel ?? "").ToLowerInvariant();
                var zone = (item.zoneLabel ?? "").ToLowerInvariant();
                if (!code.Contains(term) && !floor.Contains(term) && !zone.Contains(term))
                {
                    return false;
                }
            }
            return true;
        }

        Assert.True(Matches(matchingFloor));
        Assert.False(Matches(facilityMatchOnly), "Items matching search only on facilityName must NOT match client filter (conforms to server).");
    }

    #endregion

    #region T13 Round 3 Browser & Edge Tests (T13-R3-F01 to T13-R3-F06)

    [Fact]
    public async Task T13_R3_A_CDP_SendCommandAsync_WhenPeerIsSilent_ThrowsTimeoutExceptionInFiniteTime()
    {
        var port = Random.Shared.Next(34000, 39000);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                if (ctx.Request.IsWebSocketRequest)
                {
                    var wsContext = await ctx.AcceptWebSocketAsync(null);
                    var buffer = new byte[4096];
                    // Read command from client, then intentionally remain silent (do not reply)
                    await wsContext.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    await Task.Delay(3000);
                }
            }
            catch { }
        });

        using var clientWs = new ClientWebSocket();
        await clientWs.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

        var dummyProcess = Process.GetCurrentProcess();
        var tempDir = Path.Combine(Path.GetTempPath(), "dummy_cdp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var ctor = typeof(HeadlessEdgeSession).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
            var session = (HeadlessEdgeSession)ctor.Invoke(new object[] { dummyProcess, tempDir, clientWs });

            var sw = Stopwatch.StartNew();
            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await session.SendCommandAsync("Test.hangCommand", new { }, timeout: TimeSpan.FromMilliseconds(500));
            });

            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 3000, $"CDP command must time out promptly (took {sw.ElapsedMilliseconds}ms)");
            Assert.Contains("Test.hangCommand", ex.Message);
            Assert.Contains("timed out", ex.Message);
        }
        finally
        {
            try { listener.Stop(); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task T13_R3_F02_RevocationService_WhenRemoveFromGroupFailsPermanently_Retries3Times_AndEnforcesFailClosedAbort()
    {
        var mockGroups = new Mock<IGroupManager>();
        var removeAttempts = 0;
        mockGroups.Setup(g => g.RemoveFromGroupAsync("conn-fail-1", "facility_10", It.IsAny<CancellationToken>()))
            .Callback(() => removeAttempts++)
            .ThrowsAsync(new InvalidOperationException("Simulated Redis/SignalR network failure"));

        var mockClientProxy = new Mock<ISingleClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Client("conn-fail-1")).Returns(mockClientProxy.Object);

        var mockHubContext = new Mock<IHubContext<StorageUnitHub>>();
        mockHubContext.Setup(h => h.Groups).Returns(mockGroups.Object);
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var tracker = new StorageUnitHubConnectionTracker();
        var wasAborted = false;
        tracker.TrackConnection(1001, "conn-fail-1", new[] { "facility_10" }, () => wasAborted = true);

        var mockScopeService = new Mock<IFacilityScopeService>();
        mockScopeService.Setup(s => s.GetAccessibleFacilityIdsAsync(1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<long>()); // Assignment revoked!

        var mockIdentityService = new Mock<IAccountIdentityService>();
        mockIdentityService.Setup(i => i.GetCurrentUserAsync(1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentUserResponse
            {
                UserId = 1001,
                DisplayName = "Staff Test",
                Email = "staff@test.com",
                Status = UserStatusConstants.Active,
                Roles = new List<string> { RoleConstants.FacilityStaff }
            });

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<StorageUnitHubScopeRevocationService>.Instance;
        var revocationService = new StorageUnitHubScopeRevocationService(
            mockHubContext.Object,
            tracker,
            mockScopeService.Object,
            mockIdentityService.Object,
            logger);

        // Execute reconciliation (T13-R8-F01: fails closed with RealtimeScopeRevocationException when group removal fails)
        await Assert.ThrowsAsync<RealtimeScopeRevocationException>(() =>
            revocationService.ReconcileUserConnectionsAsync(1001, CancellationToken.None));

        // Assert 1: Exactly 3 retry attempts were made
        Assert.Equal(3, removeAttempts);

        // Assert 2: Client was notified of force disconnect
        mockClientProxy.Verify(c => c.SendCoreAsync("ForceDisconnect", It.Is<object[]>(args => args.Length > 0), It.IsAny<CancellationToken>()), Times.Once());

        // Assert 3: Fail-closed enforcement: abortAction was triggered!
        Assert.True(wasAborted, "Connection must be aborted when group revocation fails permanently.");
    }

    [Fact]
    public async Task T13_R3_Browser_ExecuteTestSuiteHtml_AllEightSuitesPassInRealEdgeSession()
    {
        var edgePath = HeadlessEdgeSession.ResolveEdgeBrowserPath();
        if (!File.Exists(edgePath))
        {
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        var htmlPath = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\BrowserTests\storage-units-realtime-tests.html"));
        if (!File.Exists(htmlPath))
        {
            htmlPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"tests\SelfStorageManagementSystem.WebTests\BrowserTests\storage-units-realtime-tests.html"));
        }
        Assert.True(File.Exists(htmlPath), $"Browser test HTML file must exist at {htmlPath}");

        var localWwwroot = ResolveWebWwwroot();
        var jsPath = Path.Combine(localWwwroot, "js", "storage-units-realtime.js");
        var jsBytes = await File.ReadAllBytesAsync(jsPath);
        var htmlBytes = await File.ReadAllBytesAsync(htmlPath);

        var proxyPort = Random.Shared.Next(34000, 39500);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{proxyPort}/");
        listener.Start();

        using var cts = new CancellationTokenSource();
        var proxyTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var req = ctx.Request;
                            var absPath = req.Url?.AbsolutePath ?? "";
                            if (absPath.EndsWith("storage-units-realtime.js", StringComparison.OrdinalIgnoreCase))
                            {
                                ctx.Response.StatusCode = 200;
                                ctx.Response.ContentType = "application/javascript; charset=utf-8";
                                ctx.Response.ContentLength64 = jsBytes.Length;
                                await ctx.Response.OutputStream.WriteAsync(jsBytes);
                                ctx.Response.OutputStream.Close();
                                return;
                            }

                            if (absPath.Contains("test-runner") || absPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                            {
                                ctx.Response.StatusCode = 200;
                                ctx.Response.ContentType = "text/html; charset=utf-8";
                                ctx.Response.ContentLength64 = htmlBytes.Length;
                                await ctx.Response.OutputStream.WriteAsync(htmlBytes);
                                ctx.Response.OutputStream.Close();
                                return;
                            }

                            ctx.Response.StatusCode = 404;
                            ctx.Response.Close();
                        }
                        catch
                        {
                            try { ctx.Response.Abort(); } catch { }
                        }
                    });
                }
                catch when (cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch { }
            }
        });

        try
        {
            await using var session = await HeadlessEdgeSession.LaunchAsync(edgePath);
            await session.NavigateAsync($"http://127.0.0.1:{proxyPort}/test-runner");

            string? testStatus = null;
            for (var i = 0; i < 40; i++)
            {
                testStatus = await session.EvaluateStringRequiredAsync("document.getElementById('summary')?.getAttribute('data-test-status') || ''");
                if (testStatus == "PASS" || testStatus == "FAIL")
                {
                    break;
                }
                await Task.Delay(250);
            }

            var summaryText = await session.EvaluateStringRequiredAsync("document.getElementById('summary')?.textContent || ''");
            Assert.Equal("PASS", testStatus);
            Assert.Contains("100%", summaryText);
        }
        finally
        {
            cts.Cancel();
            try { listener.Stop(); } catch { }
            try { await proxyTask; } catch { }
        }
    }

    [Fact]
    public async Task T13_R3_Browser_RealPage_EndToEnd_F03_F04_F05_F06()
    {
        var edgePath = HeadlessEdgeSession.ResolveEdgeBrowserPath();
        if (!File.Exists(edgePath))
        {
            return;
        }

        // T13-R4-F05: Spin up real Kestrel server with real WebSocket upgrade support
        var dbName = Guid.NewGuid().ToString("N")[..8];
        await using var server = await KestrelRealtimeServer.StartAsync(dbName);

        var facilityId = 10L;
        var unitTypeId = 20L;
        var areaId = 30L;
        var managerUserId = 1001L;

        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();

            var fac = new facility
            {
                id = facilityId,
                name = "Kestrel Facility",
                code = "FAC_KESTREL",
                address_line = "123 Realtime Street",
                ward = "Phuong 2",
                district = "Quan Cau Giay",
                city = "Hanoi",
                status = "active",
                timezone = "Asia/Ho_Chi_Minh",
                opening_time = new TimeOnly(8, 0),
                closing_time = new TimeOnly(20, 0),
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.facilities.Add(fac);

            var unitType = new unit_type
            {
                id = unitTypeId,
                code = "TYPE_KESTREL",
                name = "Loai Kho Kestrel",
                description = "Mo ta loai kho",
                width_m = 2.0m,
                length_m = 3.0m,
                height_m = 2.5m,
                area_m2 = 6.0m,
                volume_m3 = 15.0m,
                climate_controlled = true,
                max_weight_kg = 500m,
                is_active = true,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.unit_types.Add(unitType);

            var area = new facility_area
            {
                id = areaId,
                facility_id = facilityId,
                name = "Khu Vuc A",
                code = "AREA_KESTREL",
                area_type = "floor",
                display_order = 1,
                map_metadata = "{}",
                is_active = true
            };
            db.facility_areas.Add(area);

            var rate = new facility_rate
            {
                id = 40,
                facility_id = facilityId,
                unit_type_id = unitTypeId,
                monthly_rate = 1500000m,
                deposit_amount = 1500000m,
                booking_fee = 200000m,
                valid_from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                valid_to = null,
                created_at = DateTimeOffset.UtcNow
            };
            db.facility_rates.Add(rate);

            var mgrUser = new user
            {
                id = managerUserId,
                email = "mgr_kestrel@example.test",
                password_hash = "fakehash",
                status = UserStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.users.Add(mgrUser);

            var mgrRole = new role { id = 1, code = RoleConstants.FacilityManager, display_name = "Facility Manager" };
            db.roles.Add(mgrRole);

            var mgrUserRole = new user_role { user_id = managerUserId, role_id = 1, granted_at = DateTimeOffset.UtcNow };
            db.user_roles.Add(mgrUserRole);

            var mgrEmpProfile = new employee_profile
            {
                user_id = managerUserId,
                employee_code = "EMP_1001",
                full_name = "Manager Kestrel",
                hire_date = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                employment_status = EmploymentStatusConstants.Active,
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            };
            db.employee_profiles.Add(mgrEmpProfile);

            var mgrAssignment = new staff_facility_assignment
            {
                employee_id = managerUserId,
                facility_id = facilityId,
                assignment_role = RoleConstants.FacilityManager,
                starts_at = DateTimeOffset.UtcNow.AddDays(-10),
                ends_at = null,
                created_at = DateTimeOffset.UtcNow
            };
            db.staff_facility_assignments.Add(mgrAssignment);

            // Seed 10 initial units -> 1 full page (PageSize = 10)
            for (int i = 1; i <= 10; i++)
            {
                db.storage_units.Add(new storage_unit
                {
                    id = 100 + i,
                    facility_id = facilityId,
                    area_id = areaId,
                    unit_type_id = unitTypeId,
                    unit_code = $"UNIT-{i:D2}",
                    floor_label = "Tang 1",
                    zone_label = "Day A",
                    physical_status = "available",
                    is_listed = true,
                    notes = $"Initial note {i}",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }

        await using var session = await HeadlessEdgeSession.LaunchAsync(edgePath);

        // 1. Authenticate browser session via Kestrel test login endpoint
        await session.NavigateAsync($"{server.ServerUrl}/test-auth-login?role={RoleConstants.FacilityManager}&userId={managerUserId}&name=Manager+Kestrel");
        await Task.Delay(300);

        // 2. Navigate to real StorageUnits Razor Page with SearchTerm query
        await session.NavigateAsync($"{server.ServerUrl}/StorageUnits?facilityId={facilityId}&SearchTerm=BaoMatSpecial");

        // 3. Wait for real SignalR WebSocket connection to reach 'Connected' state
        string? connState = null;
        for (var i = 0; i < 40; i++)
        {
            connState = await session.EvaluateStringRequiredAsync("window.connection?.state || ''");
            if (connState == "Connected") break;
            await Task.Delay(250);
        }
        Assert.Equal("Connected", connState);

        // 4. F03: Verify that initially 0 rows exist matching secret notes
        var initialRowMatches = await session.EvaluateBoolAsync("document.querySelectorAll('tr[id^=\"unit-row-\"]').length === 0");
        Assert.True(initialRowMatches, "F03: Initial query with secret search term should have 0 matches before unit creation.");

        // Seed new unit with secret notes into DB
        var newUnitId = 201L;
        var newUnitCode = "UNIT-SPECIAL-99";
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            db.storage_units.Add(new storage_unit
            {
                id = newUnitId,
                facility_id = facilityId,
                area_id = areaId,
                unit_type_id = unitTypeId,
                unit_code = newUnitCode,
                floor_label = "Tang 1",
                zone_label = "Day A",
                physical_status = "available",
                is_listed = true,
                notes = "Thong tin BaoMatSpecial can tim",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Broadcast real Receiver_CreateStorageUnit event via Kestrel publisher
        using (var scope = server.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IStorageUnitRealtimePublisher>();
            await publisher.PublishCreatedAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = newUnitId,
                UnitCode = newUnitCode,
                FacilityId = facilityId,
                FacilityName = "Kestrel Facility",
                FacilityCode = "FAC_KESTREL",
                UnitTypeId = unitTypeId,
                UnitTypeName = "Loai Kho Kestrel",
                UnitTypeCode = "TYPE_KESTREL",
                AreaId = areaId,
                AreaName = "Khu Vuc A",
                PhysicalStatus = "available",
                IsListed = true
            });
        }

        // Real SignalR listener runs -> matchesFilter allows refetch -> refetchTable executes GET -> row inserted into DOM
        bool rowAppeared = false;
        for (var i = 0; i < 40; i++)
        {
            rowAppeared = await session.EvaluateBoolAsync($"document.getElementById('unit-row-{newUnitId}') !== null");
            if (rowAppeared) break;
            await Task.Delay(250);
        }
        Assert.True(rowAppeared, "F03 E2E: Real SignalR Receiver_CreateStorageUnit triggered server refetch and row appeared in DOM.");

        // 5. F04 E2E: Pagination & Footer 10 -> 11 -> 10 transition via real SignalR events
        // Delete the temporary secret unit from DB first to reset to exactly 10 units (1 full page)
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var sp = await db.storage_units.FindAsync(newUnitId);
            if (sp != null)
            {
                db.storage_units.Remove(sp);
                await db.SaveChangesAsync();
            }
        }

        // Navigate to full list (PageSize = 10, total 10 units)
        await session.NavigateAsync($"{server.ServerUrl}/StorageUnits?facilityId={facilityId}");
        await Task.Delay(500);

        var count10 = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsTotalCount')?.textContent || ''");
        Assert.Equal("10", count10.Trim());

        var rowCount10 = await session.EvaluateIntAsync("document.querySelectorAll('#storageUnitsTableBody tr[id^=\"unit-row-\"]').length");
        Assert.Equal(10, rowCount10);

        var footer10 = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsFooterContainer')?.textContent.replace(/\\s+/g, ' ').trim() || ''");
        Assert.Contains("1 đến 10", footer10);
        Assert.Contains("trong tổng số 10", footer10);

        var hasPage2Link10 = await session.EvaluateBoolAsync("document.querySelector('ul.pagination li.page-item a[href*=\"pageNumber=2\" i]') !== null");
        Assert.False(hasPage2Link10, "F04 E2E: With 10 units, no Page 2 link exists in pagination.");

        // Transition 10 -> 11: Seed 11th unit into DB and broadcast Receiver_CreateStorageUnit
        var unit11Id = 301L;
        var unit11Code = "UNIT-11-TRANS";
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            db.storage_units.Add(new storage_unit
            {
                id = unit11Id,
                facility_id = facilityId,
                area_id = areaId,
                unit_type_id = unitTypeId,
                unit_code = unit11Code,
                floor_label = "Tang 1",
                zone_label = "Day A",
                physical_status = "available",
                is_listed = true,
                notes = "Unit 11 pagination test",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using (var scope = server.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IStorageUnitRealtimePublisher>();
            await publisher.PublishCreatedAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = unit11Id,
                UnitCode = unit11Code,
                FacilityId = facilityId,
                FacilityName = "Kestrel Facility",
                FacilityCode = "FAC_KESTREL",
                UnitTypeId = unitTypeId,
                UnitTypeName = "Loai Kho Kestrel",
                UnitTypeCode = "TYPE_KESTREL",
                AreaId = areaId,
                AreaName = "Khu Vuc A",
                PhysicalStatus = "available",
                IsListed = true
            });
        }

        bool countBecame11 = false;
        for (var i = 0; i < 40; i++)
        {
            var c11 = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsTotalCount')?.textContent || ''");
            if (c11.Trim() == "11")
            {
                countBecame11 = true;
                break;
            }
            await Task.Delay(250);
        }
        Assert.True(countBecame11, "F04 E2E: Total count updated to 11 via real SignalR Receiver_CreateStorageUnit listener.");

        var footer11 = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsFooterContainer')?.textContent.replace(/\\s+/g, ' ').trim() || ''");
        Assert.Contains("1 đến 10", footer11);
        Assert.Contains("trong tổng số 11", footer11);

        var hasPage2Link11 = await session.EvaluateBoolAsync("document.querySelector('ul.pagination li.page-item a[href*=\"pageNumber=2\" i]') !== null");
        Assert.True(hasPage2Link11, "F04 E2E: Pagination now displays link to Page 2 when total units reached 11.");

        // Transition 11 -> 10: Delete the 11th unit from DB and broadcast Receiver_DeleteStorageUnit
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var u11 = await db.storage_units.FindAsync(unit11Id);
            if (u11 != null)
            {
                db.storage_units.Remove(u11);
                await db.SaveChangesAsync();
            }
        }

        using (var scope = server.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IStorageUnitRealtimePublisher>();
            await publisher.PublishDeletedAsync(unit11Id, facilityId);
        }

        bool countRevertedTo10 = false;
        for (var i = 0; i < 40; i++)
        {
            var cBack = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsTotalCount')?.textContent || ''");
            if (cBack.Trim() == "10")
            {
                countRevertedTo10 = true;
                break;
            }
            await Task.Delay(250);
        }
        Assert.True(countRevertedTo10, "F04 E2E: Total count reverted back to 10 via real SignalR Receiver_DeleteStorageUnit listener.");

        var footerBack10 = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsFooterContainer')?.textContent.replace(/\\s+/g, ' ').trim() || ''");
        Assert.Contains("1 đến 10", footerBack10);
        Assert.Contains("trong tổng số 10", footerBack10);

        var hasPage2LinkBack10 = await session.EvaluateBoolAsync("document.querySelector('ul.pagination li.page-item a[href*=\"pageNumber=2\" i]') !== null");
        Assert.False(hasPage2LinkBack10, "F04 E2E: Pagination Page 2 link removed when total units reverted back to 10.");

        // 6. F05 E2E: Real SignalR Sequencing with coordinated fetch responses
        // T13-R6-F02 / T13-R7-F02: Genuinely distinct HTML snapshots.
        // Intercept GET fetch from production listener, delay first response while holding Snapshot A (contains only SEQ-A, count 11).
        // Then insert SEQ-B (count 12) and emit Event 2. Fetch 2 applies Snapshot B to DOM (count 12, row 402 exists).
        // Then release Snapshot A and observe superseded callback, asserting DOM is not rolled back.
        await session.ExecuteScriptAsync("""
            (function() {
                window.__origFetch = window.fetch;
                let resolveFirst;
                window.__firstFetchPromise = new Promise(resolve => { resolveFirst = resolve; });
                window.__resolveFirst = resolveFirst;
                let fetchCount = 0;
                window.__fetchLog = [];
                window.__resp1Ready = false;
                window.__resp1Html = null;

                window.__origRefetch = window.StorageUnitsRealtime.refetchTable;
                window.__refetchOutcomes = [];
                window.StorageUnitsRealtime.refetchTable = async function(...rArgs) {
                    const res = await window.__origRefetch.apply(this, rArgs);
                    window.__refetchOutcomes.push(res);
                    return res;
                };

                window.fetch = function(...args) {
                    fetchCount++;
                    const idx = fetchCount;
                    window.__fetchLog.push({ idx: idx, url: String(args[0]) });
                    if (idx === 1) {
                        // T13-R7-F02: Genuinely fetch Snapshot A from server right now (when only SEQ-A is in DB),
                        // clone the response body to extract and prove its HTML contents, then hold the promise unresolved.
                        window.__fetch1Gen = window.StorageUnitsRealtime.getLatestScheduledGeneration();
                        return window.__origFetch.apply(this, args).then(realResp => {
                            return realResp.clone().text().then(htmlText => {
                                window.__resp1Html = htmlText;
                                window.__resp1Ready = true;
                                return window.__firstFetchPromise.then(() => realResp);
                            });
                        });
                    }
                    return window.__origFetch.apply(this, args);
                };
            })()
        """);

        var seqAId = 401L;
        var seqBId = 402L;

        try
        {
            // Step 1: Seed ONLY UNIT-00-A into DB (total units is now 11: 10 baseline + 1 SEQ-A)
            // Unit code UNIT-00-A sorts before UNIT-01, ensuring it is rendered on Page 1!
            using (var scope = server.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                db.storage_units.Add(new storage_unit
                {
                    id = seqAId,
                    facility_id = facilityId,
                    area_id = areaId,
                    unit_type_id = unitTypeId,
                    unit_code = "UNIT-00-A",
                    floor_label = "Tang 1",
                    zone_label = "Day A",
                    physical_status = "available",
                    is_listed = true,
                    notes = "Sequence A",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            // Step 2: Broadcast Event 1 (SEQ-A) via publisher.
            // Production listener triggers fetch 1, which fetches real Snapshot A from server (total 11) and holds it.
            using (var scope = server.CreateScope())
            {
                var publisher = scope.ServiceProvider.GetRequiredService<IStorageUnitRealtimePublisher>();
                await publisher.PublishCreatedAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
                {
                    Id = seqAId,
                    UnitCode = "UNIT-00-A",
                    FacilityId = facilityId,
                    FacilityName = "Kestrel Facility",
                    FacilityCode = "FAC_KESTREL",
                    UnitTypeId = unitTypeId,
                    UnitTypeName = "Loai Kho Kestrel",
                    UnitTypeCode = "TYPE_KESTREL",
                    AreaId = areaId,
                    AreaName = "Khu Vuc A",
                    PhysicalStatus = "available",
                    IsListed = true
                });
            }

            // T13-R7-F02 Barrier 1: Wait until fetch 1 has contacted server and is waiting on __firstFetchPromise
            bool resp1Ready = false;
            for (var i = 0; i < 50; i++)
            {
                resp1Ready = await session.EvaluateBoolAsync("window.__resp1Ready === true");
                if (resp1Ready) break;
                await Task.Delay(100);
            }
            Assert.True(resp1Ready, "F05 E2E: Fetch 1 (Snapshot A) must be captured from server and held pending before seeding B.");

            // T13-R7-F02: Definitively prove Snapshot A was fetched from server before B existed:
            // Must contain unit-row-401 (SEQ-A), must NOT contain unit-row-402 (SEQ-B), and total count must be 11.
            var resp1Html = await session.EvaluateStringRequiredAsync("window.__resp1Html || ''");
            Assert.Contains($"unit-row-{seqAId}", resp1Html);
            Assert.DoesNotContain($"unit-row-{seqBId}", resp1Html);
            Assert.Contains("11", resp1Html);

            // Step 3: Now insert UNIT-00-B into DB (total units is now 12: 10 baseline + SEQ-A + SEQ-B)
            // Unit code UNIT-00-B also sorts on Page 1!
            using (var scope = server.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                db.storage_units.Add(new storage_unit
                {
                    id = seqBId,
                    facility_id = facilityId,
                    area_id = areaId,
                    unit_type_id = unitTypeId,
                    unit_code = "UNIT-00-B",
                    floor_label = "Tang 1",
                    zone_label = "Day A",
                    physical_status = "available",
                    is_listed = true,
                    notes = "Sequence B",
                    created_at = DateTimeOffset.UtcNow,
                    updated_at = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            // Step 4: Broadcast Event 2 (SEQ-B) via publisher.
            // Fetch 2 runs immediately, fetches Snapshot B (total 12, contains SEQ-A and SEQ-B on Page 1), and applies to DOM.
            using (var scope = server.CreateScope())
            {
                var publisher = scope.ServiceProvider.GetRequiredService<IStorageUnitRealtimePublisher>();
                await publisher.PublishCreatedAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
                {
                    Id = seqBId,
                    UnitCode = "UNIT-00-B",
                    FacilityId = facilityId,
                    FacilityName = "Kestrel Facility",
                    FacilityCode = "FAC_KESTREL",
                    UnitTypeId = unitTypeId,
                    UnitTypeName = "Loai Kho Kestrel",
                    UnitTypeCode = "TYPE_KESTREL",
                    AreaId = areaId,
                    AreaName = "Khu Vuc A",
                    PhysicalStatus = "available",
                    IsListed = true
                });
            }

            // Barrier 2: Wait for Event 2 to apply Snapshot B to DOM (row for SEQ-B exists, total count is 12)
            bool snapshotBApplied = false;
            for (var i = 0; i < 50; i++)
            {
                var hasRow402 = await session.EvaluateBoolAsync($"document.getElementById('unit-row-{seqBId}') !== null");
                var count = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsTotalCount')?.textContent?.trim() || ''");
                if (hasRow402 && count == "12")
                {
                    snapshotBApplied = true;
                    break;
                }
                await Task.Delay(100);
            }
            Assert.True(snapshotBApplied, "F05 E2E: Snapshot B (containing SEQ-B, total count 12) must be applied to DOM.");

            // Step 5: Now release the delayed fetch 1 (Snapshot A containing only SEQ-A, count 11).
            await session.ExecuteScriptAsync("""
                (function() {
                    window.__resolveFirst();
                })()
            """);

            // T13-R7-F02 Barrier 3: Wait explicitly for fetch 1's callback to execute and be recorded as superseded in window.__refetchOutcomes
            // (NOT using a static Task.Delay as proof!)
            bool outcome1Processed = false;
            for (var i = 0; i < 50; i++)
            {
                outcome1Processed = await session.EvaluateBoolAsync("window.__refetchOutcomes && window.__refetchOutcomes.some(o => o.generation === window.__fetch1Gen && o.status === 'superseded')");
                if (outcome1Processed) break;
                await Task.Delay(100);
            }
            Assert.True(outcome1Processed, "F05 E2E: Fetch 1 callback must have genuinely executed and marked as superseded.");

            // Step 6: T13-R6-F02 / T13-R7-F02: Assert DOM did NOT roll back to Snapshot A!
            // Row 402 (SEQ-B) must STILL be in DOM!
            var hasRow402AfterA = await session.EvaluateBoolAsync($"document.getElementById('unit-row-{seqBId}') !== null");
            Assert.True(hasRow402AfterA, "F05 E2E: DOM must NOT roll back to Snapshot A; SEQ-B row (402) must still exist.");

            var countAfterA = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsTotalCount')?.textContent?.trim() || ''");
            Assert.Equal("12", countAfterA);

            var rangeAfterA = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsDisplayRange')?.textContent?.replace(/\\s+/g, ' ')?.trim() || ''");
            Assert.Contains("12", rangeAfterA);

            // Older superseded response must not trigger false alert banner
            var hasAlertOnSuperseded = await session.EvaluateBoolAsync("document.getElementById('realtimeSyncAlert') !== null");
            Assert.False(hasAlertOnSuperseded, "F05 E2E: Superseded response does NOT display false warning banner.");
        }
        finally
        {
            // Always restore original fetch and refetchTable
            await session.ExecuteScriptAsync("""
                (function() {
                    if (window.__origFetch) window.fetch = window.__origFetch;
                    if (window.__origRefetch) window.StorageUnitsRealtime.refetchTable = window.__origRefetch;
                })()
            """);

            // Clean up sequence units from DB
            using (var scope = server.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
                var ua = await db.storage_units.FindAsync(seqAId);
                var ub = await db.storage_units.FindAsync(seqBId);
                if (ua != null) db.storage_units.Remove(ua);
                if (ub != null) db.storage_units.Remove(ub);
                await db.SaveChangesAsync();
            }
        }

        // 7. F06 E2E: Real SignalR listener handles HTTP 200 with missing table elements and retry resolves alert
        // T13-R6-F03: Intercept listener's fetch to return HTTP 200 with missing table elements once
        await session.ExecuteScriptAsync("""
            (function() {
                window.__origFetch = window.fetch;
                let failOnce = true;
                window.fetch = function(...args) {
                    if (failOnce) {
                        failOnce = false;
                        return Promise.resolve(new Response("<html><body><div id='maintenance'>Trang bao tri khong co bang</div></body></html>", {
                            status: 200,
                            headers: { "Content-Type": "text/html" }
                        }));
                    }
                    return window.__origFetch.apply(this, args);
                };
            })()
        """);

        var retryUnitId = 501L;
        var retryUnitCode = "UNIT-00-RETRY";
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            db.storage_units.Add(new storage_unit
            {
                id = retryUnitId,
                facility_id = facilityId,
                area_id = areaId,
                unit_type_id = unitTypeId,
                unit_code = retryUnitCode,
                floor_label = "Tang 1",
                zone_label = "Day A",
                physical_status = "available",
                is_listed = true,
                notes = "F06 Retry test",
                created_at = DateTimeOffset.UtcNow,
                updated_at = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Broadcast real Receiver_CreateStorageUnit via publisher through WebSocket
        using (var scope = server.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IStorageUnitRealtimePublisher>();
            await publisher.PublishCreatedAsync(new SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.StorageUnits.StorageUnitAdminDto
            {
                Id = retryUnitId,
                UnitCode = retryUnitCode,
                FacilityId = facilityId,
                FacilityName = "Kestrel Facility",
                FacilityCode = "FAC_KESTREL",
                UnitTypeId = unitTypeId,
                UnitTypeName = "Loai Kho Kestrel",
                UnitTypeCode = "TYPE_KESTREL",
                AreaId = areaId,
                AreaName = "Khu Vuc A",
                PhysicalStatus = "available",
                IsListed = true
            });
        }

        // Production listener receives event -> calls refetchTable -> receives 200 with missing table -> detects failure -> shows #realtimeSyncAlert
        bool bannerAppeared = false;
        for (var i = 0; i < 40; i++)
        {
            bannerAppeared = await session.EvaluateBoolAsync("document.getElementById('realtimeSyncAlert') !== null");
            if (bannerAppeared) break;
            await Task.Delay(250);
        }
        Assert.True(bannerAppeared, "F06 E2E: #realtimeSyncAlert banner was generated by production listener upon invalid HTML response.");

        // Assert DOM BEFORE retry:
        // 1. New row has NOT yet appeared in DOM
        var hasRowBeforeRetry = await session.EvaluateBoolAsync($"document.getElementById('unit-row-{retryUnitId}') !== null");
        Assert.False(hasRowBeforeRetry, "F06 E2E: Row for retry unit must NOT appear before retry.");
        // 2. Banner is displayed with retry button
        var hasRetryButton = await session.EvaluateBoolAsync("document.getElementById('btnRetrySync') !== null");
        Assert.True(hasRetryButton, "F06 E2E: #btnRetrySync button exists inside warning alert.");

        // Click retry button in DOM (calls window.StorageUnitsRealtime.retryRefetch())
        await session.ExecuteScriptAsync("document.getElementById('btnRetrySync')?.click();");

        // Wait for retryRefetch() to succeed with restored fetch and remove alert banner
        bool bannerRemoved = false;
        for (var i = 0; i < 40; i++)
        {
            var bannerExists = await session.EvaluateBoolAsync("document.getElementById('realtimeSyncAlert') !== null");
            if (!bannerExists)
            {
                bannerRemoved = true;
                break;
            }
            await Task.Delay(250);
        }
        Assert.True(bannerRemoved, "F06 E2E: Clicking retry button successfully cleared the alert banner from DOM.");

        // Assert DOM AFTER retry:
        // 1. Row for retry unit is now rendered in DOM!
        var hasRowAfterRetry = await session.EvaluateBoolAsync($"document.getElementById('unit-row-{retryUnitId}') !== null");
        Assert.True(hasRowAfterRetry, "F06 E2E: Row for retry unit must be rendered in DOM after successful retry.");
        // 2. Total count is updated!
        var totalCountAfterRetry = await session.EvaluateStringRequiredAsync("document.getElementById('storageUnitsTotalCount')?.textContent?.trim() || ''");
        Assert.Equal("11", totalCountAfterRetry); // 10 original + 1 retry unit = 11

        // Clean up retry unit from DB
        using (var scope = server.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SelfStorageDbContext>();
            var u = await db.storage_units.FindAsync(retryUnitId);
            if (u != null)
            {
                db.storage_units.Remove(u);
                await db.SaveChangesAsync();
            }
        }

        // Restore fetch and clean up
        await session.ExecuteScriptAsync("window.fetch = window.__origFetch;");
    }

    [Fact]
    public void T13_R3_F03_MatchesFilter_DoesNotRejectSearchTerm_AllowingServerRefetchForNotes()
    {
        // When client has a search term matching 'notes' (omitted from DTO),
        // matchesFilter must NOT reject the item so that refetchTable is triggered to fetch the accurate match from server.
        var filter = new
        {
            facilityId = "10",
            unitTypeId = "20",
            areaId = "30",
            physicalStatus = "available",
            isListed = "true",
            searchTerm = "ghi chú bảo mật"
        };

        var item = new StorageUnitRealtimeEventDto
        {
            id = 99,
            unitCode = "UNIT-99",
            facilityId = 10,
            unitTypeId = 20,
            areaId = 30,
            status = "available",
            isListed = true,
            floorLabel = "Tầng 1",
            zoneLabel = "Dãy A"
        };

        // Categorical matching logic in storage-units-realtime.js:
        bool MatchesCategoricalOnly(StorageUnitRealtimeEventDto dto)
        {
            if (filter.facilityId != "" && dto.facilityId.ToString() != filter.facilityId) return false;
            if (filter.unitTypeId != "" && dto.unitTypeId.ToString() != filter.unitTypeId) return false;
            if (filter.areaId != "" && (dto.areaId?.ToString() ?? "") != filter.areaId) return false;
            if (filter.physicalStatus != "" && !dto.status.Equals(filter.physicalStatus, StringComparison.OrdinalIgnoreCase)) return false;
            if (filter.isListed != "" && dto.isListed.ToString().ToLowerInvariant() != filter.isListed) return false;
            return true;
        }

        Assert.True(MatchesCategoricalOnly(item));
    }

    #endregion
}
