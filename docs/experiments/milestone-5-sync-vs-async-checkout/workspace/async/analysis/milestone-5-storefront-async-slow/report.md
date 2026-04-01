# Analysis Report: milestone-5-storefront-async-slow

Generated: `2026-04-01T08:51:49.6892000+00:00`

## Inputs

- Requests file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-5-sync-vs-async-checkout/workspace/async/logs/requests.jsonl`
- Jobs file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-5-sync-vs-async-checkout/workspace/async/logs/jobs.jsonl`
- Filter run id: `milestone-5-storefront-async-slow`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `storefront-checkout-async`
- Included run ids: `milestone-5-storefront-async-slow`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:51:49.6892000+00:00` |
| Queue run-id filter | `milestone-5-storefront-async-slow` |
| Pending count | 0 |
| Ready count | 0 |
| Delayed count | 0 |
| In-progress count | 0 |
| Completed count | 18 |
| Failed count | 0 |
| Oldest queued enqueued UTC | `n/a` |
| Oldest queued item age (ms) | n/a |

## Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 6 |
| Completed in window | 6 |
| Window start | `2026-04-01T08:51:42.1329950+00:00` |
| Window end | `2026-04-01T08:51:42.9260250+00:00` |
| Window duration (ms) | 793.03 |
| Average latency (ms) | 413.246 |
| P50 latency (ms) | 336.329 |
| P95 latency (ms) | 792.957 |
| P99 latency (ms) | 792.957 |
| Throughput (req/s) | 7.566 |
| Average concurrency | 3.127 |
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
| Admitted request count | 6 |
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
| Request count | 6 |
| Completed in window | 6 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 413.246 |
| P95 latency (ms) | 792.957 |
| Throughput (req/s) | 7.566 |
| Average concurrency | 3.127 |
| Errors by status | `none` |

## Processed Job Metrics

| Metric | Value |
| --- | --- |
| Job count | 18 |
| Average queue delay (ms) | 3880.154 |
| P95 queue delay (ms) | 6184.926 |
| Average execution (ms) | 129.297 |
| P95 execution (ms) | 360.174 |
| Retry distribution | `0=18` |

## Interpretation

The request window carried `3.127` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is much wider than the median, which means the average alone would hide meaningful slowdown risk.

Cache hits covered `0%` of the selected requests.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows no pending jobs at the analysis moment, so no current backlog is visible.

Average queue delay is larger than average execution time, so waiting dominates work in the current worker sample.
