# Milestone 8 Experiment: Primary vs Replica and Read Model

Generated from the clean milestone-8 run on `2026-04-01` UTC.

## Contract

Milestone 8 is supposed to justify two related but different read-side mechanisms:

1. serving read-heavy traffic from a replica instead of the primary write store
2. serving a user-facing history view from a denormalized read model instead of rebuilding it from write tables on every request

The point is not to claim that either mechanism is always faster. The point is to make two tradeoffs measurable:

- how much primary or write-table read pressure gets removed
- what freshness cost appears when the read path is allowed to lag

That is why this report treats stale reads as a first-class result, not as an embarrassing footnote.

## Observation Boundary

Two different user-visible read boundaries were measured.

Product reads:

- request: `GET /products/sku-0001?cache=off&readSource=...`
- top-level measured operation: `product-page`
- internal source inspection: `catalog-product-detail`

Order-history reads:

- request: `GET /orders/user-0001?readSource=...`
- top-level measured operation: `order-history`

The experiment was produced by:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-8-primary-vs-replica-read-model.sh
```

Generated artifacts:

- `docs/experiments/milestone-8-primary-vs-replica-read-model/artifacts/product-reads/`
- `docs/experiments/milestone-8-primary-vs-replica-read-model/artifacts/order-history/`
- `docs/experiments/milestone-8-primary-vs-replica-read-model/artifacts/comparison.json`

## Topology

### Scenario A: product reads

- `Storefront.Api`
- `Catalog.Api`
- `primary.db`
- `replica-east.db`
- `replica-west.db`
- Storefront cache disabled
- Catalog cache disabled

The product scenario intentionally removed cache help so the measured change came from read source selection, not from hot-memory reuse.

### Scenario B: order history

- `Storefront.Api`
- `Order.Api`
- `Cart.Api`
- `PaymentSimulator.Api`
- `Worker`
- `primary.db`
- `readmodels.db`

The order-history scenario first created a baseline history, then stopped `Worker`, performed one more checkout, and measured reads during the resulting stale window.

That matters because the read model in this lab is not “another DB” in the abstract. It is a denormalized projection maintained by background jobs:

1. `Order.Api` writes authoritative order state to `primary.db`
2. it enqueues `order-history-projection-update`
3. `Worker` applies that job into `ReadModel_OrderHistory`
4. `Storefront.Api` can later read the projection through `readmodels.db`

So the read model is a materialized summary built from write truth. Its value is separation of read shape from write shape, not mystical speed.

## Workload

### Scenario A: product reads

Both arms used the same product workload:

- target: `sku-0001`
- measured requests: `1320`
- same offered rate in both arms
- same Storefront endpoint
- only `readSource` changed:
  - `primary`
  - `replica-east`

After the baseline primary run, the experiment intentionally advanced primary again before the replica run so the replica arm would read from a genuine stale window instead of an identical copy.

### Scenario B: order history

The order-history scenario had two phases:

1. baseline write phase
   - `24` successful synchronous checkouts for `user-0001`
   - projection queue drained before measurement
2. stale-window phase
   - `Worker` stopped
   - one more successful checkout executed
   - exactly `1` pending `order-history-projection-update` job left unprocessed during the read measurement window

Both measured read arms then queried the same user history:

- `readSource=primary-projection`
- `readSource=read-model`

## Results

### Scenario A: replica reads removed all measured primary product reads, but paid a freshness cost

Top-level Storefront boundary:

| Arm | Avg latency (ms) | P95 (ms) | Throughput (req/s) |
| --- | ---: | ---: | ---: |
| `primary` | `27.840` | `6.619` | `184.826` |
| `replica-east` | `37.535` | `11.995` | `195.939` |

Catalog-source freshness and load:

| Arm | Source requests | Primary-backed reads | Replica-backed reads | Stale request fraction | Stale result fraction | Max observed stale age (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `primary` | `1320` | `1320` | `0` | `0.000` | `0.000` | `0.000` |
| `replica-east` | `1320` | `0` | `1320` | `1.000` | `1.000` | `17436.179` |

Measured change:

- throughput increased by `6.013%`
- average latency increased by `34.823%`
- p95 latency increased by `81.220%`
- primary read load for measured catalog requests fell by `100.000%`

Interpretation:

- the replica arm successfully moved all measured catalog reads off the primary
- the replica arm did not become a universal latency win on this one-machine simulation
- every measured replica request during the stale window was stale

That is the honest result. The benefit here is read scaling and primary protection, not a blanket promise that replicas are always faster.

The sample response makes the stale window concrete:

- the replica sample still returned `version = 30` and `sellableQuantity = 100`
- the same response explicitly marked `staleRead = true`
- the freshness metadata reported a newer primary state and about `17436 ms` of observed lag

So the mechanism is working exactly as intended: stale reads are visible, not hidden.

### Scenario B: the read model removed write-table reads, but lagged behind authoritative order state

Top-level Storefront boundary:

| Arm | Avg latency (ms) | P95 (ms) | Throughput (req/s) | Stale request fraction | Stale result fraction | Sample order count |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `primary-projection` | `16.249` | `4.928` | `180.727` | `0.000` | `0.000` | `25` |
| `read-model` | `28.902` | `7.879` | `171.976` | `1.000` | `0.040` | `24` |

Measured change:

- throughput changed by `-4.842%`
- average latency increased by `77.873%`
- p95 latency increased by `59.901%`
- measured reads against the write-table projection path fell by `100.000%`
- pending projection jobs during the stale window: `1`
- max observed stale age in the read-model arm: `23238.033 ms`

Interpretation:

- the read model removed all measured reads from the primary-projection path
- the read model did not become faster in this local implementation
- every measured read-model request happened during a stale window
- but the stale result fraction was only `0.040`, because only `1` of `25` logical order results was actually missing from the denormalized view

That last point is important. “All requests were stale” and “every result was catastrophically wrong” are not the same claim.

In this run:

- the authoritative path showed `25` orders
- the stale read-model path showed `24`
- the missing order corresponded exactly to the one checkout whose projection update was still waiting in the queue

So the read model behaved like a lagged projection should behave. It was mostly current, but not fully current, because one queued projection job had not yet been applied.

## Interpretation

The two scenarios make the same deeper point through different mechanisms.

In scenario A, replica reads removed all measured primary-backed catalog reads, but the selected window still paid a visible freshness cost. In scenario B, the read model removed all measured write-table reads from the user-facing order-history path, but the denormalized projection lagged behind the authoritative order state until background work caught up.

The common lesson is that scaling reads by changing the read path is a real trade, not a free win. The lab now makes that trade discussable with measured primary-offload, stale fractions, and max observed lag instead of relying on slogans about replicas or read models.

## Architectural Justification

This milestone supports three precise claims.

### 1. Replica reads justify themselves by offloading primary read pressure

The product scenario showed a clean `100%` removal of measured primary-backed catalog reads in the replica arm.

That is the core scaling argument:

- when the system is read-heavy
- and the freshness contract allows lag
- moving reads away from the write primary protects the primary for write work and authoritative reads

The experiment does **not** support the looser claim that replica reads are automatically lower latency.

### 2. A read model is a denormalized projection, not just another database copy

The order-history path is not reading “the same data from a different file.”

It is reading a purpose-built summary shape maintained by background projection work. That is why it can support a user-friendly order-history boundary without rebuilding that shape from normalized write tables on every request.

Its justification is:

- decoupled read shape
- reduced write-table read pressure
- measurable projection lag as an explicit tradeoff

### 3. Freshness cost must remain visible

Both arms made the stale window measurable:

- replica product reads reported `staleRead`, stale fractions, and max observed lag
- read-model order-history reads reported stale fractions, max observed lag, and the missing projected order count

That is the real success condition of milestone 8.

The lab now has enough mechanism to discuss read scaling honestly:

- replicas can reduce primary load
- read models can decouple read shape from write shape
- neither mechanism should be sold as free speed
- both mechanisms introduce freshness windows that must be named, measured, and justified by the contract
