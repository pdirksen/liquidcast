namespace Liquidcast.Api.Models;

public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    /// <summary>Bumped on logout to invalidate every JWT issued before that point.</summary>
    public int TokenVersion { get; set; }
}
