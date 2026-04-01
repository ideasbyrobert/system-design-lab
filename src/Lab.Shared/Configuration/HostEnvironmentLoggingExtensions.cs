using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lab.Shared.Configuration;

public static class HostEnvironmentLoggingExtensions
{
    public static void LogResolvedLabEnvironment(this IHost host)
    {
        EnvironmentLayout layout = host.Services.GetRequiredService<EnvironmentLayout>();
        ILogger logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Lab.Environment");

        logger.LogInformation(
            "Resolved environment for {ServiceName} in region {Region}. RepoRoot={RepositoryRoot}, ContentRoot={ContentRoot}, PrimaryDb={PrimaryDb}, ReplicaEastDb={ReplicaEastDb}, ReplicaWestDb={ReplicaWestDb}, ReadModelsDb={ReadModelsDb}, RequestsJsonl={RequestsJsonl}, JobsJsonl={JobsJsonl}, RunsDir={RunsDir}, AnalysisDir={AnalysisDir}, ServiceLog={ServiceLog}",
            layout.ServiceName,
            layout.CurrentRegion,
            layout.RepositoryRoot,
            layout.ContentRoot,
            layout.PrimaryDatabasePath,
            layout.ReplicaEastDatabasePath,
            layout.ReplicaWestDatabasePath,
            layout.ReadModelDatabasePath,
            layout.RequestsJsonlPath,
            layout.JobsJsonlPath,
            layout.RunsDirectory,
            layout.AnalysisDirectory,
            layout.ServiceLogPath);
    }
}
