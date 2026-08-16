using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Unit;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService(JwtOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new JwtOptions
        {
            SigningKey = "unit-test-signing-key-at-least-32-characters",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpiryMinutes = 60,
        }));

    [Fact]
    public void GenerateToken_EncodesUsernameRoleAndId()
    {
        var service = CreateService();
        var user = new ProxyUser { Id = 42, Username = "alice", Role = "Admin" };

        var issued = service.GenerateToken(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
        Assert.Equal("alice", jwt.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
        Assert.Equal("42", jwt.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("Admin", jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public void GenerateToken_SetsExpiryAccordingToConfiguredMinutes()
    {
        var service = CreateService(new JwtOptions
        {
            SigningKey = "unit-test-signing-key-at-least-32-characters",
            ExpiryMinutes = 30,
        });
        var user = new ProxyUser { Id = 1, Username = "bob", Role = "Standard" };

        var before = DateTime.UtcNow;
        var issued = service.GenerateToken(user);

        Assert.InRange(issued.ExpiresAtUtc, before.AddMinutes(29), before.AddMinutes(31));
    }

    [Fact]
    public void GenerateToken_UsesConfiguredIssuerAndAudience()
    {
        var service = CreateService();
        var user = new ProxyUser { Id = 1, Username = "carol", Role = "Standard" };

        var issued = service.GenerateToken(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
        Assert.Equal("test-issuer", jwt.Issuer);
        Assert.Equal("test-audience", jwt.Audiences.Single());
    }
}
