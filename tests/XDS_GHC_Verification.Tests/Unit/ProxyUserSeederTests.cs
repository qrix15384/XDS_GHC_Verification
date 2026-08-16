using Microsoft.AspNetCore.Identity;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Options;
using XDS_GHC_Verification.Services;
using XDS_GHC_Verification.Tests.Infrastructure;

namespace XDS_GHC_Verification.Tests.Unit;

public class ProxyUserSeederTests
{
    private static readonly IPasswordHasher<ProxyUser> Hasher = new PasswordHasher<ProxyUser>();

    [Fact]
    public async Task SeedAdminIfEmptyAsync_WhenNoUsersExist_CreatesOneAdmin()
    {
        var users = new FakeProxyUserService();
        var options = Microsoft.Extensions.Options.Options.Create(new ServiceAuthOptions
        {
            AuthUsername = "seed-admin",
            AuthPassword = "seed-password-123",
        });

        await ProxyUserSeeder.SeedAdminIfEmptyAsync(users, Hasher, options);

        var all = await users.ListAsync();
        var admin = Assert.Single(all);
        Assert.Equal("seed-admin", admin.Username);
        Assert.Equal("Admin", admin.Role);
        Assert.True(admin.IsActive);
        Assert.Equal(PasswordVerificationResult.Success, Hasher.VerifyHashedPassword(admin, admin.PasswordHash, "seed-password-123"));
    }

    [Fact]
    public async Task SeedAdminIfEmptyAsync_WhenUsersAlreadyExist_DoesNothing()
    {
        var users = new FakeProxyUserService();
        await users.CreateAsync("existing-user", "irrelevant-hash", "Standard");
        var options = Microsoft.Extensions.Options.Options.Create(new ServiceAuthOptions
        {
            AuthUsername = "seed-admin",
            AuthPassword = "seed-password-123",
        });

        await ProxyUserSeeder.SeedAdminIfEmptyAsync(users, Hasher, options);

        var all = await users.ListAsync();
        Assert.Single(all);
        Assert.Equal("existing-user", all[0].Username);
    }
}
