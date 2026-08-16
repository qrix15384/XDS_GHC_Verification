using System.Collections.Concurrent;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Infrastructure;

/// <summary>In-memory stand-in for ProxyUserService — no real SQL Server needed for tests.</summary>
public class FakeProxyUserService : IProxyUserService
{
    private readonly ConcurrentDictionary<int, ProxyUser> _users = new();
    private int _nextId;

    public Task<ProxyUser?> FindByUsernameAsync(string username, CancellationToken ct = default) =>
        Task.FromResult(_users.Values.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)));

    public Task<ProxyUser?> FindByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(_users.GetValueOrDefault(id));

    public Task<IReadOnlyList<ProxyUser>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProxyUser>>(_users.Values.OrderBy(u => u.Username).ToList());

    public Task<ProxyUser> CreateAsync(string username, string passwordHash, string role, CancellationToken ct = default)
    {
        var user = new ProxyUser
        {
            Id = Interlocked.Increment(ref _nextId),
            Username = username,
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _users[user.Id] = user;
        return Task.FromResult(user);
    }

    public Task UpdateRoleAndStatusAsync(int id, string role, bool isActive, CancellationToken ct = default)
    {
        if (_users.TryGetValue(id, out var user))
        {
            user.Role = role;
            user.IsActive = isActive;
        }
        return Task.CompletedTask;
    }

    public Task UpdatePasswordHashAsync(int id, string passwordHash, CancellationToken ct = default)
    {
        if (_users.TryGetValue(id, out var user))
        {
            user.PasswordHash = passwordHash;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        _users.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(_users.Count);

    public Task<int> CountActiveAdminsAsync(CancellationToken ct = default) =>
        Task.FromResult(_users.Values.Count(u => u.Role == "Admin" && u.IsActive));
}
