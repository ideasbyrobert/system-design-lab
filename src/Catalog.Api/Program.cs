using Catalog.Api.ProductDetails;
using Lab.Persistence;
using Lab.Persistence.DependencyInjection;
using Lab.Shared.Caching;
using Lab.Shared.Contracts;
using Lab.Shared.Configuration;
using Lab.Shared.Http;
using Lab.Shared.Logging;
using Lab.Shared.RegionalReads;
using Lab.Telemetry.AspNetCore;
using Lab.Telemetry.DependencyInjection;
using Lab.Telemetry.RequestTracing;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddLabTelemetry();
builder.Services.AddRequestTraceJsonlWriter();
builder.Services.AddPrimaryPersistence();
builder.Services.AddInMemoryLabCache();
builder.Services.AddScoped<CatalogProductDetailQueryService>();
builder.Services.AddScoped<CatalogProductDetailCache>(serviceProvider =>
{
    return new CatalogProductDetailCache(
        serviceProvider.GetRequiredService<ICacheStore>(),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CacheOptions>>().Value,
        serviceProvider.GetRequiredService<EnvironmentLayout>());
});
builder.Services.AddProblemDetails();
builder.Logging.AddLabOperationalFileLogging();

var app = builder.Build();
app.LogResolvedLabEnvironment();
await EnsurePrimaryDatabaseReadyAsync(app.Services);

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Catalog.Exceptions");
        IProblemDetailsService problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        IExceptionHandlerFeature? exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();

        logger.LogError(exceptionFeature?.Error, "Unhandled exception reached the Catalog exception boundary.");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unhandled Catalog error",
                Detail = "The Catalog host hit an unhandled exception while processing the request.",
                Extensions =
                {
                    ["requestId"] = context.TraceIdentifier
                }
            }
        });
    });
});

app.UseRequestTracing();

app.MapGet("/", (
    EnvironmentLayout layout,
    IRequestTraceContextAccessor traceAccessor) =>
{
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);

    using (trace.BeginStage("describe_catalog_host", new Dictionary<string, string?>
    {
        ["service"] = layout.ServiceName,
        ["region"] = layout.CurrentRegion
    }))
    {
    }

    trace.MarkContractSatisfied();
    trace.AddNote("Catalog host-info endpoint completed successfully.");

    return Results.Ok(new
    {
        layout.ServiceName,
        layout.CurrentRegion,
        layout.RepositoryRoot,
        Request = CreateRequestInfo(trace)
    });
})
    .WithOperationContract(BusinessOperationContracts.CatalogHostInfo);

app.MapGet("/catalog/products/{id}", async (
    string id,
    string? readSource,
    HttpContext httpContext,
    EnvironmentLayout layout,
    Microsoft.Extensions.Options.IOptions<RegionOptions> regionOptionsAccessor,
    Microsoft.Extensions.Options.IOptions<RegionalDegradationOptions> regionalDegradationOptionsAccessor,
    Microsoft.Extensions.Options.IOptions<CacheOptions> cacheOptionsAccessor,
    CatalogProductDetailCache cache,
    CatalogProductDetailQueryService queryService,
    IRequestTraceContextAccessor traceAccessor,
    CancellationToken cancellationToken) =>
{
    CacheOptions cacheOptions = cacheOptionsAccessor.Value;
    RequestTraceContext trace = GetRequiredTraceContext(traceAccessor);
    bool debugTelemetryRequested = IsDebugTelemetryRequested(httpContext.Request);

    trace.RecordInstantStage(
        "request_received",
        metadata: new Dictionary<string, string?>
        {
            ["productId"] = id,
            ["readSource"] = readSource,
            ["debugTelemetryRequested"] = debugTelemetryRequested.ToString().ToLowerInvariant()
        });

    if (string.IsNullOrWhiteSpace(id))
    {
        trace.SetErrorCode("invalid_product_id");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_product_id"
            });

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["productId"] = ["A non-empty product identifier is required."]
        });
    }

    if (!CatalogProductReadSourceParser.TryParse(
            readSource,
            out CatalogProductReadSource selectedReadSource,
            out string? readSourceValidationError))
    {
        trace.SetErrorCode("invalid_read_source");
        trace.RecordInstantStage(
            "response_sent",
            outcome: "validation_failed",
            metadata: new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status400BadRequest.ToString(),
                ["error"] = "invalid_read_source",
                ["readSource"] = readSource
            });

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["readSource"] = [readSourceValidationError ?? "Read source must be 'local', 'primary', 'replica-east', or 'replica-west'."]
        });
    }

    CatalogProductReadTarget readTarget = CatalogProductReadTargetResolver.Resolve(
        layout,
        regionOptionsAccessor.Value,
        regionalDegradationOptionsAccessor.Value,
        selectedReadSource);
    RegionalReadPreference readPreference = CreateReadPreference(readTarget);

    trace.RecordInstantStage(
        "cache_lookup_started",
        metadata: RegionalReadPreferenceMetadata.Create(
            readPreference,
            new Dictionary<string, string?>
            {
                ["productId"] = id,
                ["cacheEnabled"] = cacheOptions.Enabled.ToString().ToLowerInvariant(),
                ["cacheNamespace"] = cache.Scope.NamespaceName,
                ["cacheRegion"] = cache.Scope.Region
            }));

    CatalogProductCacheLookupResult cacheLookup;

    using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
        "cache_lookup",
        RegionalReadPreferenceMetadata.Create(
            readPreference,
            new Dictionary<string, string?>
            {
                ["productId"] = id,
                ["cacheEnabled"] = cacheOptions.Enabled.ToString().ToLowerInvariant(),
                ["cacheNamespace"] = cache.Scope.NamespaceName,
                ["cacheRegion"] = cache.Scope.Region
            })))
    {
        cacheLookup = await cache.GetAsync(readPreference.EffectiveReadSource, id, cancellationToken);
        stage.Complete(
            ToCacheOutcomeText(cacheLookup.Outcome),
            RegionalReadPreferenceMetadata.Create(
                cacheLookup.Product is null ? readPreference : readPreference with { EffectiveReadSource = cacheLookup.Product.ReadSource },
                new Dictionary<string, string?>
                {
                    ["expiresUtc"] = cacheLookup.ExpiresUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                }));
    }

    trace.RecordInstantStage(
        "cache_lookup_completed",
        outcome: ToCacheOutcomeText(cacheLookup.Outcome),
        metadata: RegionalReadPreferenceMetadata.Create(
            cacheLookup.Product is null ? readPreference : readPreference with { EffectiveReadSource = cacheLookup.Product.ReadSource },
            new Dictionary<string, string?>
            {
                ["productId"] = id,
                ["cacheEnabled"] = cacheOptions.Enabled.ToString().ToLowerInvariant(),
                ["cacheNamespace"] = cache.Scope.NamespaceName,
                ["cacheRegion"] = cache.Scope.Region,
                ["expiresUtc"] = cacheLookup.ExpiresUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            }));

    CatalogProductDetail? product = cacheLookup.Product;

    if (cacheLookup.CacheHit)
    {
        trace.SetReadSource(product!.ReadSource);
        trace.SetFreshnessMetrics(
            product.Freshness.ComparedCount,
            product.Freshness.StaleCount,
            product.Freshness.MaxStalenessAgeMs);
        trace.MarkCacheHit();
        trace.MarkContractSatisfied();
        trace.AddNote("Catalog cache hit served product detail.");
        AddReadSelectionNotes(trace, readPreference with { EffectiveReadSource = product.ReadSource });
        trace.RecordInstantStage("freshness_evaluated", metadata: CreateFreshnessMetadata(product.Freshness));
        trace.RecordInstantStage(
            "response_sent",
            outcome: "success",
            metadata: RegionalReadPreferenceMetadata.Create(
                readPreference with { EffectiveReadSource = product.ReadSource },
                new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status200OK.ToString(),
                    ["productId"] = product.ProductId,
                    ["readSource"] = product.ReadSource,
                    ["staleRead"] = product.Freshness.StaleRead.ToString().ToLowerInvariant(),
                    ["staleFraction"] = FormatDouble(product.Freshness.StaleFraction),
                    ["maxStalenessAgeMs"] = FormatDouble(product.Freshness.MaxStalenessAgeMs),
                    ["stockStatus"] = product.StockStatus,
                    ["version"] = product.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["cacheHit"] = "true"
                }));

        return Results.Ok(new CatalogProductDetailResponse(
            ProductId: product.ProductId,
            Name: product.Name,
            Description: product.Description,
            Category: product.Category,
            Price: new CatalogPriceInfo(
                AmountCents: product.PriceCents,
                CurrencyCode: product.CurrencyCode,
                Display: product.DisplayPrice),
            Inventory: new CatalogInventoryInfo(
                AvailableQuantity: product.AvailableQuantity,
                ReservedQuantity: product.ReservedQuantity,
                SellableQuantity: product.SellableQuantity,
                StockStatus: product.StockStatus),
            Version: product.Version,
            ReadSource: product.ReadSource,
            Freshness: product.Freshness,
            Request: CreateRequestInfo(trace))
        {
            DebugTelemetry = debugTelemetryRequested ? CreateDebugTelemetry(trace) : null
        });
    }

    trace.RecordInstantStage(
        "db_query_started",
        metadata: RegionalReadPreferenceMetadata.Create(
            readPreference,
            new Dictionary<string, string?>
            {
                ["productId"] = id,
                ["database"] = readTarget.DatabaseLabel,
                ["readNetworkScope"] = readTarget.SelectionScope
            }));

    CatalogProductReadResult readResult;

    using (RequestTraceContext.StageTraceScope stage = trace.BeginStage(
        "db_query",
        RegionalReadPreferenceMetadata.Create(
            readPreference,
            new Dictionary<string, string?>
            {
                ["productId"] = id,
                ["source"] = readTarget.DatabaseLabel,
                ["readNetworkScope"] = readTarget.SelectionScope
            })))
    {
        readResult = await queryService.GetByIdAsync(id, readTarget, cancellationToken);
        product = readResult.Product;
        stage.Complete(
            product is null ? "not_found" : "success",
            RegionalReadPreferenceMetadata.Create(
                product is null ? readPreference : readPreference with { EffectiveReadSource = product.ReadSource },
                new Dictionary<string, string?>
                {
                    ["found"] = (product is not null).ToString().ToLowerInvariant(),
                    ["version"] = product?.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["stockStatus"] = product?.StockStatus,
                    ["readNetworkScope"] = readResult.ReadEnvelope.NetworkScope,
                    ["readInjectedDelayMs"] = readResult.ReadEnvelope.InjectedDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
    }

    trace.SetReadSource(readResult.Freshness.ReadSource);
    trace.SetFreshnessMetrics(
        readResult.Freshness.ComparedCount,
        readResult.Freshness.StaleCount,
        readResult.Freshness.MaxStalenessAgeMs);
    trace.RecordInstantStage("freshness_evaluated", metadata: CreateFreshnessMetadata(readResult.Freshness));

    trace.RecordInstantStage(
        "db_query_completed",
        outcome: product is null ? "not_found" : "success",
        metadata: RegionalReadPreferenceMetadata.Create(
            product is null ? readPreference : readPreference with { EffectiveReadSource = product.ReadSource },
            new Dictionary<string, string?>
            {
                ["productId"] = id,
                ["source"] = readTarget.DatabaseLabel,
                ["found"] = (product is not null).ToString().ToLowerInvariant(),
                ["version"] = product?.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["stockStatus"] = product?.StockStatus,
                ["readNetworkScope"] = readResult.ReadEnvelope.NetworkScope,
                ["readInjectedDelayMs"] = readResult.ReadEnvelope.InjectedDelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));

    if (product is null)
    {
        trace.MarkContractSatisfied();
        trace.SetErrorCode("product_not_found");
        trace.AddNote("Catalog query returned no matching product.");
        AddReadSelectionNotes(trace, product is null ? readPreference : readPreference with { EffectiveReadSource = product.ReadSource });
        trace.RecordInstantStage(
            "response_sent",
            outcome: "not_found",
            metadata: RegionalReadPreferenceMetadata.Create(
                readPreference,
                new Dictionary<string, string?>
                {
                    ["statusCode"] = StatusCodes.Status404NotFound.ToString(),
                    ["error"] = "product_not_found",
                    ["productId"] = id,
                    ["readSource"] = readPreference.EffectiveReadSource,
                    ["staleRead"] = readResult.Freshness.StaleRead.ToString().ToLowerInvariant(),
                    ["staleFraction"] = FormatDouble(readResult.Freshness.StaleFraction),
                    ["maxStalenessAgeMs"] = FormatDouble(readResult.Freshness.MaxStalenessAgeMs)
                }));

        return Results.NotFound(new CatalogProductNotFoundResponse(
            Error: "product_not_found",
            ProductId: id,
            ReadSource: readPreference.EffectiveReadSource,
            Freshness: readResult.Freshness,
            Request: CreateRequestInfo(trace))
        {
            DebugTelemetry = debugTelemetryRequested ? CreateDebugTelemetry(trace) : null
        });
    }

    await cache.SetAsync(product.ReadSource, product.ProductId, product, cancellationToken);
    trace.MarkContractSatisfied();
    trace.AddNote(cacheOptions.Enabled
        ? $"Catalog cache miss fell through to {readTarget.DatabaseLabel} and populated the in-memory cache."
        : $"Catalog cache is disabled, so the product detail query went directly to {readTarget.DatabaseLabel}.");
    AddReadSelectionNotes(trace, readPreference with { EffectiveReadSource = product.ReadSource });

    if (readTarget.FallbackApplied &&
        string.Equals(readTarget.FallbackReason, "local_replica_unavailable", StringComparison.Ordinal))
    {
        trace.AddNote(
            $"Catalog degraded mode crossed from region '{layout.CurrentRegion}' to '{readTarget.TargetRegion}' because the local replica was marked unavailable.");
    }

    if (readResult.Freshness.StaleRead)
    {
        trace.AddNote("Catalog detected that the selected read source is behind primary visibility.");
    }

    trace.RecordInstantStage(
        "response_sent",
        outcome: "success",
        metadata: RegionalReadPreferenceMetadata.Create(
            readPreference with { EffectiveReadSource = product.ReadSource },
            new Dictionary<string, string?>
            {
                ["statusCode"] = StatusCodes.Status200OK.ToString(),
                ["productId"] = product.ProductId,
                ["readSource"] = product.ReadSource,
                ["staleRead"] = readResult.Freshness.StaleRead.ToString().ToLowerInvariant(),
                ["staleFraction"] = FormatDouble(readResult.Freshness.StaleFraction),
                ["maxStalenessAgeMs"] = FormatDouble(readResult.Freshness.MaxStalenessAgeMs),
                ["stockStatus"] = product.StockStatus,
                ["version"] = product.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["cacheHit"] = "false"
            }));

    return Results.Ok(new CatalogProductDetailResponse(
        ProductId: product.ProductId,
        Name: product.Name,
        Description: product.Description,
        Category: product.Category,
        Price: new CatalogPriceInfo(
            AmountCents: product.PriceCents,
            CurrencyCode: product.CurrencyCode,
            Display: product.DisplayPrice),
        Inventory: new CatalogInventoryInfo(
            AvailableQuantity: product.AvailableQuantity,
            ReservedQuantity: product.ReservedQuantity,
            SellableQuantity: product.SellableQuantity,
            StockStatus: product.StockStatus),
        Version: product.Version,
        ReadSource: product.ReadSource,
        Freshness: readResult.Freshness,
        Request: CreateRequestInfo(trace))
    {
        DebugTelemetry = debugTelemetryRequested ? CreateDebugTelemetry(trace) : null
    });
})
    .WithOperationContract(BusinessOperationContracts.CatalogProductDetail);

app.Run();

static CatalogRequestInfo CreateRequestInfo(RequestTraceContext trace) =>
    new(
        RunId: trace.RunId,
        TraceId: trace.TraceId,
        RequestId: trace.RequestId,
        CorrelationId: trace.CorrelationId);

static CatalogDebugTelemetryInfo CreateDebugTelemetry(RequestTraceContext trace) =>
    new(
        StageTimings: trace.StageTimings
            .Select(stage => new CatalogDebugStageInfo(
                StageName: stage.StageName,
                ElapsedMs: stage.ElapsedMs,
                Outcome: stage.Outcome,
                Metadata: stage.Metadata))
            .ToArray(),
        Notes: trace.Notes.ToArray());

static IReadOnlyDictionary<string, string?> CreateFreshnessMetadata(CatalogReadFreshnessInfo freshness) =>
    new Dictionary<string, string?>
    {
        ["readSource"] = freshness.ReadSource,
        ["comparedCount"] = freshness.ComparedCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["staleCount"] = freshness.StaleCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["staleRead"] = freshness.StaleRead.ToString().ToLowerInvariant(),
        ["staleFraction"] = FormatDouble(freshness.StaleFraction),
        ["maxStalenessAgeMs"] = FormatDouble(freshness.MaxStalenessAgeMs),
        ["observedVersion"] = freshness.ObservedVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["primaryVersion"] = freshness.PrimaryVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["observedUpdatedUtc"] = freshness.ObservedUpdatedUtc?.ToString("O"),
        ["primaryUpdatedUtc"] = freshness.PrimaryUpdatedUtc?.ToString("O")
    };

static string? FormatDouble(double? value) =>
    value?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

static bool IsDebugTelemetryRequested(HttpRequest request)
{
    if (!request.Headers.TryGetValue(LabHeaderNames.DebugTelemetry, out Microsoft.Extensions.Primitives.StringValues values))
    {
        return false;
    }

    return bool.TryParse(values.ToString(), out bool enabled) && enabled;
}

static RequestTraceContext GetRequiredTraceContext(IRequestTraceContextAccessor accessor) =>
    accessor.Current ?? throw new InvalidOperationException("Request trace context is not available for the current request.");

static async Task EnsurePrimaryDatabaseReadyAsync(IServiceProvider services)
{
    using IServiceScope scope = services.CreateScope();
    EnvironmentLayout layout = scope.ServiceProvider.GetRequiredService<EnvironmentLayout>();
    PrimaryDatabaseInitializer initializer = scope.ServiceProvider.GetRequiredService<PrimaryDatabaseInitializer>();
    await initializer.InitializeAsync(layout.PrimaryDatabasePath);
}

static string ToCacheOutcomeText(CatalogProductCacheLookupOutcome outcome) =>
    outcome switch
    {
        CatalogProductCacheLookupOutcome.Disabled => "disabled",
        CatalogProductCacheLookupOutcome.Hit => "hit",
        _ => "miss"
    };

static RegionalReadPreference CreateReadPreference(
    CatalogProductReadTarget readTarget,
    string? effectiveReadSource = null) =>
    new(
        RequestedReadSource: readTarget.RequestedReadSourceText,
        EffectiveReadSource: effectiveReadSource ?? readTarget.ReadSourceText,
        TargetRegion: readTarget.TargetRegion,
        SelectionScope: readTarget.SelectionScope,
        FallbackApplied: readTarget.FallbackApplied,
        FallbackReason: readTarget.FallbackReason);

static void AddReadSelectionNotes(RequestTraceContext trace, RegionalReadPreference readPreference)
{
    if (readPreference.RequestedReadSource == readPreference.EffectiveReadSource)
    {
        trace.AddNote(
            $"Catalog resolved product read source '{readPreference.RequestedReadSource}' as a {readPreference.SelectionScope} read in region '{readPreference.TargetRegion}'.");
        return;
    }

    trace.AddNote(
        $"Catalog fell back from requested product read source '{readPreference.RequestedReadSource}' to '{readPreference.EffectiveReadSource}'.");
    trace.AddNote(
        $"Fallback reason: {readPreference.FallbackReason ?? "unspecified"}; target region '{readPreference.TargetRegion}' via {readPreference.SelectionScope} path.");
}
