using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly IConfiguration _configuration;
    private const string DefaultDevKey = "SelfStorageSecretKeyForJwtSigningMustBeLongEnough2026!";
    private const string DefaultIssuer = "SelfStoragePRN222";
    private const string DefaultAudience = "SelfStoragePRN222Clients";
    private const int DefaultExpiryMinutes = 60;

    public JwtTokenGenerator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTimeOffset ExpiresAt) GenerateToken(user user, IEnumerable<string> roleCodes)
    {
        var secretKey = _configuration["Jwt:Key"]
                        ?? Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
                        ?? DefaultDevKey;

        var issuer = _configuration["Jwt:Issuer"] ?? DefaultIssuer;
        var audience = _configuration["Jwt:Audience"] ?? DefaultAudience;
        var expiryMinutesStr = _configuration["Jwt:ExpiryMinutes"];
        var expiryMinutes = int.TryParse(expiryMinutesStr, out var m) && m > 0 ? m : DefaultExpiryMinutes;

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.id.ToString()),
            new(ClaimTypes.Email, user.email),
            new(JwtRegisteredClaimNames.Sub, user.id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in roleCodes.Distinct())
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return (tokenString, expiresAt);
    }
}
