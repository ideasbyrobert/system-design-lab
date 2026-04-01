# Analysis Report: milestone-6-storefront-sync-no-limit

Generated: `2026-04-01T08:51:59.6914130+00:00`

## Inputs

- Requests file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/workspace/no-limit/logs/requests.jsonl`
- Jobs file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/workspace/no-limit/logs/jobs.jsonl`
- Filter run id: `milestone-6-storefront-sync-no-limit`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `payment-authorize`
- Included run ids: `milestone-6-storefront-sync-no-limit`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:51:59.6914130+00:00` |
| Queue run-id filter | `milestone-6-storefront-sync-no-limit` |
| Pending count | 80 |
| Ready count | 80 |
| Delayed count | 0 |
| In-progress count | 0 |
| Completed count | 0 |
| Failed count | 0 |
| Oldest queued enqueued UTC | `2026-04-01T08:51:56.3668260+00:00` |
| Oldest queued item age (ms) | 3324.587 |

## Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 80 |
| Completed in window | 80 |
| Window start | `2026-04-01T08:51:56.3601150+00:00` |
| Window end | `2026-04-01T08:51:57.4217240+00:00` |
| Window duration (ms) | 1061.609 |
| Average latency (ms) | 5.742 |
| P50 latency (ms) | 5.92 |
| P95 latency (ms) | 6.084 |
| P99 latency (ms) | 6.37 |
| Throughput (req/s) | 75.357 |
| Average concurrency | 0.433 |
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
| Admitted request count | 80 |
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
| Request count | 80 |
| Completed in window | 80 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 5.742 |
| P95 latency (ms) | 6.084 |
| Throughput (req/s) | 75.357 |
| Average concurrency | 0.433 |
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

The request window carried `0.433` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is relatively close to the median, so the selected window looks fairly tight.

Cache hits covered `0%` of the selected requests.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows `80` pending jobs, `0` in progress, and an oldest queued age of `3324.587` ms.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
