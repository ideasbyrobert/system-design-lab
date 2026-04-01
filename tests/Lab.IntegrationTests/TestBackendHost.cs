using System.Text;
using System.Text.Json;
using Lab.Shared.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lab.IntegrationTests;

internal sealed class TestBackendHost : IAsyncDisposable
{
    private readonly WebApplication _application;
    private bool _stopped;

    private TestBackendHost(string backendId, WebApplication application, Uri baseUri)
    {
        BackendId = backendId;
        _application = application;
        BaseUri = baseUri;
    }

    public string BackendId { get; }

    public Uri BaseUri { get; }

    public static async Task<TestBackendHost> StartAsync(string backendId)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        WebApplication application = builder.Build();
        application.MapMethods("/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"], async context =>
        {
            using StreamReader reader = new(context.Request.Body, Encoding.UTF8);
            string body = await reader.ReadToEndAsync(context.RequestAborted);

            context.Response.Headers["X-Backend-Id"] = backendId;
            context.Response.Headers.Append("Set-Cookie", $"backend-id={backendId}; path=/");

            await context.Response.WriteAsJsonAsync(new
            {
                backendId,
                method = context.Request.Method,
                path = context.Request.Path.Value ?? "/",
                query = context.Request.QueryString.Value ?? string.Empty,
                body,
                runId = GetHeader(context, LabHeaderNames.RunId),
                correlationId = GetHeader(context, LabHeaderNames.CorrelationId),
                idempotencyKey = GetHeader(context, LabHeaderNames.IdempotencyKey),
                sessionKey = GetHeader(context, LabHeaderNames.SessionKey),
                debugTelemetry = GetHeader(context, LabHeaderNames.DebugTelemetry)
            });
        });

        await application.StartAsync();

        IServerAddressesFeature addressesFeature = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("The in-memory backend did not expose server addresses.");

        Uri baseUri = new(addressesFeature.Addresses.Single(), UriKind.Absolute);
        return new TestBackendHost(backendId, application, baseUri);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _application.DisposeAsync();
    }

    public async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        await _application.StopAsync();
    }

    private static string? GetHeader(HttpContext context, string headerName) =>
        context.Request.Headers.TryGetValue(headerName, out var value)
            ? value.ToString()
            : null;
}
