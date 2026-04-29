# Milestone 1 Experiment: CPU vs I/O

Generated from the clean milestone-1 run on `2026-03-31`.

## Contract

This experiment compares two synchronous `Storefront.Api` contracts under the same external load shape:

- `GET /cpu?workFactor=20&iterations=1000`
- `GET /io?delayMs=90&jitterMs=0`

The purpose is not to prove that CPU and I/O always have the same latency. The purpose is to show that two workloads can deliver similar throughput while hiding different dominant stages inside the same top-level HTTP boundary.

## Observation Boundary

The measurement boundary is the `Storefront.Api` request lifetime:

- start: request arrival at `Storefront.Api`
- end: final HTTP response emitted by `Storefront.Api`

The analyzer metrics in this report come from the server-side request traces, not from `LoadGen` client timings. That matters because the server traces measure the contract boundary directly, while `LoadGen` also includes client-side overhead.

Average concurrency is reconstructed from full request lifetimes in the trace data, not from thread counts.

## Topology

- one `Storefront.Api` process
- no cache hits
- no rate limiting
- no worker activity in the selected runs
- one clean experiment workspace under `docs/experiments/milestone-1-cpu-vs-io/workspace`

The experiment was produced by:

```bash
./scripts/experiments/run-milestone-1-cpu-vs-io.sh
```

Generated artifacts:

- `docs/experiments/milestone-1-cpu-vs-io/artifacts/milestone-1-cpu-summary.json`
- `docs/experiments/milestone-1-cpu-vs-io/artifacts/milestone-1-io-summary.json`
- `docs/experiments/milestone-1-cpu-vs-io/artifacts/milestone-1-cpu-analysis.md`
- `docs/experiments/milestone-1-cpu-vs-io/artifacts/milestone-1-io-analysis.md`
- `docs/experiments/milestone-1-cpu-vs-io/artifacts/requests.jsonl`

## Workload

Both runs used the same external load shape:

- mode: constant
- target rate: `2` requests per second
- duration: `8` seconds
- concurrency cap: `1`
- planned request count per run: `16`

The only difference was the endpoint mechanism:

- CPU run: deterministic in-process work
- I/O run: low-CPU simulated downstream wait

## Results

Server-side analyzer summary:

| Metric | CPU run | I/O run |
| --- | --- | --- |
| Request count | 16 | 16 |
| Average latency (ms) | 78.643 | 91.314 |
| P50 latency (ms) | 78.251 | 91.271 |
| P95 latency (ms) | 83.967 | 94.170 |
| P99 latency (ms) | 83.967 | 94.170 |
| Throughput (req/s) | 2.119 | 2.114 |
| Average concurrency | 0.167 | 0.193 |
| Error count | 0 | 0 |

Representative trace evidence:

- CPU sample trace `milestone-1-cpu-000016` recorded `http_request = 78.2321 ms`. In that same request, `cpu_work_started` occurred at `12:12:17.878379Z` and `cpu_work_completed` at `12:12:17.956453Z`, a gap of about `78.074 ms`.
- I/O sample trace `milestone-1-io-000001` recorded `http_request = 94.1503 ms`, and the explicit `downstream_wait` stage alone consumed `91.6735 ms`.

Client-side `LoadGen` averages were slightly higher:

- CPU run: `81.27 ms`
- I/O run: `93.579 ms`

That gap is expected because the client also pays for its own timing and network overhead. The analyzer summaries above remain the source of truth for this report because they live inside the server observation boundary.

## Interpretation

The throughput numbers are almost the same: `2.119 req/s` for CPU and `2.114 req/s` for I/O. If we looked only at throughput, we could incorrectly conclude that the two workloads are operationally interchangeable.

They are not interchangeable.

The CPU run spends nearly its whole request lifetime inside in-process work. We can see that because the gap between `cpu_work_started` and `cpu_work_completed` is almost the same as the full `http_request` duration for a representative request.

The I/O run spends nearly its whole request lifetime inside explicit downstream waiting. We can see that because the timed `downstream_wait` stage alone accounts for about `91.7 ms` out of a `94.2 ms` request.

That is the lesson this report is meant to teach: similar throughput can hide different dominant stages. The top-level metrics look close because the external offered load is low enough that both endpoints can keep up. But the mechanisms are different, and those differences matter the moment we reason about scaling, saturation, or architectural intervention.

The concurrency numbers reinforce that point. Average concurrency is modest in both runs because the offered load is low, but the I/O run still carries slightly more in-flight work (`0.193` vs `0.167`) because requests stay in the system longer even though the server is not burning comparable CPU.

## Architectural Justification

This experiment justifies why the lab keeps stage timings explicit instead of stopping at averages.

If a system is CPU-dominated, the likely interventions are CPU-oriented:

- reduce work per request
- precompute
- simplify algorithms
- cache expensive computation results
- scale compute capacity

If a system is I/O-dominated, the likely interventions are boundary-oriented:

- reduce synchronous waiting
- batch or pipeline downstream calls
- move work behind asynchronous contracts
- introduce caches closer to the observation boundary
- remove avoidable network or storage hops

Both runs can deliver about `2.1 req/s` here, but they would not respond to the same optimization strategy. That is why the lab must preserve both the top-level metrics and the internal stage evidence.
