using System.Collections.Concurrent;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Infrastructure;

/// <summary>
/// In-memory stand-in for ProxyUserService — no real SQL Server needed for
/// tests. Takes a FakeSubscriberService to resolve SubscriberName, mirroring
/// the LEFT JOIN the real service does against dbo.Subscribers.
/// </summary>
public class FakeProxyUserService(FakeSubscriberService? subscribers = null) : IProxyUserService
{
    private readonly ConcurrentDictionary<int, ProxyUser> _users = new();
    private int _nextId;

    private async Task<string?> ResolveSubscriberNameAsync(int? subscriberId, CancellationToken ct)
    {
        if (subscriberId is null || subscribers is null) return null;
        return (await subscribers.FindByIdAsync(subscriberId.Value, ct))?.Name;
    }

    private async Task<ProxyUser?> WithSubscriberNameAsync(ProxyUser? user, CancellationToken ct)
    {
        if (user is not null)
        {
            user.SubscriberName = await ResolveSubscriberNameAsync(user.SubscriberId, ct);
        }
        return user;
    }

    public async Task<ProxyUser?> FindByUsernameAsync(string username, CancellationToken ct = default) =>
        await WithSubscriberNameAsync(
            _users.Values.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)),
            ct);

    public async Task<ProxyUser?> FindByIdAsync(int id, CancellationToken ct = default) =>
        await WithSubscriberNameAsync(_users.GetValueOrDefault(id), ct);

    public async Task<IReadOnlyList<ProxyUser>> ListAsync(CancellationToken ct = default)
    {
        var all = _users.Values.OrderBy(u => u.Username).ToList();
        foreach (var user in all)
        {
            user.SubscriberName = await ResolveSubscriberNameAsync(user.SubscriberId, ct);
        }
        return all;
    }

    public async Task<ProxyUser> CreateAsync(string username, string passwordHash, string role, int? subscriberId, CancellationToken ct = default)
    {
        var user = new ProxyUser
        {
            Id = Interlocked.Increment(ref _nextId),
            Username = username,
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            SubscriberId = subscriberId,
            SubscriberName = await ResolveSubscriberNameAsync(subscriberId, ct),
        };
        _users[user.Id] = user;
        return user;
    }

    public Task UpdateRoleAndStatusAsync(int id, string role, bool isActive, int? subscriberId, CancellationToken ct = default)
    {
        if (_users.TryGetValue(id, out var user))
        {
            user.Role = role;
            user.IsActive = isActive;
            user.SubscriberId = subscriberId;
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

    public Task<int> CountBySubscriberIdAsync(int subscriberId, CancellationToken ct = default) =>
        Task.FromResult(_users.Values.Count(u => u.SubscriberId == subscriberId));
}
