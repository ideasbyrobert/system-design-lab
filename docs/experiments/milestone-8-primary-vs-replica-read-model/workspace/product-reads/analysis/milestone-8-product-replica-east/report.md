# Analysis Report: milestone-8-product-replica-east

Generated: `2026-04-01T08:53:39.9648270+00:00`

## Inputs

- Requests file: `docs/experiments/milestone-8-primary-vs-replica-read-model/workspace/product-reads/logs/requests.jsonl`
- Jobs file: `docs/experiments/milestone-8-primary-vs-replica-read-model/workspace/product-reads/logs/jobs.jsonl`
- Filter run id: `milestone-8-product-replica-east`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `catalog-product-detail`
- Included run ids: `milestone-8-product-replica-east`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:53:39.9648270+00:00` |
| Queue run-id filter | `milestone-8-product-replica-east` |
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
| Request count | 1320 |
| Completed in window | 1320 |
| Window start | `2026-04-01T08:53:23.9803420+00:00` |
| Window end | `2026-04-01T08:53:37.6431390+00:00` |
| Window duration (ms) | 13662.797 |
| Average latency (ms) | 362.417 |
| P50 latency (ms) | 31.593 |
| P95 latency (ms) | 2034.01 |
| P99 latency (ms) | 4187.969 |
| Throughput (req/s) | 96.613 |
| Average concurrency | 35.014 |
| Rate-limited fraction | 0% |
| Cache hit rate | 0% |
| Cache miss rate | 100% |
| Errors by status | `none` |

## Read Freshness Metrics

| Metric | Value |
| --- | --- |
| Read requests with freshness data | 1320 |
| Stale request count | 1320 |
| Stale request fraction | 100% |
| Compared results | 1320 |
| Stale results | 1320 |
| Stale result fraction | 100% |
| Average max staleness age (ms) | 18323.618 |
| Max observed staleness age (ms) | 18323.618 |

| Read source | Requests | Stale requests | Stale fraction | Avg latency (ms) | P95 latency (ms) | Avg max staleness age (ms) | Max staleness age (ms) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `replica-east` | 1320 | 1320 | 100% | 362.417 | 2034.01 | 18323.618 | 18323.618 |

## Overload Metrics

| Metric | Value |
| --- | --- |
| Rejected request count | 0 |
| Reject fraction | 0% |
| Timeout request count | 0 |
| Timeout fraction | 0% |
| Admitted request count | 1320 |
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
| Request count | 1320 |
| Completed in window | 1320 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 362.417 |
| P95 latency (ms) | 2034.01 |
| Throughput (req/s) | 96.613 |
| Average concurrency | 35.014 |
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

The request window carried `35.014` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is much wider than the median, which means the average alone would hide meaningful slowdown risk.

Cache hits covered `0%` of the selected requests.

Freshness was evaluated on `1320` read requests, with `1320` stale responses (`100%`) and a max observed lag of `18323.618` ms.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows no pending jobs at the analysis moment, so no current backlog is visible.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
