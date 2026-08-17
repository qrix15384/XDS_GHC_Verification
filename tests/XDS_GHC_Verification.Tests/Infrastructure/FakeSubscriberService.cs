using System.Collections.Concurrent;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Infrastructure;

/// <summary>
/// In-memory stand-in for SubscriberService — no real cross-database query
/// needed for tests. ISubscriberService is read-only in production (the
/// real subscriber list lives in an external system this app never writes
/// to); <see cref="Seed"/> is test-only setup standing in for rows that
/// would already exist there, not a production capability.
/// </summary>
public class FakeSubscriberService : ISubscriberService
{
    private readonly ConcurrentDictionary<int, Subscriber> _subscribers = new();
    private int _nextId;

    public Subscriber Seed(string name, bool isActive = true)
    {
        var subscriber = new Subscriber { Id = Interlocked.Increment(ref _nextId), Name = name, IsActive = isActive };
        _subscribers[subscriber.Id] = subscriber;
        return subscriber;
    }

    public Task<Subscriber?> FindByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(_subscribers.GetValueOrDefault(id));

    public Task<Subscriber?> FindByNameAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(_subscribers.Values.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Subscriber>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Subscriber>>(_subscribers.Values.OrderBy(s => s.Name).ToList());
}
