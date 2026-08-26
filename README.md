# System Design Lab

Measure. Break. Protect. Recover.

An executable .NET 10 laboratory for testing system-design claims under load.
Every published conclusion is tied to a runnable experiment, an explicit
observation boundary, and a candid nonclaim.

## Four useful failures

| Question | Observation |
| --- | --- |
| [Can equal throughput hide different bottlenecks?](docs/experiments/milestone-1-cpu-vs-io-report.md) | CPU and I/O arms both held about 2.1 req/s while time moved to different stages. |
| [What does asynchronous checkout really buy?](docs/experiments/milestone-5-sync-vs-async-checkout-report.md) | Throughput rose 45.6%, while queue work and eventual-completion cost became explicit. |
| [What does overload protection sacrifice?](docs/experiments/milestone-6-no-limit-vs-rate-limit-overload-report.md) | Admitted p95 fell 93%, because a strict limiter refused 95% of offered work. |
| [Can degraded mode stay available and explain its cost?](docs/experiments/milestone-9-degraded-mode-failover-report.md) | Both degraded paths served all 48 requests while tracing located two different penalties. |

Five more reports cover caching, synchronous dependency failures, scaling,
replica freshness, and regional latency. The complete
[experiment index](docs/experiments/README.md) retains all nine.

## Topology

    LoadGen → Proxy → Storefront
                          ├→ Catalog → primary / regional replicas
                          ├→ Cart
                          └→ Order → Payment simulator
                                └→ durable queue → Worker → read models

The processes are intentionally small. SQLite, injected latency, in-memory
caches, and a local reverse proxy expose mechanisms without pretending to be
production infrastructure.

## Verify

Requires .NET SDK 10.0.201, pinned in `global.json`.

    make check

That restores locked dependencies, verifies formatting, builds Release with
warnings as errors, runs 124 tests, and refuses known vulnerable dependencies.

## Run an experiment

    make experiment-1
    make experiment-5
    make experiment-6
    make experiment-9-failover

Experiment workspaces and raw artifacts default to
/tmp/system-design-lab-experiments. Only the nine curated reports are tracked.
Set LAB_EXPERIMENT_OUTPUT_ROOT to preserve a run elsewhere.

## Boundaries

This is a one-machine simulation. Latency is injected, replicas are snapshots,
cache state is process-local, sticky assignments are in memory, and SQLite
stands in for durable storage. The reports claim measured mechanisms inside
those boundaries, not proof of a production distributed system.
