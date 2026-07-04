namespace Liquidcast.Api.Models;

public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    /// <summary>Bumped on logout to invalidate every JWT issued before that point.</summary>
    public int TokenVersion { get; set; }

    /// <summary>True for a freshly-seeded admin account; forces a password change on
    /// first login. Cleared once the password is changed. Existing installs upgrading
    /// via migration keep this false so they aren't unexpectedly interrupted.</summary>
    public bool MustChangePassword { get; set; } = true;
}
