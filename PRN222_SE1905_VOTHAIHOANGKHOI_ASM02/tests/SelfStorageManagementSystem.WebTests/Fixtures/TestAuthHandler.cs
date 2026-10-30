using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SelfStorageManagementSystem.WebTests.Fixtures;

/// <summary>
/// Authentication handler for simulating authenticated requests in integration tests
/// via the X-Test-Role and X-Test-User headers.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuthScheme";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var roleHeaders) || string.IsNullOrEmpty(roleHeaders))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userName = Request.Headers.TryGetValue("X-Test-User", out var userHeaders) && !string.IsNullOrEmpty(userHeaders)
            ? userHeaders.ToString()
            : "nguoidung.test@example.com";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "9999"),
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.Email, userName)
        };

        foreach (var role in roleHeaders.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
