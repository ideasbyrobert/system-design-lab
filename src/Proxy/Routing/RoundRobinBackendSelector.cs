using System.Collections.Concurrent;

namespace Proxy.Routing;

public sealed class RoundRobinBackendSelector
{
    private readonly ConcurrentDictionary<string, RouteCursor> _cursors = new(StringComparer.OrdinalIgnoreCase);

    public Uri SelectBackend(string routeName, IReadOnlyList<Uri> backends)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(backends);

        if (backends.Count == 0)
        {
            throw new InvalidOperationException($"Route '{routeName}' has no configured backends.");
        }

        RouteCursor cursor = _cursors.GetOrAdd(routeName, _ => new RouteCursor());
        long sequence = Interlocked.Increment(ref cursor.NextSequence) - 1L;
        int index = (int)(sequence % backends.Count);
        return backends[index];
    }

    private sealed class RouteCursor
    {
        public long NextSequence;
    }
}
