# Milestone 5 Experiment: Sync vs Async Checkout

Generated from the clean milestone-5 run on `2026-04-01` UTC.

## Contract

This experiment compares two user-visible checkout contracts exposed by `Storefront.Api`:

- `POST /checkout?mode=sync`
- `POST /checkout?mode=async`

Required contract inputs:

- `Idempotency-Key`
- `userId`
- `paymentMode`

For this run, both modes used the same slow payment workload:

- `paymentMode = slow_success`

The two contracts are not interchangeable.

Synchronous checkout promises:

- the response boundary does not close until checkout resolves on the critical path
- the caller receives a resolved business result at the boundary
- in this run that result was `Paid` with `paymentStatus = Authorized`

Asynchronous checkout promises something different:

- the response boundary closes once the order is durably accepted and background work is enqueued
- the caller receives `PendingPayment` with a `backgroundJobId`
- payment confirmation is intentionally delegated to `Worker`

That means `contractSatisfied=true` means different things in the two modes:

- in `sync`, it means payment resolution happened before the boundary closed
- in `async`, it means the order was accepted and the remaining work was durably handed off before the boundary closed

## Observation Boundary

The measured top-level boundary for both modes is now the real user-visible boundary:

- start: request arrival at `Storefront.Api`
- end: final HTTP response emitted by `Storefront.Api`

The analyzer was filtered with:

- `--operation storefront-checkout-sync`
- `--operation storefront-checkout-async`

This matters because the teaching repo still emits traces from multiple internal services. The report is intentionally about what the caller pays at the top-level boundary, not about every internal hop pretending to be a user-visible contract.

Queue state was captured twice for each run:

- immediately after the user-visible workload completed
- again after `Worker` drained the run-specific backlog

So this report does not stop at request latency. It also measures the cost that moved off the critical path.

## Topology

- one clean sync workspace and one clean async workspace for isolation
- one `Storefront.Api`
- one `Order.Api`
- one `PaymentSimulator.Api`
- one `Cart.Api`
- one `Worker`, started only for the drain phase
- one clean SQLite `primary.db` per workspace
- one warm-up checkout per mode before the measured run

The experiment was produced by:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-5-sync-vs-async-checkout.sh
```

Generated artifacts:

- `docs/experiments/milestone-5-sync-vs-async-checkout/artifacts/sync/`
- `docs/experiments/milestone-5-sync-vs-async-checkout/artifacts/async/`
- `docs/experiments/milestone-5-sync-vs-async-checkout/workspace/sync/logs/requests.jsonl`
- `docs/experiments/milestone-5-sync-vs-async-checkout/workspace/async/logs/requests.jsonl`
- `docs/experiments/milestone-5-sync-vs-async-checkout/workspace/sync/logs/jobs.jsonl`
- `docs/experiments/milestone-5-sync-vs-async-checkout/workspace/async/logs/jobs.jsonl`

## Workload

This run used the same offered workload in both modes:

- `6` measured checkout requests
- `6` distinct carts
- `1` line item per cart
- `paymentMode = slow_success`
- `X-Debug-Telemetry: true`
- unique idempotency keys and correlation IDs per request

The important fairness rule is that the payment mode stayed the same while only the checkout contract changed.

## Results

### User-visible boundary

| Mode | HTTP response | Business state at boundary | Avg latency (ms) | P50 (ms) | P95 (ms) | Throughput (req/s) | Avg concurrency |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| `sync` | `200` | `Paid`, `Authorized` | `764.105` | `687.714` | `1147.829` | `5.227` | `3.994` |
| `async` | `202` | `PendingPayment`, `Pending` | `410.921` | `335.458` | `788.364` | `7.608` | `3.126` |

Immediate top-level outcome summaries:

- `sync`: `6/6` requests returned `200`, `Paid`, `Authorized`, `backgroundJobId = null`
- `async`: `6/6` requests returned `202`, `PendingPayment`, `Pending`, and a non-empty `backgroundJobId`

Measured deltas at the user-visible boundary:

- async reduced average latency by `353.185 ms`
- that is a `46.222%` latency reduction relative to sync
- async increased throughput by about `45.554%`

### Immediate queue growth

Immediately after the user-visible workload finished:

| Mode | Pending jobs | Immediate job mix | Oldest queued age (ms) |
| --- | ---: | --- | ---: |
| `sync` | `6` | `6 x order-history-projection-update` | `954.639` |
| `async` | `12` | `6 x payment-confirmation-retry`, `6 x order-history-projection-update` | `1008.643` |

This is the key milestone-5 result. Sync already leaves projection work behind, but async adds payment confirmation to that background backlog. So async does not make work disappear. It moves payment work out of the user-visible path and into the queue.

### Background drain after Worker starts

After `Worker` drained each run-specific backlog:

| Mode | Completed jobs | Failed jobs | Avg queue delay (ms) | P95 queue delay (ms) | Avg execution (ms) | P95 execution (ms) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `sync` | `6` | `0` | `3488.036` | `3819.057` | `13.598` | `21.710` |
| `async` | `18` | `0` | `3693.337` | `5904.130` | `128.164` | `358.034` |

Final queue composition after drain:

- `sync`: `6 x order-history-projection-update` completed
- `async`: `6 x payment-confirmation-retry` completed and `12 x order-history-projection-update` completed

The async count reaches `18` because each checkout creates:

- one initial order-history projection job when the order first enters `PendingPayment`
- one payment-confirmation job
- one additional order-history projection job after payment confirmation marks the order `Paid`

### Representative payloads

Representative sync response:

- `status = Paid`
- `paymentStatus = Authorized`
- `checkoutMode = sync`
- `backgroundJobId = null`

Representative async response:

- `status = PendingPayment`
- `paymentStatus = Pending`
- `checkoutMode = async`
- `backgroundJobId = job-...`

Representative top-level `Storefront.Api` request trace notes were also explicit:

- sync: `Storefront waited for Order.Api to complete synchronous checkout before closing the user-visible boundary.`
- async: `Storefront accepted checkout at the user-visible boundary while payment confirmation moved to Worker.`

## Interpretation

The main conclusion is clear:

- async checkout lowered the user-visible wait
- but it did so by changing the contract and relocating work into the background queue

This is not a slogan-level difference. It is directly visible in the measurements.

At the top-level boundary:

- sync averaged `764.105 ms`
- async averaged `410.921 ms`

So the caller waited substantially less in async mode.

But async did not eliminate payment cost. Instead, it created a new pending state:

- the caller sees `202 Accepted`
- the order remains `PendingPayment`
- `Worker` still has to authorize payment later
- read models still need projection updates after each state transition

That is why the queue snapshot matters so much.

Immediately after the requests finished:

- sync had `6` pending jobs
- async had `12` pending jobs

So async doubled the immediate backlog for this run. The extra backlog is exactly the moved payment work.

There is another important nuance here: sync is not background-free.

Even in sync mode, the system still defers order-history projection updates. That means the true distinction is not:

- sync = no background work
- async = background work

The real distinction is:

- sync keeps payment confirmation on the user-visible path and only defers read-model update work
- async defers both read-model update work and payment confirmation

That is why the async drained job count reached `18` instead of `12`. Once payment completed in the worker, the system generated another wave of projection work to reflect the paid state.

The execution metrics reinforce that interpretation:

- sync background work averaged only `13.598 ms` because it was projection-only
- async background work averaged `128.164 ms` because it included real payment authorization work around `352-358 ms` per payment job

So the saved latency was not destroyed. It was transferred:

- from the user-visible `Storefront.Api` response path
- into queued waiting time plus worker execution time

That is exactly the kind of boundary shift this milestone was supposed to justify with evidence.

## Architectural Justification

This experiment justifies asynchronous checkout only under an honest interpretation:

- async is valuable when user-visible latency matters more than immediate payment finality
- async requires explicit pending state and explicit queue instrumentation
- async adds operational cost, because now queue growth, oldest item age, worker capacity, and projection lag matter

It also clarifies what we must never say loosely:

- async did not make payment fast
- async did not reduce total work
- async did not remove the need for correctness, idempotency, or retry discipline

What async actually did was narrower and more precise:

- it changed the contract
- it shortened the top-level boundary
- it transferred payment completion to `Worker`
- it made backlog and pending state first-class concerns

In short: milestone 5 proves that async checkout can reduce user-visible wait, but only by making the background system carry the work that sync forced the caller to wait for. The latency improvement is real, and so is the queue cost.
