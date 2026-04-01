# Analysis Report: milestone-4-sync-fast-success

Generated: `2026-04-01T08:51:14.8175960+00:00`

## Inputs

- Requests file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-4-synchronous-checkout/workspace/logs/requests.jsonl`
- Jobs file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-4-synchronous-checkout/workspace/logs/jobs.jsonl`
- Filter run id: `milestone-4-sync-fast-success`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `payment-authorize`
- Included run ids: `milestone-4-sync-fast-success`

## Queue State Snapshot

| Metric | Value |
| --- | --- |
| Snapshot time | `2026-04-01T08:51:14.8175960+00:00` |
| Queue run-id filter | `milestone-4-sync-fast-success` |
| Pending count | 1 |
| Ready count | 1 |
| Delayed count | 0 |
| In-progress count | 0 |
| Completed count | 0 |
| Failed count | 0 |
| Oldest queued enqueued UTC | `2026-04-01T08:51:13.0333840+00:00` |
| Oldest queued item age (ms) | 1784.212 |

## Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 1 |
| Completed in window | 1 |
| Window start | `2026-04-01T08:51:13.0053140+00:00` |
| Window end | `2026-04-01T08:51:13.0316670+00:00` |
| Window duration (ms) | 26.353 |
| Average latency (ms) | 26.352 |
| P50 latency (ms) | 26.352 |
| P95 latency (ms) | 26.352 |
| P99 latency (ms) | 26.352 |
| Throughput (req/s) | 37.946 |
| Average concurrency | 1 |
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
| Admitted request count | 1 |
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
| Request count | 1 |
| Completed in window | 1 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 26.352 |
| P95 latency (ms) | 26.352 |
| Throughput (req/s) | 37.946 |
| Average concurrency | 1 |
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

The request window carried `1` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is relatively close to the median, so the selected window looks fairly tight.

Cache hits covered `0%` of the selected requests.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

The live queue snapshot shows `1` pending jobs, `0` in progress, and an oldest queued age of `1784.212` ms.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
