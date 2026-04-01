using System.Collections.ObjectModel;
using Lab.Shared.Contracts;

namespace Lab.Telemetry.RequestTracing;

public sealed class RequestTraceContext
{
    private readonly TimeProvider _timeProvider;
    private readonly long _arrivalTimestamp;
    private readonly long _startTimestamp;
    private readonly List<StageTimingRecord> _stageTimings = [];
    private readonly List<DependencyCallRecord> _dependencyCalls = [];
    private readonly List<string> _notes = [];
    private bool _completed;

    internal RequestTraceContext(
        TimeProvider timeProvider,
        string runId,
        string traceId,
        string spanId,
        string requestId,
        string service,
        string region,
        string route,
        string method,
        OperationContractDescriptor contract,
        DateTimeOffset arrivalUtc,
        long arrivalTimestamp,
        DateTimeOffset startUtc,
        long startTimestamp,
        string? userId,
        string? correlationId)
    {
        _timeProvider = timeProvider;
        _arrivalTimestamp = arrivalTimestamp;
        _startTimestamp = startTimestamp;
        RunId = runId;
        TraceId = traceId;
        SpanId = spanId;
        RequestId = requestId;
        Service = service;
        Region = region;
        Route = route;
        Method = method;
        Contract = contract;
        ArrivalUtc = arrivalUtc;
        StartUtc = startUtc;
        UserId = userId;
        CorrelationId = correlationId;
    }

    public string RunId { get; }

    public string TraceId { get; }

    public string SpanId { get; }

    public string RequestId { get; }

    public string Service { get; }

    public string Region { get; }

    public string Route { get; }

    public string Method { get; }

    public OperationContractDescriptor Contract { get; private set; }

    public DateTimeOffset ArrivalUtc { get; }

    public DateTimeOffset StartUtc { get; }

    public bool ContractSatisfied { get; private set; }

    public bool CacheHit { get; private set; }

    public bool RateLimited { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? UserId { get; private set; }

    public string? SessionKey { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? ReadSource { get; private set; }

    public int? FreshnessComparedCount { get; private set; }

    public int? FreshnessStaleCount { get; private set; }

    public double? FreshnessStaleFraction { get; private set; }

    public double? MaxStalenessAgeMs { get; private set; }

    public IReadOnlyList<StageTimingRecord> StageTimings => _stageTimings.AsReadOnly();

    public IReadOnlyList<DependencyCallRecord> DependencyCalls => _dependencyCalls.AsReadOnly();

    public IReadOnlyList<string> Notes => _notes.AsReadOnly();

    public bool IsCompleted => _completed;

    public void MarkContractSatisfied(bool value = true)
    {
        EnsureNotCompleted();
        ContractSatisfied = value;
    }

    public void MarkCacheHit(bool value = true)
    {
        EnsureNotCompleted();
        CacheHit = value;
    }

    public void MarkRateLimited(bool value = true)
    {
        EnsureNotCompleted();
        RateLimited = value;
    }

    public void SetErrorCode(string? errorCode)
    {
        EnsureNotCompleted();
        ErrorCode = NormalizeOptionalText(errorCode);
    }

    public void SetUserId(string? userId)
    {
        EnsureNotCompleted();
        UserId = NormalizeOptionalText(userId);
    }

    public void SetSessionKey(string? sessionKey)
    {
        EnsureNotCompleted();
        SessionKey = NormalizeOptionalText(sessionKey);
    }

    public void SetCorrelationId(string? correlationId)
    {
        EnsureNotCompleted();
        CorrelationId = NormalizeOptionalText(correlationId);
    }

    public void SetReadSource(string? readSource)
    {
        EnsureNotCompleted();
        ReadSource = NormalizeOptionalText(readSource);
    }

    public void SetFreshnessMetrics(
        int? comparedCount,
        int? staleCount,
        double? maxStalenessAgeMs)
    {
        EnsureNotCompleted();

        if (comparedCount is not null && comparedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(comparedCount), "Freshness compared count cannot be negative.");
        }

        if (staleCount is not null && staleCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staleCount), "Freshness stale count cannot be negative.");
        }

        if (comparedCount is not null && staleCount is not null && staleCount > comparedCount)
        {
            throw new ArgumentOutOfRangeException(nameof(staleCount), "Freshness stale count cannot exceed the compared count.");
        }

        if (maxStalenessAgeMs is not null && maxStalenessAgeMs < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStalenessAgeMs), "Max staleness age cannot be negative.");
        }

        FreshnessComparedCount = comparedCount;
        FreshnessStaleCount = staleCount;
        FreshnessStaleFraction =
            comparedCount.HasValue &&
            staleCount.HasValue &&
            comparedCount.Value > 0
                ? staleCount.Value / (double)comparedCount.Value
                : null;
        MaxStalenessAgeMs = maxStalenessAgeMs;
    }

    public void SetOperationContract(OperationContractDescriptor contract)
    {
        EnsureNotCompleted();
        ArgumentNullException.ThrowIfNull(contract);
        Contract = contract;
    }

    public void AddNote(string note)
    {
        EnsureNotCompleted();

        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        _notes.Add(note.Trim());
    }

    public StageTraceScope BeginStage(string stageName, IReadOnlyDictionary<string, string?>? metadata = null)
    {
        EnsureNotCompleted();
        return new StageTraceScope(this, stageName, metadata);
    }

    public DependencyCallScope BeginDependencyCall(
        string dependencyName,
        string route,
        string region,
        IReadOnlyDictionary<string, string?>? metadata = null,
        IEnumerable<string>? notes = null)
    {
        EnsureNotCompleted();
        return new DependencyCallScope(this, dependencyName, route, region, metadata, notes);
    }

    public void RecordStage(
        string stageName,
        DateTimeOffset startedUtc,
        DateTimeOffset endedUtc,
        long startedTimestamp,
        long endedTimestamp,
        string outcome = "completed",
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        EnsureNotCompleted();

        _stageTimings.Add(new StageTimingRecord
        {
            StageName = RequireText(stageName, nameof(stageName)),
            StartedUtc = startedUtc,
            EndedUtc = endedUtc,
            ElapsedMs = _timeProvider.GetElapsedTime(startedTimestamp, endedTimestamp).TotalMilliseconds,
            Outcome = RequireText(outcome, nameof(outcome)),
            Metadata = FreezeMetadata(metadata)
        });
    }

    public void RecordDependencyCall(
        string dependencyName,
        string route,
        string region,
        DateTimeOffset startedUtc,
        DateTimeOffset endedUtc,
        long startedTimestamp,
        long endedTimestamp,
        int? statusCode = null,
        string outcome = "completed",
        IReadOnlyDictionary<string, string?>? metadata = null,
        IEnumerable<string>? notes = null)
    {
        EnsureNotCompleted();

        _dependencyCalls.Add(new DependencyCallRecord
        {
            DependencyName = RequireText(dependencyName, nameof(dependencyName)),
            Route = RequireText(route, nameof(route)),
            Region = RequireText(region, nameof(region)),
            StartedUtc = startedUtc,
            EndedUtc = endedUtc,
            ElapsedMs = _timeProvider.GetElapsedTime(startedTimestamp, endedTimestamp).TotalMilliseconds,
            StatusCode = statusCode,
            Outcome = RequireText(outcome, nameof(outcome)),
            Metadata = FreezeMetadata(metadata),
            Notes = FreezeNotes(notes)
        });
    }

    public void RecordInstantStage(
        string stageName,
        string outcome = "observed",
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        EnsureNotCompleted();

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        long nowTimestamp = _timeProvider.GetTimestamp();

        RecordStage(
            stageName,
            nowUtc,
            nowUtc,
            nowTimestamp,
            nowTimestamp,
            outcome,
            metadata);
    }

    public RequestTraceRecord Complete(int statusCode, string? errorCode = null)
    {
        EnsureNotCompleted();

        long completionTimestamp = _timeProvider.GetTimestamp();
        DateTimeOffset completionUtc = _timeProvider.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            ErrorCode = errorCode.Trim();
        }

        _completed = true;

        return new RequestTraceRecord
        {
            RunId = RunId,
            TraceId = TraceId,
            SpanId = SpanId,
            RequestId = RequestId,
            Operation = Contract.OperationName,
            Region = Region,
            Service = Service,
            Route = Route,
            Method = Method,
            ArrivalUtc = ArrivalUtc,
            StartUtc = StartUtc,
            CompletionUtc = completionUtc,
            LatencyMs = _timeProvider.GetElapsedTime(_arrivalTimestamp, completionTimestamp).TotalMilliseconds,
            StatusCode = statusCode,
            ContractSatisfied = ContractSatisfied,
            CacheHit = CacheHit,
            RateLimited = RateLimited,
            DependencyCalls = _dependencyCalls.ToArray(),
            StageTimings = _stageTimings.ToArray(),
            ErrorCode = ErrorCode,
            UserId = UserId,
            SessionKey = SessionKey,
            CorrelationId = CorrelationId,
            ReadSource = ReadSource,
            FreshnessComparedCount = FreshnessComparedCount,
            FreshnessStaleCount = FreshnessStaleCount,
            FreshnessStaleFraction = FreshnessStaleFraction,
            MaxStalenessAgeMs = MaxStalenessAgeMs,
            Notes = _notes.ToArray()
        };
    }

    private static IReadOnlyDictionary<string, string?> FreezeMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        Dictionary<string, string?> copy = new(StringComparer.Ordinal);

        if (metadata is not null)
        {
            foreach ((string key, string? value) in metadata)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    copy[key.Trim()] = value;
                }
            }
        }

        return new ReadOnlyDictionary<string, string?>(copy);
    }

    private static IReadOnlyList<string> FreezeNotes(IEnumerable<string>? notes)
    {
        string[] copy = (notes ?? [])
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Select(note => note.Trim())
            .ToArray();

        return Array.AsReadOnly(copy);
    }

    private void EnsureNotCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The request trace has already been completed.");
        }
    }

    private static string RequireText(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class StageTraceScope : IDisposable
    {
        private readonly RequestTraceContext _owner;
        private readonly string _stageName;
        private readonly IReadOnlyDictionary<string, string?>? _metadata;
        private readonly DateTimeOffset _startedUtc;
        private readonly long _startedTimestamp;
        private bool _recorded;

        internal StageTraceScope(
            RequestTraceContext owner,
            string stageName,
            IReadOnlyDictionary<string, string?>? metadata)
        {
            _owner = owner;
            _stageName = RequireText(stageName, nameof(stageName));
            _metadata = metadata;
            _startedUtc = owner._timeProvider.GetUtcNow();
            _startedTimestamp = owner._timeProvider.GetTimestamp();
        }

        public void Complete(string outcome = "completed", IReadOnlyDictionary<string, string?>? metadata = null)
        {
            if (_recorded)
            {
                throw new InvalidOperationException("The stage has already been recorded.");
            }

            DateTimeOffset endedUtc = _owner._timeProvider.GetUtcNow();
            long endedTimestamp = _owner._timeProvider.GetTimestamp();

            _owner.RecordStage(
                _stageName,
                _startedUtc,
                endedUtc,
                _startedTimestamp,
                endedTimestamp,
                outcome,
                MergeMetadata(_metadata, metadata));

            _recorded = true;
        }

        public void Dispose()
        {
            if (!_recorded)
            {
                Complete();
            }
        }
    }

    public sealed class DependencyCallScope : IDisposable
    {
        private readonly RequestTraceContext _owner;
        private readonly string _dependencyName;
        private readonly string _route;
        private readonly string _region;
        private readonly IReadOnlyDictionary<string, string?>? _metadata;
        private readonly IReadOnlyList<string> _notes;
        private readonly DateTimeOffset _startedUtc;
        private readonly long _startedTimestamp;
        private bool _recorded;

        internal DependencyCallScope(
            RequestTraceContext owner,
            string dependencyName,
            string route,
            string region,
            IReadOnlyDictionary<string, string?>? metadata,
            IEnumerable<string>? notes)
        {
            _owner = owner;
            _dependencyName = RequireText(dependencyName, nameof(dependencyName));
            _route = RequireText(route, nameof(route));
            _region = RequireText(region, nameof(region));
            _metadata = metadata;
            _notes = FreezeNotes(notes);
            _startedUtc = owner._timeProvider.GetUtcNow();
            _startedTimestamp = owner._timeProvider.GetTimestamp();
        }

        public void Complete(
            int? statusCode = null,
            string outcome = "completed",
            IReadOnlyDictionary<string, string?>? metadata = null,
            IEnumerable<string>? notes = null)
        {
            if (_recorded)
            {
                throw new InvalidOperationException("The dependency call has already been recorded.");
            }

            DateTimeOffset endedUtc = _owner._timeProvider.GetUtcNow();
            long endedTimestamp = _owner._timeProvider.GetTimestamp();

            _owner.RecordDependencyCall(
                _dependencyName,
                _route,
                _region,
                _startedUtc,
                endedUtc,
                _startedTimestamp,
                endedTimestamp,
                statusCode,
                outcome,
                MergeMetadata(_metadata, metadata),
                _notes.Concat(FreezeNotes(notes)));

            _recorded = true;
        }

        public void Dispose()
        {
            if (!_recorded)
            {
                Complete();
            }
        }
    }

    private static IReadOnlyDictionary<string, string?> MergeMetadata(
        IReadOnlyDictionary<string, string?>? left,
        IReadOnlyDictionary<string, string?>? right)
    {
        Dictionary<string, string?> merged = new(StringComparer.Ordinal);

        if (left is not null)
        {
            foreach ((string key, string? value) in left)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    merged[key.Trim()] = value;
                }
            }
        }

        if (right is not null)
        {
            foreach ((string key, string? value) in right)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    merged[key.Trim()] = value;
                }
            }
        }

        return new ReadOnlyDictionary<string, string?>(merged);
    }
}
