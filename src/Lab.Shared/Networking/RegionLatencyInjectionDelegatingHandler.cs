using Microsoft.Extensions.Logging;

namespace Lab.Shared.Networking;

public sealed class RegionLatencyInjectionDelegatingHandler(
    IRegionNetworkEnvelopePolicy networkEnvelopePolicy,
    ILogger<RegionLatencyInjectionDelegatingHandler> logger,
    string dependencyName,
    string? defaultTargetRegion) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? targetRegion = request.Options.TryGetValue(RegionLatencyRequestOptions.TargetRegion, out string? requestTargetRegion)
            ? requestTargetRegion
            : defaultTargetRegion;

        RegionNetworkEnvelope envelope = networkEnvelopePolicy.Resolve(targetRegion);

        logger.LogInformation(
            "Applying {NetworkScope} network envelope for dependency {DependencyName}. CallerRegion={CallerRegion}, TargetRegion={TargetRegion}, InjectedDelayMs={InjectedDelayMs}, Method={Method}, Uri={Uri}",
            envelope.NetworkScope,
            dependencyName,
            envelope.CallerRegion,
            envelope.TargetRegion,
            envelope.InjectedDelayMs,
            request.Method,
            request.RequestUri);

        if (envelope.InjectedDelay > TimeSpan.Zero)
        {
            await Task.Delay(envelope.InjectedDelay, cancellationToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
