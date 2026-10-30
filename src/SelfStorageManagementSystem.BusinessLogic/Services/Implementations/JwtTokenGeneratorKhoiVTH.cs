using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class JwtTokenGeneratorKhoiVTH : IJwtTokenGenerator
{
    private readonly JwtOptions _options;

    public JwtTokenGeneratorKhoiVTH(IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options), "JwtOptions cannot be null.");

        if (string.IsNullOrWhiteSpace(_options.Key) || Encoding.UTF8.GetByteCount(_options.Key) < JwtOptions.MinimumKeyLengthBytes)
        {
            throw new InvalidOperationException(
                $"JWT signing key is missing or shorter than {JwtOptions.MinimumKeyLengthBytes} bytes (256 bits). " +
                "Configure a secure key via User Secrets or the JWT_SECRET_KEY environment variable.");
        }
    }

    public (string Token, DateTimeOffset ExpiresAt) GenerateToken(user user, IEnumerable<string> roleCodes)
    {
        ArgumentNullException.ThrowIfNull(user);
        return GenerateToken(user.id, user.email, roleCodes);
    }

    public (string Token, DateTimeOffset ExpiresAt) GenerateToken(long userId, string email, IEnumerable<string> roleCodes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var expiryMinutes = _options.ExpiryMinutes > 0 ? _options.ExpiryMinutes : 60;
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (roleCodes != null)
        {
            foreach (var role in roleCodes.Distinct())
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return (tokenString, expiresAt);
    }
}
