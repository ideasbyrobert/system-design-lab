# Analysis Report: milestone-1-cpu

Generated: `2026-04-01T09:18:03.2795560+00:00`

## Contract

This analyzer report covers the selected trace set for run filter `milestone-1-cpu` and operation filter `all`.

It does not invent extra business promises beyond the trace data. It describes the behavior that actually crossed the selected observed boundary.

## Observation Boundary

The request boundary in this report is reconstructed from request trace lifetimes:

- start: request arrival recorded in the selected trace
- end: request completion recorded in the selected trace
- average concurrency: reconstructed from overlap of full observed lifetimes, not from thread counts

Processed job metrics are reported separately from job traces, and any live queue snapshot is a point-in-time view rather than a replay of the whole window.

## Topology

- Requests file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-1-cpu-vs-io/workspace/logs/requests.jsonl`
- Jobs file: `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-1-cpu-vs-io/workspace/logs/jobs.jsonl`
- Included run ids: `milestone-1-cpu`
- Live queue snapshot captured: `no`
- Read freshness data present: `no`
- Processed job traces present: `no`

Detailed process counts, regions, and dependency layouts must come from the surrounding experiment docs; the analyzer only reports what the selected traces and queue snapshot contain.

## Workload

- Filter run id: `milestone-1-cpu`
- Filter from: `n/a`
- Filter to: `n/a`
- Filter operation: `all`
- Request count selected: `16`
- Completed requests in window: `16`
- Job count selected: `0`

If you need the intended offered load, endpoint path, or scenario setup, pair this auto-generated report with the milestone experiment docs.

## Results

### Queue State Snapshot

No live queue snapshot was captured. Reason: `Primary database file '/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-1-cpu-vs-io/workspace/data/primary.db' does not exist.`

### Request Metrics

| Metric | Value |
| --- | --- |
| Request count | 16 |
| Completed in window | 16 |
| Window start | `2026-04-01T08:50:11.8047340+00:00` |
| Window end | `2026-04-01T08:50:19.3597750+00:00` |
| Window duration (ms) | 7555.041 |
| Average latency (ms) | 78.356 |
| P50 latency (ms) | 78.104 |
| P95 latency (ms) | 82.299 |
| P99 latency (ms) | 82.299 |
| Throughput (req/s) | 2.118 |
| Average concurrency | 0.166 |
| Rate-limited fraction | 0% |
| Cache hit rate | 0% |
| Cache miss rate | 100% |
| Errors by status | `none` |

### Read Freshness Metrics

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

### Overload Metrics

| Metric | Value |
| --- | --- |
| Rejected request count | 0 |
| Reject fraction | 0% |
| Timeout request count | 0 |
| Timeout fraction | 0% |
| Admitted request count | 16 |
| Admitted fraction | 100% |
| Retried job count | 0 |
| Total retry attempts | 0 |

### Rate-Limited Requests

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

### Admitted Requests

| Metric | Value |
| --- | --- |
| Request count | 16 |
| Completed in window | 16 |
| Fraction of selected requests | 100% |
| Average latency (ms) | 78.356 |
| P95 latency (ms) | 82.299 |
| Throughput (req/s) | 2.118 |
| Average concurrency | 0.166 |
| Errors by status | `none` |

### Processed Job Metrics

| Metric | Value |
| --- | --- |
| Job count | 0 |
| Average queue delay (ms) | n/a |
| P95 queue delay (ms) | n/a |
| Average execution (ms) | n/a |
| P95 execution (ms) | n/a |
| Retry distribution | `none` |

## Interpretation

The request window carried `0.166` requests in flight on average, reconstructed exactly from observed lifetimes instead of thread counts.

The latency tail is relatively close to the median, so the selected window looks fairly tight.

Cache hits covered `0%` of the selected requests.

No selected requests were rejected, so any overload signal has to show up as slower admitted work rather than explicit shedding.

No live queue snapshot was available, so current backlog counts and oldest queued age could not be reported.

No job traces matched the selected filter, so waiting-versus-execution decomposition is unavailable even if backlog exists.

## Architectural Justification

Architectural discussion should stay attached to the measured dominant term rather than to a slogan or a preferred pattern.

The safe architectural conclusion is therefore the narrow one supported by the measured boundary, queue state, and freshness data, not a broader claim that this mechanism is universally better.
