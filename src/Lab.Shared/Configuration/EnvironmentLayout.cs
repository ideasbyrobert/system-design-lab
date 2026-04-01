using Microsoft.Extensions.Hosting;

namespace Lab.Shared.Configuration;

public sealed record EnvironmentLayout(
    string RepositoryRoot,
    string SourceRoot,
    string ContentRoot,
    string ServiceName,
    string CurrentRegion,
    string PrimaryDatabasePath,
    string ReplicaEastDatabasePath,
    string ReplicaWestDatabasePath,
    string ReadModelDatabasePath,
    string RequestsJsonlPath,
    string JobsJsonlPath,
    string RunsDirectory,
    string AnalysisDirectory,
    string ServiceLogPath)
{
    public string GetRunSummaryPath(string runId) =>
        Path.Combine(RunsDirectory, runId, "summary.json");

    public string GetRunReportPath(string runId) =>
        Path.Combine(AnalysisDirectory, runId, "report.md");

    public static EnvironmentLayout Create(
        IHostEnvironment hostEnvironment,
        RepositoryOptions repositoryOptions,
        DatabasePathOptions databasePathOptions,
        LogPathOptions logPathOptions,
        RegionOptions regionOptions)
    {
        string discoveredRepositoryRoot = FindRepositoryRoot(hostEnvironment.ContentRootPath);
        string repositoryRoot = string.IsNullOrWhiteSpace(repositoryOptions.RootPath)
            ? discoveredRepositoryRoot
            : ResolvePath(discoveredRepositoryRoot, repositoryOptions.RootPath);

        return new EnvironmentLayout(
            RepositoryRoot: repositoryRoot,
            SourceRoot: ResolvePath(repositoryRoot, "src"),
            ContentRoot: hostEnvironment.ContentRootPath,
            ServiceName: hostEnvironment.ApplicationName,
            CurrentRegion: regionOptions.CurrentRegion,
            PrimaryDatabasePath: ResolvePath(repositoryRoot, databasePathOptions.Primary),
            ReplicaEastDatabasePath: ResolvePath(repositoryRoot, databasePathOptions.ReplicaEast),
            ReplicaWestDatabasePath: ResolvePath(repositoryRoot, databasePathOptions.ReplicaWest),
            ReadModelDatabasePath: ResolvePath(repositoryRoot, databasePathOptions.ReadModels),
            RequestsJsonlPath: ResolvePath(repositoryRoot, logPathOptions.RequestsJsonl),
            JobsJsonlPath: ResolvePath(repositoryRoot, logPathOptions.JobsJsonl),
            RunsDirectory: ResolvePath(repositoryRoot, logPathOptions.RunsDirectory),
            AnalysisDirectory: ResolvePath(repositoryRoot, logPathOptions.AnalysisDirectory),
            ServiceLogPath: ResolvePath(repositoryRoot, GetServiceLogRelativePath(logPathOptions, hostEnvironment.ApplicationName)));
    }

    public static string FindRepositoryRoot(string startingPath)
    {
        DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(startingPath));

        while (current is not null)
        {
            bool hasSolutionMarker =
                File.Exists(Path.Combine(current.FullName, "ecommerce-systems-lab.sln")) ||
                File.Exists(Path.Combine(current.FullName, "ecommerce-systems-lab.slnx"));

            if (hasSolutionMarker && Directory.Exists(Path.Combine(current.FullName, "src")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the ecommerce-systems-lab repository root from '{startingPath}'.");
    }

    public static string ResolvePath(string repositoryRoot, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        return Path.GetFullPath(configuredPath, repositoryRoot);
    }

    private static string GetServiceLogRelativePath(LogPathOptions options, string serviceName) =>
        serviceName switch
        {
            "Storefront.Api" => options.StorefrontLog ?? Path.Combine(options.OperationalLogsDirectory, "storefront.log"),
            "Catalog.Api" => options.CatalogLog ?? Path.Combine(options.OperationalLogsDirectory, "catalog.log"),
            "Cart.Api" => options.CartLog ?? Path.Combine(options.OperationalLogsDirectory, "cart.log"),
            "Order.Api" => options.OrderLog ?? Path.Combine(options.OperationalLogsDirectory, "order.log"),
            "PaymentSimulator.Api" => options.PaymentSimulatorLog ?? Path.Combine(options.OperationalLogsDirectory, "payment-simulator.log"),
            "Proxy" => options.ProxyLog ?? Path.Combine(options.OperationalLogsDirectory, "proxy.log"),
            "Worker" => options.WorkerLog ?? Path.Combine(options.OperationalLogsDirectory, "worker.log"),
            "SeedData" => options.SeedDataLog ?? Path.Combine(options.OperationalLogsDirectory, "seed-data.log"),
            "LoadGen" => options.LoadGenLog ?? Path.Combine(options.OperationalLogsDirectory, "loadgen.log"),
            "Analyze" => options.AnalyzeLog ?? Path.Combine(options.OperationalLogsDirectory, "analyze.log"),
            _ => Path.Combine(options.OperationalLogsDirectory, $"{serviceName.ToLowerInvariant()}.log")
        };
}
