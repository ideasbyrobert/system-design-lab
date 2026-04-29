# Analysis Report: milestone-2-cache-on

Generated: `2026-04-01T08:51:05.0413670+00:00`

## Inputs

- Requests file: `docs/experiments/milestone-2-cache-off-vs-cache-on/workspace/logs/requests.jsonl`
- Jobs file: `docs/experiments/milestone-2-cache-off-vs-cache-on/workspace/logs/jobs.jsonl`
- Filter run id: `milestone-2-cache-on`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `product-page`
- Included run ids: `milestone-2-cache-on`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:51:05.0413670+00:00` |
| Queue run-id filter | `milestone-2-cache-on` |
| Pending count | 0 |
| Ready count | 0 |
| Delayed count | 0 |
| In-progress count | 0 |
| Completed count | 0 |
| Failed count | 0 |
| Oldest queued enqueued UTC | `n/a` |
| Oldest queued item age (ms) | n/a |

## Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 3600 |
| Completed in window | 3600 |
| Window start | `2026-04-01T08:50:56.4583280+00:00` |
| Window end | `2026-04-01T08:51:02.4278110+00:00` |
| Window duration (ms) | 5969.483 |
| Average latency (ms) | 10.345 |
| P50 latency (ms) | 0.056 |
| P95 latency (ms) | 0.125 |
| P99 latency (ms) | 0.178 |
| Throughput (req/s) | 603.067 |
| Average concurrency | 6.239 |
| Rate-limited fraction | 0% |
| Cache hit rate | 99.33% |
| Cache miss rate | 0.67% |
| Errors by status | `none` |

## Read Freshness Metrics

| Metric | Value |
| --- | --- |
| Read requests with freshness data | 3600 |
| Stale request count | 0 |
| Stale request fraction | 0% |
| Compared results | 3600 |
| Stale results | 0 |
| Stale result fraction | 0% |
| Average max staleness age (ms) | n/a |
| Max observed staleness age (ms) | n/a |

| Read source | Requests | Stale requests | Stale fraction | Avg latency (ms) | P95 latency (ms) | Avg max staleness age (ms) | Max staleness age (ms) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `primary` | 3600 | 0 | 0% | 10.345 | 0.125 | n/a | n/a |

## Overload Metrics

| Metric | Value |
| --- | --- |
| Rejected request count | 0 |
| Reject fraction | 0% |
| Timeout request count | 0 |
| Timeout fraction | 0% |
| Admitted request count | 3600 |
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
| Request count | 3600 |
| Completed in window | 3600 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 10.345 |
| P95 latency (ms) | 0.125 |
| Throughput (req/s) | 603.067 |
| Average concurrency | 6.239 |
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

The request window carried `6.239` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is much wider than the median, which means the average alone would hide meaningful slowdown risk.

Cache hits covered `99.33%` of the selected requests.

Freshness was evaluated on `3600` read requests, with `0` stale responses (`0%`) and a max observed lag of `n/a` ms.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows no pending jobs at the analysis moment, so no current backlog is visible.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
