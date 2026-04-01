namespace Proxy.Routing;

public sealed record ProxyRoutingDecision(
    string RouteName,
    string RoutePrefix,
    string RoutingMode,
    string BackendSelection,
    string? SessionKey,
    string SessionKeySource,
    Uri BackendBaseUri,
    Uri ForwardUri);
