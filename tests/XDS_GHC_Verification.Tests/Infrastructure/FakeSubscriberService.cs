using System.Collections.Concurrent;
using XDS_GHC_Verification.Models;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Infrastructure;

/// <summary>In-memory stand-in for SubscriberService — no real SQL Server needed for tests.</summary>
public class FakeSubscriberService : ISubscriberService
{
    private readonly ConcurrentDictionary<int, Subscriber> _subscribers = new();
    private int _nextId;

    public Task<Subscriber?> FindByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(_subscribers.GetValueOrDefault(id));

    public Task<Subscriber?> FindByNameAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(_subscribers.Values.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Subscriber>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Subscriber>>(_subscribers.Values.OrderBy(s => s.Name).ToList());

    public Task<Subscriber> CreateAsync(string name, CancellationToken ct = default)
    {
        var subscriber = new Subscriber
        {
            Id = Interlocked.Increment(ref _nextId),
            Name = name,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _subscribers[subscriber.Id] = subscriber;
        return Task.FromResult(subscriber);
    }

    public Task UpdateAsync(int id, string name, bool isActive, CancellationToken ct = default)
    {
        if (_subscribers.TryGetValue(id, out var subscriber))
        {
            subscriber.Name = name;
            subscriber.IsActive = isActive;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        _subscribers.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
