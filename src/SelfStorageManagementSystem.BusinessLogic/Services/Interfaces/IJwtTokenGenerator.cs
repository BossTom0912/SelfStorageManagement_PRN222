using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IJwtTokenGenerator
{
    (string Token, DateTimeOffset ExpiresAt) GenerateToken(user user, IEnumerable<string> roleCodes);
}
