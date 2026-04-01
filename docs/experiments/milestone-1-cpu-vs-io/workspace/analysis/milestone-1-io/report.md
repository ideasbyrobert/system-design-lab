# Analysis Report: milestone-1-io

Generated: `2026-04-01T08:50:29.1925600+00:00`

## Inputs

- Requests file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-1-cpu-vs-io/workspace/logs/requests.jsonl`
- Jobs file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-1-cpu-vs-io/workspace/logs/jobs.jsonl`
- Filter run id: `milestone-1-io`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `all`
- Included run ids: `milestone-1-io`

## Queue State Snapshot

No live queue snapshot was captured. Reason: `Primary database file '/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-1-cpu-vs-io/workspace/data/primary.db' does not exist.`

## Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 16 |
| Completed in window | 16 |
| Window start | `2026-04-01T08:50:20.0311630+00:00` |
| Window end | `2026-04-01T08:50:27.5997420+00:00` |
| Window duration (ms) | 7568.579 |
| Average latency (ms) | 91.46 |
| P50 latency (ms) | 91.267 |
| P95 latency (ms) | 94.084 |
| P99 latency (ms) | 94.084 |
| Throughput (req/s) | 2.114 |
| Average concurrency | 0.193 |
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
| Admitted request count | 16 |
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
| Request count | 16 |
| Completed in window | 16 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 91.46 |
| P95 latency (ms) | 94.084 |
| Throughput (req/s) | 2.114 |
| Average concurrency | 0.193 |
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

The request window carried `0.193` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is relatively close to the median, so the selected window looks fairly tight.

Cache hits covered `0%` of the selected requests.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

No live queue snapshot was available, so current backlog counts and oldest queued age could not be reported.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
