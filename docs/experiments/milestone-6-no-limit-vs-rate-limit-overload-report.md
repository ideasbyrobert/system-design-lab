# Milestone 6 Experiment: No-Limit vs Rate-Limit Overload

Generated from the clean milestone-6 run on `2026-04-01` UTC.

## Contract

This experiment compares the same user-visible contract under two admission policies:

- `POST /checkout?mode=sync` with the checkout limiter disabled
- `POST /checkout?mode=sync` with the checkout limiter enabled

The business contract did not change:

- synchronous checkout
- same `Storefront.Api` boundary
- same `Order.Api`
- same `PaymentSimulator.Api`
- same `Cart.Api`

What changed was only admission control at `Storefront.Api`.

For this run, the rate-limited arm used an intentionally strict token bucket:

- bucket capacity: `1`
- refill rate: `5 tokens/s`

That is not presented as a universally correct production setting. It is a teaching configuration chosen to make overload protection measurable instead of theoretical.

## Observation Boundary

The measured top-level boundary is the user-visible Storefront checkout boundary:

- start: request arrival at `Storefront.Api`
- end: final HTTP response emitted by `Storefront.Api`

The analyzer was filtered with:

- `--operation storefront-checkout-sync`

To inspect downstream pressure separately, the analyzer also measured:

- `--operation payment-authorize`

This matters because the rate-limited arm returns many fast `429` responses. If we looked only at mixed response-time percentiles, we could accidentally claim the system became “magically faster.” It did not. It became more selective about what it admitted.

## Topology

- one clean workspace for `no-limit`
- one clean workspace for `rate-limit`
- one `Storefront.Api`
- one `Order.Api`
- one `PaymentSimulator.Api`
- one `Cart.Api`
- no `Worker` participation in this milestone
- one SQLite `primary.db` per workspace
- one warm-up checkout per arm before measured traffic

The experiment was produced by:

```bash
./scripts/experiments/run-milestone-6-no-limit-vs-rate-limit-overload.sh
```

Generated artifacts:

- `docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/artifacts/no-limit/`
- `docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/artifacts/rate-limit/`
- `docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/artifacts/comparison.json`

## Workload

This run intentionally hammered one hot partition instead of simulating healthy multi-user traffic:

- `80` checkout attempts
- `64` client-side concurrency cap
- `10 ms` spacing between scheduled attempts
- same `userId = user-0002` for every measured request
- same cart and product path for every measured request
- `paymentMode = fast_success`
- unique `Idempotency-Key` and `X-Correlation-Id` per request

That setup was deliberate.

The limiter key for checkout is route plus user identity, so using one hot user keeps all pressure inside the same partition. The goal here was not realism. The goal was to justify backpressure under a concentrated overload condition.

## Results

### Top-level boundary

| Arm | Total requests | Admitted requests | Rejected requests | Reject rate | Mixed p95 (ms) | Admitted p95 (ms) | Admitted throughput (req/s) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `no-limit` | `80` | `80` | `0` | `0.00%` | `710.921` | `710.921` | `54.214` |
| `rate-limit` | `80` | `4` | `76` | `95.00%` | `2.104` | `50.021` | `5.154` |

Two observations matter immediately:

- the mixed `p95` in the rate-limited arm is not a valid “system got faster” number, because it is dominated by very fast `429` rejections
- the admitted `p95` is the honest comparison, and it dropped from `710.921 ms` to `50.021 ms`

That is a reduction of about `92.964%` in admitted-request `p95`.

### Downstream payment pressure

| Arm | Payment requests issued | Payment p95 (ms) | Pending projection jobs after run | Oldest queued age (ms) |
| --- | ---: | ---: | ---: | ---: |
| `no-limit` | `80` | `6.697` | `80` | `2193.612` |
| `rate-limit` | `4` | `6.925` | `4` | `1447.093` |

Important interpretation:

- downstream payment latency did not materially change
- downstream payment volume changed enormously

The limiter reduced payment authorizations from `80` to `4`, which is a `95%` reduction in downstream pressure. The queue snapshot tells the same story for projection work: `80` pending jobs without the limiter versus `4` with it.

### Downstream payment failure rate

This experiment intentionally used `paymentMode = fast_success`, so downstream payment failure rate was not the differentiator here:

- `no-limit`: `0` payment failures
- `rate-limit`: `0` payment failures

That was deliberate. This run isolates overload and admission control rather than mixing in simulated provider failures.

## Interpretation

This experiment justifies backpressure, but only under an honest reading.

What improved:

- admitted-request latency improved dramatically
- downstream payment volume fell sharply
- queued follow-up work fell sharply

What did not improve:

- admitted throughput
- acceptance rate

In fact, the protected arm sacrificed a great deal:

- it rejected `95%` of the offered requests
- admitted throughput fell from `54.214 req/s` to `5.154 req/s`

So this is not a “faster system” story.

It is a boundary-protection story.

Without the limiter:

- all `80` requests were admitted
- the checkout boundary absorbed the burst
- admitted `p95` climbed to `710.921 ms`
- downstream services and follow-up queues saw the full burst

With the limiter:

- only `4` requests were admitted
- `76` were rejected immediately with `429`
- admitted `p95` stayed near `50 ms`
- only `4` payment requests and `4` projection jobs were created

That is exactly what admission control is supposed to do. It converts overload from “everyone waits and everything downstream gets hit” into “most requests are told no immediately, and the admitted ones stay within a much tighter latency envelope.”

But the cost is explicit and severe. If the business goal is “accept almost everything,” this limiter configuration is too strict. If the business goal is “preserve a tight latency budget for the few requests we do admit,” then this configuration demonstrates why backpressure exists.

## Architectural Justification

This milestone justifies admission control in the lab for three concrete reasons:

1. It keeps the observation boundary honest.
   The user-visible request either gets rejected quickly or admitted into a bounded amount of work. We stop pretending overload can be absorbed for free.

2. It protects downstream dependencies.
   Payment authorization volume dropped from `80` to `4` even though the downstream service itself was healthy. Protection is about limiting pressure, not only about reacting to explicit downstream failure.

3. It protects follow-up work too.
   Even a fast synchronous checkout still creates queue-backed projection work. Backpressure reduced that queue growth from `80` pending jobs to `4`.

The important non-claim is just as valuable:

- this report does not prove that “rate limiting makes the system faster”
- it proves that a strict limiter can preserve admitted latency by refusing most offered work

That is the right lesson for this milestone.
