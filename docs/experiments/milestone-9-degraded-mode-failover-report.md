# Milestone 9 Experiment: Degraded Mode and Regional Failover

Generated from the clean milestone-9 degraded-mode run on `2026-04-01` UTC.

## Contract

This experiment asks a narrower question than the previous same-region vs cross-region run:

- what happens when the west-side read path degrades?
- does the request boundary fail outright?
- if it stays available, where does the extra latency move?

The measured user-visible contract stayed the same in all three arms:

- `GET /products/{id}?cache=on&readSource=local`
- Storefront `product-page` boundary

What changed was only the simulated failure mode behind that boundary.

## Observation Boundary

The measured top-level boundary is still the Storefront product-page request:

- start: request arrival at `Storefront.Api`
- end: final HTTP response from `Storefront.Api`

The experiment was produced by:

```bash
./scripts/experiments/run-milestone-9-degraded-mode-failover.sh
```

Generated artifacts live in:

- `docs/experiments/milestone-9-degraded-mode-failover/artifacts/healthy-west-local/`
- `docs/experiments/milestone-9-degraded-mode-failover/artifacts/local-replica-unavailable/`
- `docs/experiments/milestone-9-degraded-mode-failover/artifacts/local-catalog-unavailable/`
- `docs/experiments/milestone-9-degraded-mode-failover/artifacts/comparison.json`

## Topology

Three west-side scenarios were measured.

### 1. Healthy west local

- `Storefront.Api` in `us-west`
- `Catalog.Api` in `us-west`
- local product reads resolve to `replica-west`
- no degradation flags enabled

### 2. Local replica unavailable

- `Storefront.Api` in `us-west`
- `Catalog.Api` in `us-west`
- `SimulateLocalReplicaUnavailable = true`
- local product reads fall back from `replica-west` to `primary`
- the Storefront-to-Catalog hop remains same-region
- the Catalog read itself becomes a cross-region fallback

### 3. Local catalog unavailable

- `Storefront.Api` in `us-west`
- west Catalog endpoint declared unavailable
- `SimulateLocalCatalogUnavailable = true`
- Storefront reroutes the Catalog dependency to east failover Catalog
- local product reads still resolve successfully, but now through the east stack

That last arm is the key availability claim of the ticket:

- west local Catalog unavailability did not force total request failure
- the path remained available through east
- the cost showed up as extra latency

## Workload

The run used the same small mixed product workload in every arm:

- seeded products: `16`
- measured products: `8`
- request rate: `1 req/s` per product
- duration: `6 s`
- total measured requests per arm: `48`
- Storefront cache mode: `on`
- requested read source: `local`

That gives one first miss per product and then a warmed path:

- miss request count: `8`
- hit request count: `40`
- cache hit rate: `83.333%`

Holding cache hit rate constant matters here. It keeps the degraded-mode penalty visible instead of letting one arm “win” because it happened to warm differently.

## Results

### Top-level Storefront metrics

| Arm | Effective source | Avg latency (ms) | P95 (ms) | Throughput (req/s) | Cache hit rate |
| --- | --- | ---: | ---: | ---: | ---: |
| healthy-west-local | `replica-west` | `8.665` | `48.559` | `8.937` | `83.333%` |
| local-replica-unavailable | `primary` | `11.400` | `66.486` | `8.965` | `83.333%` |
| local-catalog-unavailable | `primary` | `10.192` | `58.273` | `8.931` | `83.333%` |

The first important fact is simple:

- both degraded arms still completed all `48` measured requests successfully

So the west-side dependency problem was not automatically a total outage.

### Storefront miss-path dependency metrics

| Arm | Storefront -> Catalog scope | Avg dependency elapsed on misses (ms) | P95 dependency elapsed on misses (ms) |
| --- | --- | ---: | ---: |
| healthy-west-local | `same-region` | `48.663` | `61.494` |
| local-replica-unavailable | `same-region` | `65.374` | `92.448` |
| local-catalog-unavailable | `cross-region` | `58.420` | `76.829` |

This is where the two degraded scenarios start to separate.

- local-replica-unavailable stayed same-region at the Storefront dependency boundary
- local-catalog-unavailable became cross-region at the Storefront dependency boundary

### Catalog db-query metrics

| Arm | Catalog read scope | Avg db query elapsed (ms) | P95 db query elapsed (ms) | Avg injected read delay (ms) |
| --- | --- | ---: | ---: | ---: |
| healthy-west-local | `same-region` | `38.973` | `50.069` | `0.000` |
| local-replica-unavailable | `cross-region` | `57.364` | `77.470` | `35.000` |
| local-catalog-unavailable | `same-region` | `17.986` | `32.971` | `0.000` |

This is the mechanical heart of the experiment.

For `local-replica-unavailable`:

- the Storefront dependency stayed west-to-west
- but Catalog had to fall back across regions to the east primary
- the db query stage itself became the degraded cross-region step

For `local-catalog-unavailable`:

- Storefront paid the cross-region hop to east Catalog
- but east Catalog then read its local primary in-region
- so the db query itself stayed same-region and actually became cheaper than the healthy west-local case

That is exactly why degraded mode needs tracing. Without the stage split, we would see “slower” and miss where the slowness moved.

## Key Comparisons

### Local replica unavailable vs healthy west local

Measured change:

- average latency increased by `2.735 ms` (`31.560%`)
- p95 increased by `17.928 ms`
- average dependency elapsed on misses increased by `16.712 ms`
- average Catalog db-query elapsed increased by `18.391 ms`

Mechanism notes:

- availability stayed intact
- the extra cost came mostly from the Catalog read path itself
- the trace shows this honestly as `fallbackReason = local_replica_unavailable` and `readNetworkScope = cross-region`

### Local catalog unavailable vs healthy west local

Measured change:

- average latency increased by `1.526 ms` (`17.613%`)
- p95 increased by `9.715 ms`
- average dependency elapsed on misses increased by `9.757 ms`
- average Catalog db-query elapsed actually decreased by `20.988 ms`

Mechanism notes:

- the request still stayed available because Storefront rerouted to east Catalog
- the extra cost moved into the Storefront dependency hop
- once the request arrived in east, Catalog read primary locally and cheaply

That is a very useful non-obvious result. “Failover is slower” is true here, but the path is not slower for the same reason as replica unavailability.

## Interpretation

This experiment justifies three careful claims.

### 1. A local-region dependency problem does not necessarily mean total failure

The strongest acceptance point is the simplest:

- both degraded arms still served the full measured request set

In particular, `local-catalog-unavailable` proves the main availability claim. Storefront treated the west Catalog as unavailable, rerouted to east, and still served the product page.

### 2. Degraded mode is slower, but the slowdown can move

The degraded path became slower in both cases, but the dominant extra cost did not land in the same place:

- replica unavailable: slower Catalog db query
- catalog unavailable: slower Storefront dependency hop

That is why the report should not collapse everything into one vague “regional failover penalty” phrase.

### 3. Honest degraded-mode reasoning requires mechanism, not slogans

If we had only looked at top-level average latency:

- healthy west local: `8.665 ms`
- replica unavailable: `11.400 ms`
- catalog unavailable: `10.192 ms`

we would know degraded mode is slower, but not why.

The trace makes the mechanism discussable:

- `fallbackReason = local_replica_unavailable`
- `degradedModeReason = local_catalog_unavailable`
- same-region vs cross-region counts
- db-query timing versus dependency timing

That is the real teaching objective of LAB-093.

## Architectural Justification

This milestone justifies adding degraded mode to the lab for three reasons:

1. It proves that regional outages can be modeled as slower fallback paths instead of all-or-nothing death.

2. It keeps the tradeoff explicit.
   The request survives, but the latency budget gets worse in measurable ways.

3. It prevents fake architectural confidence.
   The report does not claim full production failover. It claims something narrower and true: this lab can now explain how local replica loss and local service loss become different degraded-but-available paths.

That is the honest result we wanted.
