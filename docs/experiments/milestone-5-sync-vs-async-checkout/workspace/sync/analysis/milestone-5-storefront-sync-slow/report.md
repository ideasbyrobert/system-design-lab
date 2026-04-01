# Analysis Report: milestone-5-storefront-sync-slow

Generated: `2026-04-01T08:51:37.3548330+00:00`

## Inputs

- Requests file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-5-sync-vs-async-checkout/workspace/sync/logs/requests.jsonl`
- Jobs file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-5-sync-vs-async-checkout/workspace/sync/logs/jobs.jsonl`
- Filter run id: `milestone-5-storefront-sync-slow`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `storefront-checkout-sync`
- Included run ids: `milestone-5-storefront-sync-slow`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:51:37.3548330+00:00` |
| Queue run-id filter | `milestone-5-storefront-sync-slow` |
| Pending count | 0 |
| Ready count | 0 |
| Delayed count | 0 |
| In-progress count | 0 |
| Completed count | 6 |
| Failed count | 0 |
| Oldest queued enqueued UTC | `n/a` |
| Oldest queued item age (ms) | n/a |

## Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 6 |
| Completed in window | 6 |
| Window start | `2026-04-01T08:51:32.0723350+00:00` |
| Window end | `2026-04-01T08:51:33.0667750+00:00` |
| Window duration (ms) | 994.44 |
| Average latency (ms) | 644.506 |
| P50 latency (ms) | 541.651 |
| P95 latency (ms) | 993.102 |
| P99 latency (ms) | 993.102 |
| Throughput (req/s) | 6.034 |
| Average concurrency | 3.889 |
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
| Average latency (ms) | 644.506 |
| P95 latency (ms) | 993.102 |
| Throughput (req/s) | 6.034 |
| Average concurrency | 3.889 |
| Errors by status | `none` |

## Processed Job Metrics

| Metric | Value |
| --- | --- |
| Job count | 6 |
| Average queue delay (ms) | 3727.835 |
| P95 queue delay (ms) | 3949.213 |
| Average execution (ms) | 15.042 |
| P95 execution (ms) | 23.539 |
| Retry distribution | `0=6` |

## Interpretation

The request window carried `3.889` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is relatively close to the median, so the selected window looks fairly tight.

Cache hits covered `0%` of the selected requests.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows no pending jobs at the analysis moment, so no current backlog is visible.

Average queue delay is larger than average execution time, so waiting dominates work in the current worker sample.
