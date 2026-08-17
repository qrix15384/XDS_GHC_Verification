using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Options;

namespace XDS_GHC_Verification.Services;

/// <summary>
/// Bootstraps the first Admin account so there's a way to log in before any
/// ProxyUsers rows exist. Extracted out of Program.cs so it's unit-testable.
/// </summary>
public static class ProxyUserSeeder
{
    public static async Task SeedAdminIfEmptyAsync(
        IProxyUserService users,
        IPasswordHasher<ProxyUser> passwordHasher,
        IOptions<ServiceAuthOptions> serviceAuthOptions,
        CancellationToken ct = default)
    {
        if (await users.CountAsync(ct) > 0)
        {
            return;
        }

        var opts = serviceAuthOptions.Value;
        var seedUser = new ProxyUser { Username = opts.AuthUsername, Role = "Admin" };
        var passwordHash = passwordHasher.HashPassword(seedUser, opts.AuthPassword);

        await users.CreateAsync(opts.AuthUsername, passwordHash, "Admin", subscriberId: null, ct);
    }
}
