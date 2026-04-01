# Analysis Report: milestone-7-frontend-cpu-two-instance

Generated: `2026-04-01T08:52:34.7052450+00:00`

## Inputs

- Requests file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/workspace/frontend-cpu/two-instance/logs/requests.jsonl`
- Jobs file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/workspace/frontend-cpu/two-instance/logs/jobs.jsonl`
- Filter run id: `milestone-7-frontend-cpu-two-instance`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `cpu-bound-lab`
- Included run ids: `milestone-7-frontend-cpu-two-instance`

## Queue State Snapshot

No live queue snapshot was captured. Reason: `Primary database file '/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/workspace/frontend-cpu/two-instance/data/primary.db' does not exist.`

## Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 48 |
| Completed in window | 48 |
| Window start | `2026-04-01T08:52:27.6904280+00:00` |
| Window end | `2026-04-01T08:52:33.9955300+00:00` |
| Window duration (ms) | 6305.102 |
| Average latency (ms) | 360.591 |
| P50 latency (ms) | 358.182 |
| P95 latency (ms) | 379.743 |
| P99 latency (ms) | 381.151 |
| Throughput (req/s) | 7.613 |
| Average concurrency | 2.745 |
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
| Average latency (ms) | 360.591 |
| P95 latency (ms) | 379.743 |
| Throughput (req/s) | 7.613 |
| Average concurrency | 2.745 |
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

The request window carried `2.745` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is relatively close to the median, so the selected window looks fairly tight.

Cache hits covered `0%` of the selected requests.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

No live queue snapshot was available, so current backlog counts and oldest queued age could not be reported.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.
