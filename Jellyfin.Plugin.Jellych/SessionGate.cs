using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellych.WebhookPlugin;

// Preserve event ordering within a session without serializing other viewers.
// References include queued waiters, so a lock cannot be replaced while in use.
internal sealed class SessionGate
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(sessionId, out entry!))
            {
                entry = new Entry();
                _entries.Add(sessionId, entry);
            }
            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Lease(this, sessionId, entry);
        }
        catch
        {
            ReleaseReference(sessionId, entry);
            throw;
        }
    }

    private void ReleaseReference(string sessionId, Entry entry)
    {
        lock (_sync)
        {
            if (--entry.References == 0)
            {
                _entries.Remove(sessionId);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
    }

    private sealed class Lease(SessionGate owner, string sessionId, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                entry.Semaphore.Release();
                owner.ReleaseReference(sessionId, entry);
            }
        }
    }
}
