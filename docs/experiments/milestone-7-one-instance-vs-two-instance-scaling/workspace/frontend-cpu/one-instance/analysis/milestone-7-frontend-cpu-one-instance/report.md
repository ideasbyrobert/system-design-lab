# Analysis Report: milestone-7-frontend-cpu-one-instance

Generated: `2026-04-01T08:52:23.5583280+00:00`

## Inputs

- Requests file: `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/workspace/frontend-cpu/one-instance/logs/requests.jsonl`
- Jobs file: `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/workspace/frontend-cpu/one-instance/logs/jobs.jsonl`
- Filter run id: `milestone-7-frontend-cpu-one-instance`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `cpu-bound-lab`
- Included run ids: `milestone-7-frontend-cpu-one-instance`

## Queue State Snapshot

No live queue snapshot was captured. Reason: `Primary database file 'docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/workspace/frontend-cpu/one-instance/data/primary.db' does not exist.`

## Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 48 |
| Completed in window | 48 |
| Window start | `2026-04-01T08:52:12.5304220+00:00` |
| Window end | `2026-04-01T08:52:22.8804840+00:00` |
| Window duration (ms) | 10350.062 |
| Average latency (ms) | 357.183 |
| P50 latency (ms) | 355.095 |
| P95 latency (ms) | 376.935 |
| P99 latency (ms) | 380.126 |
| Throughput (req/s) | 4.638 |
| Average concurrency | 1.657 |
| Rate-limited fraction | 0% |
| Cache hit rate | 0% |
| Cache miss rate | 100% |
| Errors by status | `none` |

## Read Freshness Metrics

| Metric | Value |
| --- | --- |
| Read requests with freshness data | 0 |
| Stale request count | 0 |
| Stale request fraction | n/a |
| Compared results | 0 |
| Stale results | 0 |
| Stale result fraction | n/a |
| Average max staleness age (ms) | n/a |
| Max observed staleness age (ms) | n/a |

## Overload Metrics

| Metric | Value |
| --- | --- |
| Rejected request count | 0 |
| Reject fraction | 0% |
| Timeout request count | 0 |
| Timeout fraction | 0% |
| Admitted request count | 48 |
| Admitted fraction | 100% |
| Retried job count | 0 |
| Total retry attempts | 0 |

## Rate-Limited Requests

| Metric | Value |
| --- | --- |
| Request count | 0 |
| Completed in window | 0 |
| Fraction of selected requests | 0% |
| Average latency (ms) | n/a |
| P95 latency (ms) | n/a |
| Throughput (req/s) | 0 |
| Average concurrency | 0 |
| Errors by status | `none` |

## Admitted Requests

| Metric | Value |
| --- | --- |
| Request count | 48 |
| Completed in window | 48 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 357.183 |
| P95 latency (ms) | 376.935 |
| Throughput (req/s) | 4.638 |
| Average concurrency | 1.657 |
| Errors by status | `none` |

## Processed Job Metrics

| Metric | Value |
| --- | --- |
| Job count | 0 |
| Average queue delay (ms) | n/a |
| P95 queue delay (ms) | n/a |
| Average execution (ms) | n/a |
| P95 execution (ms) | n/a |
| Retry distribution | `none` |

## Interpretation

The request window carried `1.657` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is relatively close to the median, so the selected window looks fairly tight.

Cache hits covered `0%` of the selected requests.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

No live queue snapshot was available, so current backlog counts and oldest queued age could not be reported.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
