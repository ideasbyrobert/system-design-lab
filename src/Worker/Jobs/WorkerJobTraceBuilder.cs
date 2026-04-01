using Lab.Telemetry.RequestTracing;

namespace Worker.Jobs;

internal sealed class WorkerJobTraceBuilder(TimeProvider timeProvider)
{
    private readonly List<StageTimingRecord> _stageTimings = [];
    private readonly List<DependencyCallRecord> _dependencyCalls = [];
    private readonly List<string> _notes = [];
    private string? _errorCode;

    public IReadOnlyList<StageTimingRecord> StageTimings => _stageTimings;

    public IReadOnlyList<DependencyCallRecord> DependencyCalls => _dependencyCalls;

    public IReadOnlyList<string> Notes => _notes;

    public string? ErrorCode => _errorCode;

    public StageScope BeginStage(string stageName, IReadOnlyDictionary<string, string?>? metadata = null) =>
        new(this, timeProvider, stageName, metadata);

    public DependencyScope BeginDependencyCall(
        string dependencyName,
        string route,
        string region,
        IReadOnlyDictionary<string, string?>? metadata = null,
        IReadOnlyList<string>? notes = null) =>
        new(this, timeProvider, dependencyName, route, region, metadata, notes);

    public void RecordInstantStage(
        string stageName,
        string outcome = "observed",
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow();

        _stageTimings.Add(new StageTimingRecord
        {
            StageName = stageName,
            StartedUtc = nowUtc,
            EndedUtc = nowUtc,
            ElapsedMs = 0d,
            Outcome = outcome,
            Metadata = metadata is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?>(metadata, StringComparer.Ordinal)
        });
    }

    public void SetErrorCode(string errorCode) =>
        _errorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim();

    public void AddNote(string note)
    {
        if (!string.IsNullOrWhiteSpace(note))
        {
            _notes.Add(note.Trim());
        }
    }

    private void AddStage(StageTimingRecord stage) => _stageTimings.Add(stage);

    private void AddDependency(DependencyCallRecord dependency) => _dependencyCalls.Add(dependency);

    internal sealed class StageScope(
        WorkerJobTraceBuilder builder,
        TimeProvider timeProvider,
        string stageName,
        IReadOnlyDictionary<string, string?>? metadata) : IDisposable
    {
        private readonly DateTimeOffset _startedUtc = timeProvider.GetUtcNow();
        private readonly long _startedTimestamp = timeProvider.GetTimestamp();
        private bool _completed;

        public void Complete(string outcome = "success", IReadOnlyDictionary<string, string?>? metadataOverride = null)
        {
            if (_completed)
            {
                return;
            }

            DateTimeOffset endedUtc = timeProvider.GetUtcNow();
            long endedTimestamp = timeProvider.GetTimestamp();

            builder.AddStage(new StageTimingRecord
            {
                StageName = stageName,
                StartedUtc = _startedUtc,
                EndedUtc = endedUtc,
                ElapsedMs = timeProvider.GetElapsedTime(_startedTimestamp, endedTimestamp).TotalMilliseconds,
                Outcome = outcome,
                Metadata = metadataOverride is null
                    ? metadata is null
                        ? new Dictionary<string, string?>()
                        : new Dictionary<string, string?>(metadata, StringComparer.Ordinal)
                    : new Dictionary<string, string?>(metadataOverride, StringComparer.Ordinal)
            });

            _completed = true;
        }

        public void Dispose() => Complete("disposed");
    }

    internal sealed class DependencyScope(
        WorkerJobTraceBuilder builder,
        TimeProvider timeProvider,
        string dependencyName,
        string route,
        string region,
        IReadOnlyDictionary<string, string?>? metadata,
        IReadOnlyList<string>? notes) : IDisposable
    {
        private readonly DateTimeOffset _startedUtc = timeProvider.GetUtcNow();
        private readonly long _startedTimestamp = timeProvider.GetTimestamp();
        private bool _completed;

        public void Complete(
            int? statusCode = null,
            string outcome = "success",
            IReadOnlyDictionary<string, string?>? metadataOverride = null,
            IReadOnlyList<string>? notesOverride = null)
        {
            if (_completed)
            {
                return;
            }

            DateTimeOffset endedUtc = timeProvider.GetUtcNow();
            long endedTimestamp = timeProvider.GetTimestamp();

            builder.AddDependency(new DependencyCallRecord
            {
                DependencyName = dependencyName,
                Route = route,
                Region = region,
                StartedUtc = _startedUtc,
                EndedUtc = endedUtc,
                ElapsedMs = timeProvider.GetElapsedTime(_startedTimestamp, endedTimestamp).TotalMilliseconds,
                StatusCode = statusCode,
                Outcome = outcome,
                Metadata = metadataOverride is null
                    ? metadata is null
                        ? new Dictionary<string, string?>()
                        : new Dictionary<string, string?>(metadata, StringComparer.Ordinal)
                    : new Dictionary<string, string?>(metadataOverride, StringComparer.Ordinal),
                Notes = notesOverride ?? notes ?? Array.Empty<string>()
            });

            _completed = true;
        }

        public void Dispose() => Complete(outcome: "disposed");
    }
}
