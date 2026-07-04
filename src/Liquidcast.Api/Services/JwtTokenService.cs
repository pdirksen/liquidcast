using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Liquidcast.Api.Services;

public class JwtTokenService
{
    public const string Issuer = "liquidcast";
    public const string Audience = "liquidcast";

    private readonly SymmetricSecurityKey _key;

    public JwtTokenService(string secret)
    {
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>
    /// Uses Jwt:Secret from config if set (min 32 chars); otherwise generates a
    /// random key on first run and persists it under the data directory so
    /// existing sessions survive restarts.
    /// </summary>
    public static string ResolveSecret(IConfiguration config, string dataPath)
    {
        var configured = config["Jwt:Secret"];
        if (!string.IsNullOrWhiteSpace(configured) && configured.Length >= 32)
            return configured;

        var secretPath = Path.Combine(dataPath, "jwt.secret");
        if (File.Exists(secretPath))
        {
            var existing = File.ReadAllText(secretPath).Trim();
            if (existing.Length >= 32)
                return existing;
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(secretPath, generated);
        return generated;
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
