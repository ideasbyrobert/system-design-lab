# Analysis Report: milestone-8-product-primary

Generated: `2026-04-01T08:53:19.4446140+00:00`

## Inputs

- Requests file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-8-primary-vs-replica-read-model/workspace/product-reads/logs/requests.jsonl`
- Jobs file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-8-primary-vs-replica-read-model/workspace/product-reads/logs/jobs.jsonl`
- Filter run id: `milestone-8-product-primary`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `catalog-product-detail`
- Included run ids: `milestone-8-product-primary`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:53:19.4446140+00:00` |
| Queue run-id filter | `milestone-8-product-primary` |
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
| Window start | `2026-04-01T08:53:10.5531290+00:00` |
| Window end | `2026-04-01T08:53:17.4287640+00:00` |
| Window duration (ms) | 6875.635 |
| Average latency (ms) | 38.873 |
| P50 latency (ms) | 3.471 |
| P95 latency (ms) | 6.582 |
| P99 latency (ms) | 1006.646 |
| Throughput (req/s) | 191.982 |
| Average concurrency | 7.463 |
| Rate-limited fraction | 0% |
| Cache hit rate | 0% |
| Cache miss rate | 100% |
| Errors by status | `none` |

## Read Freshness Metrics

| Metric | Value |
| --- | --- |
| Read requests with freshness data | 1320 |
| Stale request count | 0 |
| Stale request fraction | 0% |
| Compared results | 1320 |
| Stale results | 0 |
| Stale result fraction | 0% |
| Average max staleness age (ms) | n/a |
| Max observed staleness age (ms) | n/a |

| Read source | Requests | Stale requests | Stale fraction | Avg latency (ms) | P95 latency (ms) | Avg max staleness age (ms) | Max staleness age (ms) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `primary` | 1320 | 0 | 0% | 38.873 | 6.582 | n/a | n/a |

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
| Average latency (ms) | 38.873 |
| P95 latency (ms) | 6.582 |
| Throughput (req/s) | 191.982 |
| Average concurrency | 7.463 |
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

The request window carried `7.463` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is much wider than the median, which means the average alone would hide meaningful slowdown risk.

Cache hits covered `0%` of the selected requests.

Freshness was evaluated on `1320` read requests, with `0` stale responses (`0%`) and a max observed lag of `n/a` ms.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows no pending jobs at the analysis moment, so no current backlog is visible.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
