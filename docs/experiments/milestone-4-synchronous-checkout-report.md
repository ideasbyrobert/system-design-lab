# Milestone 4 Experiment: Synchronous Checkout

Generated from the clean milestone-4 run on `2026-04-01` UTC.

## Contract

This experiment measures the current milestone-local synchronous checkout contract:

- `POST /orders/checkout`

Required contract inputs:

- `Idempotency-Key`
- `userId`
- `paymentMode`

What the endpoint promises in the current implementation is not "the order will succeed." What it promises is stricter and more precise:

- the synchronous checkout path will resolve before the response boundary closes
- the response will tell us whether the order became `Paid`, `Failed`, or remained `PendingPayment`
- `contractSatisfied=true` means the synchronous contract reached a resolved state before the boundary closed

That distinction matters here. In the current code, both `Paid` and `Failed` satisfy the synchronous contract. Only `PendingPayment` would mean the synchronous contract was not fully met.

## Observation Boundary

For this milestone, the measured top-level boundary is:

- start: request arrival at `Order.Api`
- end: final HTTP response emitted by `Order.Api`

This is intentionally narrower than the final architecture in `project.md`, which will later move the top-level checkout boundary up to `Storefront.Api`. For this milestone, `Order.Api` is the honest boundary because that is where checkout currently starts and ends.

The analyzer is filtered with:

- `--operation checkout-sync` for the top-level contract
- `--operation payment-authorize` for the downstream payment-provider boundary

That filter is important because `requests.jsonl` still contains direct traces from multiple services during this teaching phase.

## Topology

- one `Cart.Api` process for cart setup only
- one `PaymentSimulator.Api` process acting as the external payment-provider boundary
- one `Order.Api` process owning synchronous checkout
- one clean SQLite `primary.db`
- one clean experiment workspace under `docs/experiments/milestone-4-synchronous-checkout/workspace`
- one warm-up checkout before the measured runs to avoid cold-start distortion

The experiment was produced by:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-4-synchronous-checkout.sh
```

Generated artifacts:

- `docs/experiments/milestone-4-synchronous-checkout/artifacts/milestone-4-sync-fast-success-checkout-summary.json`
- `docs/experiments/milestone-4-synchronous-checkout/artifacts/milestone-4-sync-slow-success-checkout-summary.json`
- `docs/experiments/milestone-4-synchronous-checkout/artifacts/milestone-4-sync-timeout-checkout-summary.json`
- `docs/experiments/milestone-4-synchronous-checkout/artifacts/milestone-4-sync-transient-failure-checkout-summary.json`
- `docs/experiments/milestone-4-synchronous-checkout/artifacts/milestone-4-sync-fast-success-response.json`
- `docs/experiments/milestone-4-synchronous-checkout/artifacts/milestone-4-sync-slow-success-response.json`
- `docs/experiments/milestone-4-synchronous-checkout/artifacts/milestone-4-sync-timeout-response.json`
- `docs/experiments/milestone-4-synchronous-checkout/artifacts/milestone-4-sync-transient-failure-response.json`
- `docs/experiments/milestone-4-synchronous-checkout/artifacts/requests.jsonl`

## Workload

This is not a throughput stress test. Checkout is stateful and destructive, so the honest workload is one carefully prepared checkout per payment mode.

Measured runs:

- `fast_success`
- `slow_success`
- `timeout`
- `transient_failure`

Setup for each measured run:

- seed `5` products and `5` users
- create one active cart with one line item
- execute one checkout with a unique idempotency key
- request `X-Debug-Telemetry: true`
- analyze the resulting server-side traces

Because each run contains exactly one checkout request, the average, `P50`, `P95`, and `P99` are identical inside each per-run analyzer summary. That is expected and not a mistake.

## Results

Top-level checkout boundary:

| Payment mode | Order result | Payment result | Contract satisfied | Checkout latency (ms) | Downstream payment latency (ms) | Dominant checkout stage |
| --- | --- | --- | --- | ---: | ---: | --- |
| `fast_success` | `Paid` | `Authorized` | `true` | `63.569` | `26.776` | `payment_request_completed = 29.608 ms` |
| `slow_success` | `Paid` | `Authorized` | `true` | `524.309` | `500.802` | `payment_request_completed = 502.710 ms` |
| `timeout` | `Failed` | `Timeout` | `true` | `3010.633` | `3003.361` | `payment_request_completed = 3005.006 ms` |
| `transient_failure` | `Failed` | `Failed` | `true` | `31.815` | `24.841` | `payment_request_completed = 26.188 ms` |

Representative stage decomposition from the saved debug-telemetry responses:

- `fast_success`
  `idempotency_checked = 24.510 ms`, `cart_loaded = 0.939 ms`, `inventory_reserved = 2.609 ms`, `payment_request_completed = 29.608 ms`, `order_persisted = 1.755 ms`
- `slow_success`
  `idempotency_checked = 8.164 ms`, `cart_loaded = 0.779 ms`, `inventory_reserved = 7.777 ms`, `payment_request_completed = 502.710 ms`, `order_persisted = 2.114 ms`
- `timeout`
  `idempotency_checked = 1.014 ms`, `cart_loaded = 0.573 ms`, `inventory_reserved = 1.816 ms`, `payment_request_completed = 3005.006 ms`, `order_persisted = 1.512 ms`
- `transient_failure`
  `idempotency_checked = 0.848 ms`, `cart_loaded = 0.398 ms`, `inventory_reserved = 1.661 ms`, `payment_request_completed = 26.188 ms`, `order_persisted = 1.558 ms`

Downstream payment-provider HTTP status codes were explicit:

- `fast_success` -> `200`
- `slow_success` -> `200`
- `timeout` -> `504`
- `transient_failure` -> `503`

Top-level `Order.Api` behavior stayed explicit too:

- every measured checkout returned HTTP `200`
- the business outcome moved into the payload as `status`, `paymentStatus`, and `paymentErrorCode`
- the timeout run returned `status = Failed`, `paymentStatus = Timeout`, `paymentErrorCode = simulated_timeout`
- the transient-failure run returned `status = Failed`, `paymentStatus = Failed`, `paymentErrorCode = simulated_transient_failure`

## Interpretation

The main result is clear: in the current synchronous checkout design, payment latency sits directly on the critical path.

The evidence is strongest in the slow and timeout runs:

- `slow_success` spent about `502.710 ms` in `payment_request_completed` out of `524.309 ms` total checkout latency
- `timeout` spent about `3005.006 ms` in `payment_request_completed` out of `3010.633 ms` total checkout latency

That is near-total dominance. Once the payment provider gets slow, the user-visible checkout gets slow by almost the same amount because there is no asynchronous handoff yet.

The fast and transient-failure runs are also instructive:

- in `fast_success`, payment was still the single largest stage, but local work like idempotency checking remained visible
- in `transient_failure`, the provider failed quickly, so the whole checkout failed quickly too

So the system is not merely "slow when payment is slow." It is more exact than that:

- synchronous checkout exposes downstream waiting directly at the response boundary
- fast failure is still user-visible, but only for the short time needed to reach that failure
- timeout is user-visible for almost the full provider timeout interval

One subtle but important lesson is about `contractSatisfied`.

In this experiment:

- `fast_success` and `slow_success` both have `contractSatisfied = true`
- `timeout` and `transient_failure` also have `contractSatisfied = true`

That is not a bug. It tells us that the current synchronous contract means "resolved before response," not "business success." The system reached a final answer before the boundary closed, even when that final answer was failure.

Another important teaching point is what does **not** exist yet.

There is no worker, queue, or post-response continuation in this sync flow. That means there is no hidden background checkout work to subtract away from the observed latency. The wait we see at the boundary is the real wait the caller pays. In the timeout run, the user-visible wait is long because the synchronous architecture chooses to wait for that provider outcome before responding.

## Architectural Justification

This experiment justifies three architectural instincts for the next milestones:

- if a downstream dependency dominates the synchronous path, we should not pretend averages alone explain the system; stage timing must stay first-class
- if we want checkout latency to stop inheriting provider timeout directly, we need an explicit asynchronous boundary rather than hand-waving about "eventual" work
- idempotency matters even before async work appears, because retries against slow or failed payment paths must not create duplicate charges or duplicate inventory reservation

It also clarifies why the future async queue work matters so much. In the current sync design, `Order.Api` cannot hide provider delay because the contract requires it to resolve before responding. Later, when the architecture introduces a queue and pending state, the observation boundary will change. At that point, some payment-related work can move out of the immediate response path, and the report for that milestone should explicitly show the changed boundary.

In short: this experiment proves that the current synchronous checkout path is dependency-bound. When payment is slow, checkout is slow. When payment times out, checkout waits almost the full timeout. And because no background work exists yet, that cost is fully user-visible at the measured boundary.
