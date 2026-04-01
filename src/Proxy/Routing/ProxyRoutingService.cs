using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Proxy.Configuration;

namespace Proxy.Routing;

public sealed class ProxyRoutingService
{
    private readonly IReadOnlyList<ProxyRouteTarget> _routes;
    private readonly IReadOnlyDictionary<string, ProxyRouteTarget> _routesByName;
    private readonly RoundRobinBackendSelector _selector;
    private readonly StickyBackendAssignmentStore _stickyAssignments;
    private readonly string _routingMode;

    public ProxyRoutingService(
        IOptions<ProxyOptions> options,
        RoundRobinBackendSelector selector,
        StickyBackendAssignmentStore stickyAssignments)
    {
        ArgumentNullException.ThrowIfNull(options);

        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _stickyAssignments = stickyAssignments ?? throw new ArgumentNullException(nameof(stickyAssignments));
        _routingMode = ProxyRoutingModes.Normalize(options.Value.RoutingMode);
        _routes = CreateRoutes(options.Value);
        _routesByName = _routes.ToDictionary(route => route.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ProxyRouteTarget> Routes => _routes;

    public string RoutingMode => _routingMode;

    public int StickyAssignmentCount => _stickyAssignments.Count;

    public bool TryResolve(
        ProxyRoutingInput input,
        out ProxyRoutingDecision? decision,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(input);

        ProxyRouteTarget? route = _routes.FirstOrDefault(candidate => candidate.Matches(input.RequestPath));
        if (route is null)
        {
            decision = null;
            error = $"No proxy route matched path '{input.RequestPath}'.";
            return false;
        }

        if (route.Backends.Count == 0)
        {
            decision = null;
            error = $"Route '{route.Name}' matched path '{input.RequestPath}' but has no configured backends.";
            return false;
        }

        ProxySessionKeyResolution session = ProxySessionKeyResolver.Resolve(input.SessionKeyHeader, input.SessionCookie);
        (Uri backend, string backendSelection) = SelectBackend(route, session);

        decision = CreateDecision(
            route,
            input.RequestPath,
            input.QueryString,
            backend,
            backendSelection,
            session);
        error = null;
        return true;
    }

    public bool TryRemapAfterTransportFailure(
        ProxyRoutingDecision failedDecision,
        out ProxyRoutingDecision? remappedDecision,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(failedDecision);

        remappedDecision = null;

        if (!string.Equals(failedDecision.RoutingMode, ProxyRoutingModes.Sticky, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(failedDecision.SessionKey))
        {
            error = "Transport-failure remap is only available for sticky decisions with a session key.";
            return false;
        }

        if (!_routesByName.TryGetValue(failedDecision.RouteName, out ProxyRouteTarget? route))
        {
            error = $"Route '{failedDecision.RouteName}' is no longer configured.";
            return false;
        }

        _stickyAssignments.Clear(
            failedDecision.RouteName,
            failedDecision.SessionKey,
            failedDecision.BackendBaseUri);

        Uri[] remainingBackends = route.Backends
            .Where(backend => backend != failedDecision.BackendBaseUri)
            .ToArray();

        if (remainingBackends.Length == 0)
        {
            error = $"Route '{failedDecision.RouteName}' has no alternate backend after '{failedDecision.BackendBaseUri.AbsoluteUri}' failed.";
            return false;
        }

        Uri remappedBackend = _selector.SelectBackend(route.Name, remainingBackends);
        _stickyAssignments.Assign(failedDecision.RouteName, failedDecision.SessionKey, remappedBackend);

        remappedDecision = CreateDecision(
            route,
            new PathString(failedDecision.ForwardUri.AbsolutePath),
            new QueryString(failedDecision.ForwardUri.Query),
            remappedBackend,
            "sticky_remapped_after_failure",
            new ProxySessionKeyResolution(failedDecision.SessionKey, failedDecision.SessionKeySource));
        error = null;
        return true;
    }

    private ProxyRoutingDecision CreateDecision(
        ProxyRouteTarget route,
        PathString requestPath,
        QueryString queryString,
        Uri backend,
        string backendSelection,
        ProxySessionKeyResolution session)
    {
        return new ProxyRoutingDecision(
            RouteName: route.Name,
            RoutePrefix: route.PathPrefix.Value ?? "/",
            RoutingMode: _routingMode,
            BackendSelection: backendSelection,
            SessionKey: session.SessionKey,
            SessionKeySource: session.Source,
            BackendBaseUri: backend,
            ForwardUri: BuildForwardUri(backend, requestPath, queryString));
    }

    private (Uri Backend, string BackendSelection) SelectBackend(
        ProxyRouteTarget route,
        ProxySessionKeyResolution session)
    {
        if (string.Equals(_routingMode, ProxyRoutingModes.Sticky, StringComparison.OrdinalIgnoreCase) &&
            session.SessionKey is not null)
        {
            if (_stickyAssignments.TryGetAssignedBackend(route.Name, session.SessionKey, route.Backends, out Uri? assignedBackend) &&
                assignedBackend is not null)
            {
                return (assignedBackend, "sticky_reused");
            }

            Uri assigned = _selector.SelectBackend(route.Name, route.Backends);
            _stickyAssignments.Assign(route.Name, session.SessionKey, assigned);
            return (assigned, "sticky_assigned");
        }

        Uri roundRobinBackend = _selector.SelectBackend(route.Name, route.Backends);
        return (roundRobinBackend, string.Equals(_routingMode, ProxyRoutingModes.Sticky, StringComparison.OrdinalIgnoreCase)
            ? "sticky_fallback_no_session"
            : "round_robin");
    }

    private static IReadOnlyList<ProxyRouteTarget> CreateRoutes(ProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<ProxyRouteTarget> routes = [];
        AddRouteIfEnabled(routes, "catalog", options.Catalog);
        AddRouteIfEnabled(routes, "storefront", options.Storefront);

        return routes
            .OrderByDescending(route => route.PathPrefix.Value?.Length ?? 0)
            .ToArray();
    }

    private static void AddRouteIfEnabled(
        ICollection<ProxyRouteTarget> routes,
        string routeName,
        ProxyBackendGroupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return;
        }

        PathString prefix = NormalizePrefix(options.PathPrefix);
        Uri[] backends = options.Backends
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(ParseBackendUri)
            .ToArray();

        routes.Add(new ProxyRouteTarget(routeName, prefix, backends));
    }

    private static PathString NormalizePrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new PathString("/");
        }

        string normalized = value.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        if (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized.TrimEnd('/');
        }

        return new PathString(normalized);
    }

    private static Uri ParseBackendUri(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException($"'{value}' is not a valid proxy backend URI.");
        }

        string normalized = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri.AbsoluteUri
            : uri.AbsoluteUri + "/";

        return new Uri(normalized, UriKind.Absolute);
    }

    private static Uri BuildForwardUri(Uri backendBaseUri, PathString requestPath, QueryString queryString)
    {
        string relativePath = requestPath.HasValue
            ? requestPath.Value!.TrimStart('/')
            : string.Empty;

        Uri combined = new(backendBaseUri, relativePath);
        UriBuilder builder = new(combined)
        {
            Query = queryString.HasValue ? queryString.Value!.TrimStart('?') : string.Empty
        };

        return builder.Uri;
    }
}
