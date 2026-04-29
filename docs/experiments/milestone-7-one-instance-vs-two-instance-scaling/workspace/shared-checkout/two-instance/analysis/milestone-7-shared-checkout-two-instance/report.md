# Analysis Report: milestone-7-shared-checkout-two-instance

Generated: `2026-04-01T08:52:59.2125690+00:00`

## Inputs

- Requests file: `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/workspace/shared-checkout/two-instance/logs/requests.jsonl`
- Jobs file: `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/workspace/shared-checkout/two-instance/logs/jobs.jsonl`
- Filter run id: `milestone-7-shared-checkout-two-instance`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `payment-authorize`
- Included run ids: `milestone-7-shared-checkout-two-instance`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:52:59.2125690+00:00` |
| Queue run-id filter | `milestone-7-shared-checkout-two-instance` |
| Pending count | 12 |
| Ready count | 12 |
| Delayed count | 0 |
| In-progress count | 0 |
| Completed count | 0 |
| Failed count | 0 |
| Oldest queued enqueued UTC | `2026-04-01T08:52:55.6316980+00:00` |
| Oldest queued item age (ms) | 3580.871 |

## Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 12 |
| Completed in window | 12 |
| Window start | `2026-04-01T08:52:54.9285520+00:00` |
| Window end | `2026-04-01T08:52:56.1078260+00:00` |
| Window duration (ms) | 1179.274 |
| Average latency (ms) | 701.318 |
| P50 latency (ms) | 701.508 |
| P95 latency (ms) | 702.083 |
| P99 latency (ms) | 702.083 |
| Throughput (req/s) | 10.176 |
| Average concurrency | 7.136 |
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
| Admitted request count | 12 |
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
| Request count | 12 |
| Completed in window | 12 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 701.318 |
| P95 latency (ms) | 702.083 |
| Throughput (req/s) | 10.176 |
| Average concurrency | 7.136 |
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

The request window carried `7.136` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is relatively close to the median, so the selected window looks fairly tight.

Cache hits covered `0%` of the selected requests.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows `12` pending jobs, `0` in progress, and an oldest queued age of `3580.871` ms.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
