# Analysis Report: milestone-9-local-replica-unavailable

Generated: `2026-04-01T08:55:17.3880210+00:00`

## Inputs

- Requests file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-degraded-mode-failover/workspace/local-replica-unavailable/logs/requests.jsonl`
- Jobs file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-degraded-mode-failover/workspace/local-replica-unavailable/logs/jobs.jsonl`
- Filter run id: `milestone-9-local-replica-unavailable`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `product-page`
- Included run ids: `milestone-9-local-replica-unavailable`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:55:17.3880210+00:00` |
| Queue run-id filter | `milestone-9-local-replica-unavailable` |
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
| Request count | 48 |
| Completed in window | 48 |
| Window start | `2026-04-01T08:55:11.2997400+00:00` |
| Window end | `2026-04-01T08:55:16.7469020+00:00` |
| Window duration (ms) | 5447.162 |
| Average latency (ms) | 10.413 |
| P50 latency (ms) | 0.229 |
| P95 latency (ms) | 61.564 |
| P99 latency (ms) | 82.443 |
| Throughput (req/s) | 8.812 |
| Average concurrency | 0.092 |
| Rate-limited fraction | 0% |
| Cache hit rate | 83.33% |
| Cache miss rate | 16.67% |
| Errors by status | `none` |

## Read Freshness Metrics

| Metric | Value |
| --- | --- |
| Read requests with freshness data | 48 |
| Stale request count | 0 |
| Stale request fraction | 0% |
| Compared results | 48 |
| Stale results | 0 |
| Stale result fraction | 0% |
| Average max staleness age (ms) | n/a |
| Max observed staleness age (ms) | n/a |

| Read source | Requests | Stale requests | Stale fraction | Avg latency (ms) | P95 latency (ms) | Avg max staleness age (ms) | Max staleness age (ms) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `primary` | 48 | 0 | 0% | 10.413 | 61.564 | n/a | n/a |

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
| Average latency (ms) | 10.413 |
| P95 latency (ms) | 61.564 |
| Throughput (req/s) | 8.812 |
| Average concurrency | 0.092 |
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

The request window carried `0.092` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is much wider than the median, which means the average alone would hide meaningful slowdown risk.

Cache hits covered `83.33%` of the selected requests.

Freshness was evaluated on `48` read requests, with `0` stale responses (`0%`) and a max observed lag of `n/a` ms.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows no pending jobs at the analysis moment, so no current backlog is visible.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
