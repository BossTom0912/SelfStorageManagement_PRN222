using System.Net;
using System.Text.RegularExpressions;
using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.WebTests.Fixtures;

namespace SelfStorageManagementSystem.WebTests;

public class BlazorStorageUnitsTests : IClassFixture<CookieAuthWebTestFixture>
{
    private readonly CookieAuthWebTestFixture _factory;
    public BlazorStorageUnitsTests(CookieAuthWebTestFixture factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_IsChallengedBeforeBlazorRendering()
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/Blazor/StorageUnits");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString);
        using var negotiate = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        negotiate.Headers.Accept.ParseAdd("application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(negotiate)).StatusCode);
    }

    [Theory]
    [InlineData(RoleConstants.StorageCustomer, false)]
    [InlineData(RoleConstants.FacilityStaff, true)]
    [InlineData(RoleConstants.SystemAdministrator, true)]
    public async Task Host_EnforcesRoleAndRendersInteractiveComponent(string role, bool allowed)
    {
        var email = $"blazor_{Guid.NewGuid():N}@example.test";
        const string password = "BlazorIntegrationTest123!";
        await _factory.SeedUserAsync(email, password, role);
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var match = Regex.Match(loginHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        var loginResponse = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        var response = await client.GetAsync("/Blazor/StorageUnits");
        using var negotiate = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        negotiate.Headers.Accept.ParseAdd("application/json");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.SendAsync(negotiate)).StatusCode);
        if (!allowed)
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Forbidden", response.Headers.Location?.OriginalString);
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("blazor-storage-units", html);
        Assert.Contains("blazor.server.js", html);
        Assert.Contains("blazor-search", html);
        Assert.Contains("base href=\"/\"", html);
        Assert.DoesNotContain("@onclick", html);
        if (role == RoleConstants.SystemAdministrator) Assert.Contains("Thêm ô kho", html);
        else Assert.DoesNotContain("Thêm ô kho", html);
    }
}
