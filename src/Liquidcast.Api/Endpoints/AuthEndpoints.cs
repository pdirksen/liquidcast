using Liquidcast.Api.Data;
using Liquidcast.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Liquidcast.Api.Endpoints;

public static class AuthEndpoints
{
    public const string CookieName = "lc_token";

    public record LoginRequest(string Username, string Password);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        g.MapPost("/login", async (LoginRequest req, AppDbContext db, JwtTokenService jwt, HttpContext http) =>
        {
            var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == req.Username);
            if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Results.Unauthorized();

            var token = jwt.Create(user.Username);
            http.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = http.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/",
            });
            return Results.Ok(new { username = user.Username });
        });

        g.MapPost("/logout", (HttpContext http) =>
        {
            http.Response.Cookies.Delete(CookieName);
            return Results.Ok();
        });

        g.MapGet("/me", (HttpContext http) =>
            http.User.Identity?.IsAuthenticated == true
                ? Results.Ok(new { username = http.User.Identity!.Name })
                : Results.Unauthorized());
    }
}
