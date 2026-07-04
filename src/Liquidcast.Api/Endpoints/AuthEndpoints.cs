using Liquidcast.Api.Persistence;
using Liquidcast.Api.Models;
using Liquidcast.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public static class AuthEndpoints
{
    public const string CookieName = "lc_token";

    public record LoginRequest(string Username, string Password);
    public record ChangeProfileRequest(string CurrentPassword, string NewUsername);
    public record ChangeCredentialsRequest(string CurrentPassword, string NewPassword);

    private static void IssueCookie(HttpContext http, JwtTokenService jwt, AdminUser user)
    {
        var token = jwt.Create(user.Username, user.TokenVersion);
        http.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/",
        });
    }

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (LoginRequest req, AppDbContext db, JwtTokenService jwt, HttpContext http) =>
        {
            var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == req.Username);
            if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Results.Unauthorized();

            IssueCookie(http, jwt, user);
            return Results.Ok(new { username = user.Username, mustChangePassword = user.MustChangePassword });
        }).RequireRateLimiting("login");

        g.MapPost("/logout", async (HttpContext http, AppDbContext db) =>
        {
            // Bump TokenVersion so every previously issued JWT for this user — not just
            // the cookie being cleared here — fails validation from this point on.
            var username = http.User.Identity?.Name;
            if (username is not null)
            {
                var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
                if (user is not null)
                {
                    user.TokenVersion++;
                    await db.SaveChangesAsync();
                }
            }
            http.Response.Cookies.Delete(CookieName);
            return Results.Ok();
        }).RequireAuthorization();

        g.MapGet("/me", async (HttpContext http, AppDbContext db) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();
            var user = await db.AdminUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == http.User.Identity!.Name);
            if (user is null)
                return Results.Unauthorized();
            return Results.Ok(new { username = user.Username, mustChangePassword = user.MustChangePassword });
        });

        g.MapPut("/profile", async (ChangeProfileRequest req, AppDbContext db, JwtTokenService jwt, HttpContext http) =>
        {
            var username = http.User.Identity!.Name!;
            var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null || !PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
                return Results.BadRequest(new { error = "Current password is incorrect." });

            var newUsername = req.NewUsername.Trim();
            if (string.IsNullOrWhiteSpace(newUsername))
                return Results.BadRequest(new { error = "Username cannot be blank." });

            user.Username = newUsername;
            user.TokenVersion++; // claims embed the username — old tokens must die
            await db.SaveChangesAsync();

            IssueCookie(http, jwt, user); // keep the current session logged in under the new name
            return Results.Ok(new { username = user.Username });
        }).RequireAuthorization();

        g.MapPut("/credentials", async (ChangeCredentialsRequest req, AppDbContext db, JwtTokenService jwt, HttpContext http) =>
        {
            var username = http.User.Identity!.Name!;
            var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null || !PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
                return Results.BadRequest(new { error = "Current password is incorrect." });

            if (req.NewPassword.Length < 8)
                return Results.BadRequest(new { error = "New password must be at least 8 characters." });

            user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
            user.TokenVersion++; // invalidate every other session
            user.MustChangePassword = false;
            await db.SaveChangesAsync();

            IssueCookie(http, jwt, user); // keep the current session logged in
            return Results.Ok(new { mustChangePassword = user.MustChangePassword });
        }).RequireAuthorization();
    }
}
