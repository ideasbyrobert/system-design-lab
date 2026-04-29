# Analysis Report: milestone-8-order-history-read-model

Generated: `2026-04-01T08:54:14.5131650+00:00`

## Inputs

- Requests file: `docs/experiments/milestone-8-primary-vs-replica-read-model/workspace/order-history/logs/requests.jsonl`
- Jobs file: `docs/experiments/milestone-8-primary-vs-replica-read-model/workspace/order-history/logs/jobs.jsonl`
- Filter run id: `milestone-8-order-history-read-model`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `order-history`
- Included run ids: `milestone-8-order-history-read-model`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:54:14.5131650+00:00` |
| Queue run-id filter | `milestone-8-order-history-read-model` |
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
| Request count | 1080 |
| Completed in window | 1080 |
| Window start | `2026-04-01T08:54:04.7829780+00:00` |
| Window end | `2026-04-01T08:54:12.5072690+00:00` |
| Window duration (ms) | 7724.291 |
| Average latency (ms) | 30.016 |
| P50 latency (ms) | 4.545 |
| P95 latency (ms) | 7.983 |
| P99 latency (ms) | 1008.365 |
| Throughput (req/s) | 139.819 |
| Average concurrency | 4.197 |
| Rate-limited fraction | 0% |
| Cache hit rate | 0% |
| Cache miss rate | 100% |
| Errors by status | `none` |

## Read Freshness Metrics

| Metric | Value |
| --- | --- |
| Read requests with freshness data | 1080 |
| Stale request count | 1080 |
| Stale request fraction | 100% |
| Compared results | 27000 |
| Stale results | 1080 |
| Stale result fraction | 4% |
| Average max staleness age (ms) | 20451.644 |
| Max observed staleness age (ms) | 23448.756 |

| Read source | Requests | Stale requests | Stale fraction | Avg latency (ms) | P95 latency (ms) | Avg max staleness age (ms) | Max staleness age (ms) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `read-model` | 1080 | 1080 | 100% | 30.016 | 7.983 | 20451.644 | 23448.756 |

## Overload Metrics

| Metric | Value |
| --- | --- |
| Rejected request count | 0 |
| Reject fraction | 0% |
| Timeout request count | 0 |
| Timeout fraction | 0% |
| Admitted request count | 1080 |
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
| Request count | 1080 |
| Completed in window | 1080 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 30.016 |
| P95 latency (ms) | 7.983 |
| Throughput (req/s) | 139.819 |
| Average concurrency | 4.197 |
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

The request window carried `4.197` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is much wider than the median, which means the average alone would hide meaningful slowdown risk.

Cache hits covered `0%` of the selected requests.

Freshness was evaluated on `1080` read requests, with `1080` stale responses (`100%`) and a max observed lag of `23448.756` ms.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows no pending jobs at the analysis moment, so no current backlog is visible.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
