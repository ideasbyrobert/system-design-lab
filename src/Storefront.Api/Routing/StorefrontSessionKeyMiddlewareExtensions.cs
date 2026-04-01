using Lab.Shared.Http;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Storefront.Api.Routing;

internal static class StorefrontSessionKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseStorefrontSessionKeyConvention(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            StorefrontSessionKeyResolution resolution = ResolveSessionKey(context);
            context.SetStorefrontSessionKeyResolution(resolution);

            if (context.Items.TryGetValue(typeof(RequestTraceContext), out object? traceValue) &&
                traceValue is RequestTraceContext trace)
            {
                trace.SetSessionKey(resolution.SessionKey);

                if (resolution.Source == StorefrontSessionKeySources.Generated)
                {
                    trace.AddNote("Storefront generated a session key because the request did not include X-Session-Key or lab-session.");
                }
            }

            context.Response.OnStarting(() =>
            {
                context.Response.Headers[LabHeaderNames.SessionKey] = resolution.SessionKey;

                if (resolution.CookieIssued)
                {
                    context.Response.Cookies.Append(
                        LabCookieNames.Session,
                        resolution.SessionKey,
                        CreateCookieOptions());
                }

                return Task.CompletedTask;
            });

            await next();
        });
    }

    private static StorefrontSessionKeyResolution ResolveSessionKey(HttpContext context)
    {
        string? headerValue = Normalize(context.Request.Headers[LabHeaderNames.SessionKey].ToString());

        if (headerValue is not null)
        {
            return new StorefrontSessionKeyResolution(
                SessionKey: headerValue,
                Source: StorefrontSessionKeySources.Header,
                CookieIssued: false);
        }

        string? cookieValue = context.Request.Cookies.TryGetValue(LabCookieNames.Session, out string? rawCookieValue)
            ? Normalize(rawCookieValue)
            : null;

        if (cookieValue is not null)
        {
            return new StorefrontSessionKeyResolution(
                SessionKey: cookieValue,
                Source: StorefrontSessionKeySources.Cookie,
                CookieIssued: false);
        }

        return new StorefrontSessionKeyResolution(
            SessionKey: $"sess-{Guid.NewGuid():N}",
            Source: StorefrontSessionKeySources.Generated,
            CookieIssued: true);
    }

    private static CookieOptions CreateCookieOptions() =>
        new()
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(30),
            Path = "/",
            SameSite = SameSiteMode.Lax
        };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
