using System.Net;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SelfStorageManagementSystem.WebTests.Fixtures;

namespace SelfStorageManagementSystem.WebTests;

public class LandingPageAndBootstrapTests : IClassFixture<WebTestFixture>
{
    private readonly WebTestFixture _factory;
    private readonly HttpClient _client;

    public LandingPageAndBootstrapTests(WebTestFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task LandingPage_ReturnsSuccessAndHtml()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains("text/html", response.Content.Headers.ContentType.ToString());

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hệ Thống Quản Lý Kho Tự Quản Self Storage", html);
        Assert.Contains("Bootstrap 5.3.3", html);
        Assert.Contains("jQuery 3.7.1", html);
        Assert.Contains("/storageUnitHub", html);
    }

    [Fact]
    public async Task HostStartsWithoutJwtSecret()
    {
        // Ensure web host operates cleanly without any JWT configuration
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css", "text/css")]
    [InlineData("/lib/bootstrap/dist/js/bootstrap.bundle.min.js", "javascript")]
    [InlineData("/lib/jquery/dist/jquery.min.js", "javascript")]
    [InlineData("/lib/microsoft/signalr/dist/browser/signalr.min.js", "javascript")]
    [InlineData("/css/site.css", "text/css")]
    [InlineData("/js/site.js", "javascript")]
    public async Task StaticAssets_AreServedLocally(string assetUrl, string expectedContentTypeSubString)
    {
        // Act
        var response = await _client.GetAsync(assetUrl);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Contains(expectedContentTypeSubString, response.Content.Headers.ContentType.ToString());

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task SignalRHub_NegotiationEndpoint_ReturnsOk()
    {
        // Act - provide test authentication header so request is authenticated
        var request = new HttpRequestMessage(HttpMethod.Post, "/storageUnitHub/negotiate?negotiateVersion=1");
        request.Headers.Add("X-Test-Role", "storage_customer");
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("connectionId", json);
    }

    [Fact]
    public async Task CentralizedErrorHandling_NotFound_GetMethod_Returns404StatusCodeView()
    {
        // Act
        var response = await _client.GetAsync("/non-existent-route-404-test");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("404", html);
        Assert.Contains("Thông Báo Trạng Thái", html);
    }

    [Fact]
    public async Task CentralizedErrorHandling_NotFound_PostMethod_Returns404StatusCodeViewNot405()
    {
        // Act - Re-executed POST to non-existent route must return 404 with error view, NOT 405 Method Not Allowed
        var response = await _client.PostAsync("/non-existent-route-404-post-test", new StringContent("test"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("404", html);
        Assert.Contains("Thông Báo Trạng Thái", html);
    }

    [Fact]
    public async Task CentralizedErrorHandling_ErrorAction_HandlesPostRequestAndPreserves500()
    {
        // Act - Direct POST to Error action must succeed and return HTTP 500
        var response = await _client.PostAsync("/Home/Error", null);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("500", html);
        Assert.Contains("Lỗi Xử Lý Yêu Cầu", html);
    }

    [Fact]
    public void Security_NoHardcodedHmacSecretInAppSettings()
    {
        // Directly inspect source configuration files on disk, not merged runtime configuration with environment variables
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "PRN222_SE1905_VOTHAIHOANGKHOI_ASM01.sln")))
        {
            currentDir = currentDir.Parent;
        }

        Assert.NotNull(currentDir);

        var webConfigPath = Path.Combine(currentDir.FullName, "src", "SelfStorageManagementSystem.Web", "appsettings.json");
        Assert.True(File.Exists(webConfigPath), $"appsettings.json not found at {webConfigPath}");

        var jsonText = File.ReadAllText(webConfigPath);
        using (var doc = System.Text.Json.JsonDocument.Parse(jsonText))
        {
            if (doc.RootElement.TryGetProperty("CheckIn", out var checkInElement) &&
                checkInElement.TryGetProperty("PinHmacSecret", out var pinSecretElement))
            {
                var pinSecret = pinSecretElement.GetString();
                Assert.True(string.IsNullOrEmpty(pinSecret), 
                    "CheckIn:PinHmacSecret must not contain a hardcoded secret in delivered appsettings.json.");
            }
        }

        var devConfigPath = Path.Combine(currentDir.FullName, "src", "SelfStorageManagementSystem.Web", "appsettings.Development.json");
        if (File.Exists(devConfigPath))
        {
            var devJsonText = File.ReadAllText(devConfigPath);
            using var devDoc = System.Text.Json.JsonDocument.Parse(devJsonText);
            if (devDoc.RootElement.TryGetProperty("CheckIn", out var devCheckIn) &&
                devCheckIn.TryGetProperty("PinHmacSecret", out var devPinSecret))
            {
                var devSecret = devPinSecret.GetString();
                Assert.True(string.IsNullOrEmpty(devSecret), 
                    "CheckIn:PinHmacSecret must not contain a hardcoded secret in delivered appsettings.Development.json.");
            }
        }
    }

    [Fact]
    public void MvcWebApp_DoesNotReferenceDataAccessOrApiOrWpf()
    {
        // Get the loaded assembly for the Web project
        var webAssembly = typeof(Program).Assembly;
        var referencedAssemblies = webAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        // Must reference BusinessLogic
        Assert.Contains("SelfStorageManagementSystem.BusinessLogic", referencedAssemblies);

        // Must NOT reference DataAccess, Presentation, or WpfClient directly
        Assert.DoesNotContain("SelfStorageManagementSystem.DataAccess", referencedAssemblies);
        Assert.DoesNotContain("SelfStorageManagementSystem.Presentation", referencedAssemblies);
        Assert.DoesNotContain("SelfStorageManagementSystem.WpfClient", referencedAssemblies);
    }
}

