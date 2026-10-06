using SelfStorageManagementSystem.WpfClient.Models;

namespace SelfStorageManagementSystem.WpfClient.Services;

public static class SessionStore
{
    public static string? AccessToken { get; private set; }
    public static CurrentUserResponse? CurrentUser { get; private set; }
    public static DateTimeOffset? TokenExpiresAt { get; private set; }

    public static bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken) &&
                                     TokenExpiresAt.HasValue &&
                                     TokenExpiresAt.Value > DateTimeOffset.UtcNow;

    public static bool IsSystemAdministrator =>
        CurrentUser?.Roles.Contains("system_administrator") == true;

    public static void SetSession(string token, DateTimeOffset expiresAt, CurrentUserResponse? user = null)
    {
        AccessToken = token;
        TokenExpiresAt = expiresAt;
        CurrentUser = user;
    }

    public static void SetCurrentUser(CurrentUserResponse user)
    {
        CurrentUser = user;
    }

    public static void Clear()
    {
        AccessToken = null;
        CurrentUser = null;
        TokenExpiresAt = null;
    }
}
