using Lab.Analysis.Cli;
using Lab.Analysis.DependencyInjection;
using Lab.Analysis.Services;
using Lab.Shared.Configuration;
using Lab.Shared.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (!AnalyzeCliOptions.TryParse(args, out AnalyzeCliOptions cliOptions, out string? error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(AnalyzeCliOptions.GetUsage());
    Environment.ExitCode = 1;
    return;
}

if (cliOptions.ShowHelp)
{
    Console.WriteLine(AnalyzeCliOptions.GetUsage());
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLabConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddLabAnalysis();
builder.Logging.AddLabOperationalFileLogging();

using var host = builder.Build();
host.LogResolvedLabEnvironment();

EnvironmentLayout layout = host.Services.GetRequiredService<EnvironmentLayout>();
TelemetryAnalyzer analyzer = host.Services.GetRequiredService<TelemetryAnalyzer>();
AnalysisArtifactWriter artifactWriter = host.Services.GetRequiredService<AnalysisArtifactWriter>();
ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Analyze");

var summary = await analyzer.AnalyzeAsync(
    layout.RequestsJsonlPath,
    layout.JobsJsonlPath,
    layout.PrimaryDatabasePath,
    cliOptions.ToFilter());

string summaryPath = layout.GetRunSummaryPath(summary.RunId);
string reportPath = layout.GetRunReportPath(summary.RunId);

await artifactWriter.WriteSummaryJsonAsync(summary, summaryPath);
await artifactWriter.WriteMarkdownReportAsync(summary, reportPath);

logger.LogInformation(
    "Analysis completed for run {RunId}. Requests={RequestCount}, Jobs={JobCount}, SummaryPath={SummaryPath}, ReportPath={ReportPath}.",
    summary.RunId,
    summary.Requests.RequestCount,
    summary.Jobs.JobCount,
    summaryPath,
    reportPath);

Console.WriteLine($"RunId: {summary.RunId}");
Console.WriteLine($"Summary: {summaryPath}");
Console.WriteLine($"Report: {reportPath}");
