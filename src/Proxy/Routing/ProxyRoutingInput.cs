using Microsoft.AspNetCore.Http;

namespace Proxy.Routing;

public sealed record ProxyRoutingInput(
    PathString RequestPath,
    QueryString QueryString,
    string? SessionKeyHeader,
    string? SessionCookie);
