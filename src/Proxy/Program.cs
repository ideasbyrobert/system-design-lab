using Lab.Shared.Configuration;
using Lab.Shared.Http;
using Lab.Shared.Logging;
using Proxy.Configuration;
using Proxy.Forwarding;
using Proxy.Routing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddOptions<ProxyOptions>()
    .Configure(options => ProxyOptionsParser.Apply(options, builder.Configuration.GetSection("Lab:Proxy")));
builder.Services.AddSingleton<RoundRobinBackendSelector>();
builder.Services.AddSingleton<StickyBackendAssignmentStore>();
builder.Services.AddSingleton<ProxyRoutingService>();
builder.Services.AddSingleton<ProxyForwarder>();
builder.Services.AddHttpClient(ProxyForwarder.BackendClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Logging.AddLabOperationalFileLogging();

var app = builder.Build();
app.LogResolvedLabEnvironment();

app.MapGet("/proxy/status", (EnvironmentLayout layout, ProxyRoutingService routingService) => Results.Ok(new
{
    layout.ServiceName,
    layout.CurrentRegion,
    layout.RepositoryRoot,
    routingMode = routingService.RoutingMode,
    stickyAssignmentCount = routingService.StickyAssignmentCount,
    routes = routingService.Routes.Select(route => new
    {
        route.Name,
        pathPrefix = route.PathPrefix.Value ?? "/",
        backends = route.Backends.Select(backend => backend.AbsoluteUri).ToArray()
    })
}));

string[] proxyMethods =
[
    HttpMethods.Get,
    HttpMethods.Post,
    HttpMethods.Put,
    HttpMethods.Patch,
    HttpMethods.Delete,
    HttpMethods.Head,
    HttpMethods.Options
];

app.MapMethods("/{**path}", proxyMethods, async (
    HttpContext httpContext,
    ProxyRoutingService routingService,
    ProxyForwarder forwarder,
    CancellationToken cancellationToken) =>
{
    string? sessionCookie = httpContext.Request.Cookies.TryGetValue(LabCookieNames.Session, out string? rawSessionCookie)
        ? rawSessionCookie
        : null;

    if (!routingService.TryResolve(
            new ProxyRoutingInput(
                RequestPath: httpContext.Request.Path,
                QueryString: httpContext.Request.QueryString,
                SessionKeyHeader: httpContext.Request.Headers[LabHeaderNames.SessionKey].ToString(),
                SessionCookie: sessionCookie),
            out ProxyRoutingDecision? decision,
            out string? error))
    {
        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                error = "proxy_no_backend",
                detail = error
            },
            cancellationToken);
        return;
    }

    await forwarder.ForwardAsync(httpContext, decision!, cancellationToken);
});

app.Run();
