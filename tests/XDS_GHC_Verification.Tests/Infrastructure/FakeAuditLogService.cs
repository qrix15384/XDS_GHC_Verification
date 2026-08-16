using System.Collections.Concurrent;
using XDS_GHC_Verification.Services;

namespace XDS_GHC_Verification.Tests.Infrastructure;

/// <summary>Records audit log entries in memory instead of writing to SQL Server.</summary>
public class FakeAuditLogService : IAuditLogService
{
    public ConcurrentQueue<AuditLogEntry> Entries { get; } = new();

    public Task LogTransactionAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        Entries.Enqueue(entry);
        return Task.CompletedTask;
    }

    public Task<bool> CheckConnectivityAsync(CancellationToken ct = default) => Task.FromResult(true);
}
