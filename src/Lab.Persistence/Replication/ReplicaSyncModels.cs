namespace Lab.Persistence.Replication;

public sealed record ReplicaSyncTarget(
    string ReplicaRegion,
    string ReplicaDatabasePath,
    TimeSpan ConfiguredLag);

public sealed record ReplicaSyncBatchResult(
    string PrimaryDatabasePath,
    DateTimeOffset SnapshotCapturedUtc,
    string Mechanism,
    IReadOnlyList<ReplicaSyncResult> Replicas);

public sealed record ReplicaSyncResult(
    string ReplicaRegion,
    string ReplicaDatabasePath,
    string Mechanism,
    DateTimeOffset SnapshotCapturedUtc,
    DateTimeOffset AppliedUtc,
    double ConfiguredLagMs,
    double ObservedLagMs,
    double SnapshotReadMs,
    double ApplyMs,
    int ProductCount,
    int InventoryRecordCount,
    DateTimeOffset? LatestProductUpdatedUtc,
    DateTimeOffset? LatestInventoryUpdatedUtc);
