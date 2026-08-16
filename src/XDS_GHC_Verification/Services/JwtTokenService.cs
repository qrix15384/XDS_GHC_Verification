using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Options;

namespace XDS_GHC_Verification.Services;

public record IssuedToken(string Token, DateTime ExpiresAtUtc);

/// <summary>Issues JWTs for logged-in ProxyUsers — used to authorize the admin endpoints (user management, transactions).</summary>
public class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _opts = options.Value;

    public IssuedToken GenerateToken(ProxyUser user)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_opts.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.Role),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
