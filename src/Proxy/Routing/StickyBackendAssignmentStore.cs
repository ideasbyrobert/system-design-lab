using System.Collections.Concurrent;

namespace Proxy.Routing;

public sealed class StickyBackendAssignmentStore
{
    private readonly ConcurrentDictionary<string, Uri> _assignments = new(StringComparer.Ordinal);

    public int Count => _assignments.Count;

    public bool TryGetAssignedBackend(
        string routeName,
        string sessionKey,
        IReadOnlyList<Uri> availableBackends,
        out Uri? backend)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentNullException.ThrowIfNull(availableBackends);

        string key = CreateKey(routeName, sessionKey);
        if (!_assignments.TryGetValue(key, out Uri? assignedBackend))
        {
            backend = null;
            return false;
        }

        if (availableBackends.Any(candidate => candidate == assignedBackend))
        {
            backend = assignedBackend;
            return true;
        }

        _assignments.TryRemove(key, out _);
        backend = null;
        return false;
    }

    public void Assign(string routeName, string sessionKey, Uri backend)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentNullException.ThrowIfNull(backend);

        _assignments[CreateKey(routeName, sessionKey)] = backend;
    }

    public void Clear(string routeName, string sessionKey, Uri? expectedBackend = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);

        string key = CreateKey(routeName, sessionKey);

        if (expectedBackend is null)
        {
            _assignments.TryRemove(key, out _);
            return;
        }

        if (_assignments.TryGetValue(key, out Uri? assignedBackend) &&
            assignedBackend == expectedBackend)
        {
            _assignments.TryRemove(key, out _);
        }
    }

    private static string CreateKey(string routeName, string sessionKey) =>
        $"{routeName}|{sessionKey}";
}
