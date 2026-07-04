using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Liquidcast.Api.Services;

public class JwtTokenService
{
    public const string Issuer = "liquidcast";
    public const string Audience = "liquidcast";

    private readonly SymmetricSecurityKey _key;

    public JwtTokenService(IConfiguration config)
    {
        var secret = config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            secret = "liquidcast-dev-secret-change-me-please-0123456789"; // dev fallback
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public SymmetricSecurityKey Key => _key;

    public string Create(string username)
    {
        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(ClaimTypes.Name, username),
        };
        var token = new JwtSecurityToken(Issuer, Audience, claims,
            expires: DateTime.UtcNow.AddDays(7), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
