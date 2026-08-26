using System.Collections.Concurrent;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Infrastructure;

/// <summary>Records NIA/proxy response log entries in memory instead of writing to SQL Server.</summary>
public class FakeVerificationResponseLogService : IVerificationResponseLogService
{
    public ConcurrentQueue<NiaResponseLogEntry> NiaEntries { get; } = new();
    public ConcurrentQueue<ProxyResponseLogEntry> ProxyEntries { get; } = new();

    public Task LogNiaResponseAsync(NiaResponseLogEntry entry, CancellationToken ct = default)
    {
        NiaEntries.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task LogProxyResponseAsync(ProxyResponseLogEntry entry, CancellationToken ct = default)
    {
        ProxyEntries.Enqueue(entry);
        return Task.CompletedTask;
    }
}
