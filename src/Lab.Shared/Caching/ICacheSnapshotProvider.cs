namespace Lab.Shared.Caching;

public interface ICacheSnapshotProvider
{
    CacheMetricsSnapshot GetSnapshot();
}
