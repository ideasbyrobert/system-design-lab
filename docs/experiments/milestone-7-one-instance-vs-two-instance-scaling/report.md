# Milestone 7 Experiment: One Instance vs Two Instance Scaling

Generated from the clean milestone-7 run on `2026-04-01` UTC.

## Contract

This milestone compares two different scaling stories behind the same local reverse proxy:

1. a case where horizontal scale helps because the bottleneck lives in the frontend node itself
2. a case where horizontal scale does not help because the expensive work remains in one shared downstream path

To keep the comparison honest on one physical machine, each `Storefront.Api` process was started with:

- `DOTNET_PROCESSOR_COUNT=1`

That does not claim the machine truly had one core. It means each Storefront process was deliberately constrained to behave like a small frontend node so the proxy experiment measures node count instead of letting one large local process absorb the whole workload.

## Observation Boundary

Both scenarios were measured at the user-visible boundary reached through `Proxy`:

- start: request arrival at `Storefront.Api`
- end: final HTTP response emitted by `Storefront.Api`

The analyzer filters were:

- scenario A: `--operation cpu-bound-lab`
- scenario B: `--operation storefront-checkout-sync`

For the shared-checkout scenario, the report also inspected internal shared work with:

- `--operation checkout-sync`
- `--operation payment-authorize`

That matters because the milestone is not only about whether the proxy splits traffic. It is about whether splitting traffic actually changes the dominant work.

## Topology

Both scenarios used the same proxy entrypoint:

- `Proxy` on `http://127.0.0.1:5090`

Scenario A, frontend CPU bottleneck:

- one or two constrained `Storefront.Api` instances
- no downstream dependency on the measured path
- workload target: `GET /cpu?workFactor=90&iterations=1000`

Scenario B, shared downstream bottleneck:

- one or two constrained `Storefront.Api` instances
- one shared `Order.Api`
- one shared `PaymentSimulator.Api`
- one shared `Cart.Api`
- payment mode: `slow_success`
- simulated slow payment latency: `700 ms`
- workload target: `POST /checkout?mode=sync`

The experiment was produced by:

```bash
./scripts/experiments/run-milestone-7-one-instance-vs-two-instance-scaling.sh
```

Generated artifacts:

- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/frontend-cpu/one-instance/`
- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/frontend-cpu/two-instance/`
- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/shared-checkout/one-instance/`
- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/shared-checkout/two-instance/`
- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/comparison.json`

## Workload

### Scenario A: frontend CPU

Both arms used the same offered workload:

- `48` measured requests
- `8 req/s`
- `6 seconds`
- `8` client concurrency cap
- same CPU parameters on every request:
  - `workFactor = 90`
  - `iterations = 1000`

### Scenario B: shared checkout

Both arms used the same offered workload:

- `12` measured checkouts
- `12` distinct carts
- `12` distinct users
- `12` distinct products
- `12` client concurrency cap
- `15 ms` spacing between request launches
- `paymentMode = slow_success`
- one shared `Order.Api`
- one shared `PaymentSimulator.Api`

The point of scenario B was not realism. The point was to make the downstream critical path dominant enough that adding another frontend would not materially change the result.

## Results

### Scenario A: horizontal scale helps when the frontend node is the bottleneck

| Arm | Avg latency (ms) | P50 (ms) | P95 (ms) | Throughput (req/s) | Avg concurrency | Proxy backend distribution |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `1 x Storefront` | `354.631` | `355.261` | `360.859` | `5.898` | `2.092` | `49 -> 5088` |
| `2 x Storefront` | `355.445` | `355.520` | `358.627` | `7.613` | `2.706` | `25 -> 5088`, `24 -> 5089` |

Measured change:

- throughput increased by `29.076%`
- average latency changed by only `0.230%`
- p95 latency improved by `0.619%`

Interpretation:

- the proxy really did balance the measured requests almost evenly across the two frontend nodes
- the top-level latency stayed essentially flat
- the main gain showed up as higher admitted throughput

So in this scenario horizontal scale helped because the bottleneck lived in the per-node CPU work at the frontend boundary itself.

### Scenario B: horizontal scale does not help when the bottleneck remains shared downstream

Top-level Storefront boundary:

| Arm | Avg latency (ms) | P50 (ms) | P95 (ms) | Throughput (req/s) | Avg concurrency | Pending projection jobs after run | Proxy backend distribution |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `1 x Storefront` | `792.551` | `709.770` | `1186.394` | `10.090` | `7.997` | `12` | `13 -> 5088` |
| `2 x Storefront` | `811.645` | `716.068` | `1211.606` | `9.885` | `8.023` | `12` | `7 -> 5088`, `6 -> 5089` |

Shared internal work stayed almost unchanged too.

Order boundary:

| Arm | Avg latency (ms) | P95 (ms) | Throughput (req/s) |
| --- | ---: | ---: | ---: |
| `1 x Storefront` | `790.892` | `1184.554` | `10.111` |
| `2 x Storefront` | `801.159` | `1168.547` | `9.907` |

Payment boundary:

| Arm | Avg latency (ms) | P50 (ms) | P95 (ms) | Throughput (req/s) |
| --- | ---: | ---: | ---: | ---: |
| `1 x Storefront` | `701.006` | `700.823` | `702.313` | `10.419` |
| `2 x Storefront` | `701.050` | `700.924` | `702.012` | `10.200` |

Measured top-level change:

- Storefront throughput changed by `-2.032%`
- Storefront average latency changed by `+2.409%`
- payment average latency changed by only `+0.006%`
- payment throughput changed by `-2.105%`

Interpretation:

- the proxy again did its job and distributed the measured requests across both frontend instances
- but the slow shared checkout path did not get meaningfully faster
- the payment stage stayed at roughly `701 ms` in both arms
- the immediate follow-up queue footprint stayed the same at `12` pending projection jobs

So in this scenario adding another frontend did not remove the dominant cost, because the dominant cost remained inside the same shared downstream checkout stack.

## Interpretation

This milestone was supposed to justify load balancing and show where the bottleneck moved. The two scenarios together do that cleanly.

In scenario A:

- the expensive work was frontend-local CPU work
- one constrained frontend node was the limiting resource
- adding a second constrained frontend raised throughput while keeping latency roughly unchanged

So the bottleneck moved outward from one saturated frontend node to the combined frontend pool.

In scenario B:

- the expensive work was still the shared synchronous checkout path
- `Order.Api` and `PaymentSimulator.Api` stayed singleton dependencies
- the payment stage remained about `701 ms` regardless of whether there were one or two frontends

So the bottleneck did not move. The proxy distributed requests, but the dominant work stayed downstream.

## Architectural Justification

This experiment supports two very specific claims and rejects a looser one.

What it supports:

- horizontal scale helps when the path is bottlenecked by per-node frontend capacity
- horizontal scale does not help much when the expensive work stays in one shared downstream dependency chain

What it rejects:

- “add more app instances and the system scales” as a generic claim

That generic claim is false in this lab.

The correct reading is narrower:

- if the bottleneck is local to the node you are duplicating, another node can help
- if the bottleneck is shared and unchanged, another frontend mostly adds another place to wait before doing the same expensive downstream work

That is the real lesson of milestone 7.
