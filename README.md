# E-Commerce Systems Lab

This is the executable code root for the local systems-design lab.

The backlog and extracted ticket queue live one level up:

## Architecture

The lab is intentionally built as small explicit processes so system behavior stays visible:

- `Storefront.Api`
  the user-visible boundary for product reads, cart mutation, checkout, and order-history reads
- `Catalog.Api`
  product and inventory reads from `primary.db` or the simulated replicas
- `Cart.Api`
  durable cart state in `primary.db`
- `Order.Api`
  synchronous and asynchronous checkout execution against `primary.db`
- `PaymentSimulator.Api`
  explicit external payment-provider simulator with configurable modes
- `Worker`
  background queue consumer for payment confirmation and projection jobs
- `Proxy`
  explicit reverse proxy for round-robin and sticky-routing experiments
- `SeedData`, `LoadGen`, and `Analyze`
  operator tools for initialization, traffic generation, and measurement

## Milestone Spine

The codebase is organized around milestone experiments rather than around a fake production rollout:

- milestone 1
  CPU-bound vs I/O-bound latency at the request boundary
- milestone 2
  cache-off vs cache-on at the Storefront product-page boundary
- milestone 3
  cart persistence and routing identity groundwork for later proxy experiments
- milestone 4
  synchronous checkout and dependency-bound latency
- milestone 5
  sync vs async checkout and moved background cost
- milestone 6
  overload and token-bucket admission control
- milestone 7
  reverse proxy, sticky routing, and scaling limits
- milestone 8
  primary vs replica and read-model freshness tradeoffs
- milestone 9
  same-region vs cross-region behavior and degraded failover reasoning

Runnable milestone scripts and their curated reports live in:

- [docs/experiments/README.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/README.md)

## Operator Quick Start

This is the shortest clean-machine-style path from clone to report:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
dotnet tool restore
dotnet build ecommerce-systems-lab.sln

export LAB_OPERATOR_ROOT=/tmp/ecommerce-systems-lab-demo

./scripts/operators/seed-lab.sh
./scripts/operators/start-core-topology.sh

Lab__Repository__RootPath="$LAB_OPERATOR_ROOT" \
dotnet run --project src/LoadGen --no-build -- \
  --target-url 'http://127.0.0.1:5202/products/sku-0001?cache=off&readSource=primary' \
  --method GET \
  --rps 5 \
  --duration-seconds 5 \
  --concurrency-cap 2 \
  --run-id quickstart-product-page

./scripts/operators/analyze-run.sh quickstart-product-page product-page
./scripts/operators/stop-topology.sh
```

That walkthrough produces:

- `"$LAB_OPERATOR_ROOT/logs/runs/quickstart-product-page/summary.json"`
- `"$LAB_OPERATOR_ROOT/analysis/quickstart-product-page/report.md"`

## Operator Scripts

The operator scripts are the stable entry points for manual lab work:

- [`seed-lab.sh`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/scripts/operators/seed-lab.sh)
  seeds `primary.db`, rebuilds the product-page read model, and syncs both replicas by default
- [`start-core-topology.sh`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/scripts/operators/start-core-topology.sh)
  starts `Catalog.Api`, `Cart.Api`, `Order.Api`, `PaymentSimulator.Api`, `Storefront.Api`, and `Worker`
- [`start-east-west-topology.sh`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/scripts/operators/start-east-west-topology.sh)
  starts east/west Catalog and Storefront instances plus the local `Proxy`
- [`analyze-run.sh`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/scripts/operators/analyze-run.sh)
  runs `Analyze` against one `runId` and prints the generated output paths
- [`stop-topology.sh`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/scripts/operators/stop-topology.sh)
  stops any operator-managed background processes for the chosen workspace

All operator scripts default to:

```text
/tmp/ecommerce-systems-lab-operator
```

Override that by exporting `LAB_OPERATOR_ROOT` or by passing an explicit workspace argument to the script you are running.

### Core Topology

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
export LAB_OPERATOR_ROOT=/tmp/ecommerce-systems-lab-core

./scripts/operators/seed-lab.sh
./scripts/operators/start-core-topology.sh
curl http://127.0.0.1:5202/health
./scripts/operators/stop-topology.sh
```

Default core topology ports:

- `Storefront.Api` -> `http://127.0.0.1:5202`
- `Catalog.Api` -> `http://127.0.0.1:5203`
- `Cart.Api` -> `http://127.0.0.1:5204`
- `Order.Api` -> `http://127.0.0.1:5205`
- `PaymentSimulator.Api` -> `http://127.0.0.1:5206`

### East/West Topology

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
export LAB_OPERATOR_ROOT=/tmp/ecommerce-systems-lab-regions

./scripts/operators/seed-lab.sh
./scripts/operators/start-east-west-topology.sh
curl http://127.0.0.1:5300/proxy/status
curl 'http://127.0.0.1:5302/products/sku-0001?cache=off&readSource=local'
./scripts/operators/stop-topology.sh
```

Default east/west topology ports:

- `Proxy` -> `http://127.0.0.1:5300`
- east `Storefront.Api` -> `http://127.0.0.1:5301`
- west `Storefront.Api` -> `http://127.0.0.1:5302`
- east `Catalog.Api` -> `http://127.0.0.1:5303`
- west `Catalog.Api` -> `http://127.0.0.1:5304`

### Run Every Experiment

All milestone experiments already have dedicated scripts under:

- [/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/scripts/experiments](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/scripts/experiments)

For the index of all experiment scripts and their output folders, use:

- [docs/experiments/README.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/README.md)

## Logs, Runs, And Reports

For a workspace rooted at `LAB_OPERATOR_ROOT=/tmp/ecommerce-systems-lab-demo`, the main outputs land here:

- `/tmp/ecommerce-systems-lab-demo/data/`
  SQLite databases such as `primary.db`, `readmodels.db`, `replica-east.db`, and `replica-west.db`
- `/tmp/ecommerce-systems-lab-demo/logs/requests.jsonl`
  request traces
- `/tmp/ecommerce-systems-lab-demo/logs/jobs.jsonl`
  background job traces
- `/tmp/ecommerce-systems-lab-demo/logs/*.log`
  service logs emitted through `ILogger`, including tool logs when you pass `Lab__Repository__RootPath` to `LoadGen` or `Analyze`
- `/tmp/ecommerce-systems-lab-demo/logs/runs/<run-id>/summary.json`
  analyzer summary for one measured run
- `/tmp/ecommerce-systems-lab-demo/analysis/<run-id>/report.md`
  analyzer markdown report for one measured run
- `/tmp/ecommerce-systems-lab-demo/.operator/`
  operator script build log, per-service stdout logs, pid files, and topology manifests

Curated experiment write-ups stay inside the repo under:

- [/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments)

## Troubleshooting

### SQLite Locked Database

Symptoms:

- `SQLite Error 5: 'database is locked'`
- a seed or projection rebuild hangs behind another process

What to do:

- stop the current topology with [`stop-topology.sh`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/scripts/operators/stop-topology.sh)
- make sure two different shells are not sharing the same `LAB_OPERATOR_ROOT`
- do not run `SeedData`, `Worker`, and experiment scripts concurrently against the same workspace unless that concurrency is the thing you are intentionally testing
- if needed, start again with a fresh workspace such as `export LAB_OPERATOR_ROOT=/tmp/ecommerce-systems-lab-fresh`

### Missing Run ID

Symptoms:

- `Analyze` generates an empty report
- you cannot find your traffic in `requests.jsonl`

What to do:

- `LoadGen` already stamps `X-Run-Id`, so prefer it for measured runs
- for manual `curl`, send your own header:

```bash
curl -H 'X-Run-Id: manual-demo-run' http://127.0.0.1:5202/health
```

- inspect the logs directly when in doubt:

```bash
rg 'manual-demo-run|quickstart-product-page' "$LAB_OPERATOR_ROOT/logs/requests.jsonl"
```

### Stale Logs

Symptoms:

- an old run pollutes a new analysis
- the log directory contains data from an earlier workspace state

What to do:

- operator scripts intentionally reuse the same workspace unless you change it
- switch to a new root for a fresh run:

```bash
export LAB_OPERATOR_ROOT=/tmp/ecommerce-systems-lab-new
```

- or stop the topology and remove the old workspace completely:

```bash
./scripts/operators/stop-topology.sh
rm -rf "$LAB_OPERATOR_ROOT"
```

### Port Collisions

Symptoms:

- a service exits during startup
- health checks never become ready

What to do:

- stop the existing operator-managed processes first
- check the per-service stdout log in `"$LAB_OPERATOR_ROOT/.operator/"`
- override ports with environment variables before starting the topology again

Example:

```bash
export LAB_STOREFRONT_URL=http://127.0.0.1:5402
export LAB_CATALOG_URL=http://127.0.0.1:5403
./scripts/operators/start-core-topology.sh
```

## Limits Of This Local Simulation

This lab is intentionally honest about what it is and what it is not:

- it runs on one machine with local processes, not on real distributed infrastructure
- network latency is injected deliberately, not discovered from a physical network
- replicas are lagged snapshot copies, not a full streaming replication system
- caches are in-memory and process-local
- the proxy keeps sticky assignments in memory only
- SQLite stands in for durable storage because the point is measurement and mechanism, not cloud operations
- some downstream services still emit their own request traces as a transitional debugging aid, even though the intended end-state is stricter Storefront-owned top-level request truth

## Layout

```text
ecommerce-systems-lab/
  Directory.Build.props
  global.json
  ecommerce-systems-lab.sln

  src/
  tests/
  data/
  logs/
  analysis/
  docs/
```

## Build

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
dotnet tool restore
dotnet build ecommerce-systems-lab.sln
```

## Test

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
dotnet test ecommerce-systems-lab.sln
```

The regression suite is split intentionally:

- `tests/Lab.UnitTests`
  deterministic mechanism tests for cache TTL and invalidation, durable queue claim/retry/lease behavior, checkout persistence and non-negative inventory rules, token-bucket math, replica sync, and analyzer calculations
- `tests/Lab.IntegrationTests`
  ASP.NET test-hosting coverage for API boundaries, proxy routing, payment modes, stale-read behavior, worker processing, and full multi-service journeys such as Storefront cart -> async checkout -> Worker completion -> order-history projection

All tests run against temporary directories plus local SQLite files. They do not require external infrastructure.

## First Run Targets

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
dotnet run --project src/Storefront.Api
dotnet run --project src/Analyze
```

The code is intentionally scaffolded as small, explicit processes so later tickets can make architecture and measurement visible rather than hidden behind infrastructure.

## Local Reverse Proxy

`Proxy` is now a small explicit reverse proxy for milestone 7.

- it matches route prefixes to backend groups
- it supports `round_robin` and `sticky` routing modes
- it forwards method, path, query string, body, and a small header allowlist
- it logs the chosen backend per request
- it exposes `GET /proxy/status` so the active route table is inspectable at runtime

Sticky mode uses the same routing key convention introduced in Storefront:

- request header `X-Session-Key`
- or cookie `lab-session` when the header is absent

When sticky mode is enabled, the proxy keeps the assignment in memory and reuses it while the backend stays reachable. If that backend disappears, the proxy clears the old assignment, remaps the session to another healthy backend, and retries the request once.

Example with two Storefront instances:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
LAB_ROOT=/tmp/ecommerce-systems-lab-proxy

Lab__Repository__RootPath=$LAB_ROOT \
dotnet run --project src/Storefront.Api -- --urls http://127.0.0.1:5091

Lab__Repository__RootPath=$LAB_ROOT \
dotnet run --project src/Storefront.Api -- --urls http://127.0.0.1:5092

Lab__Repository__RootPath=$LAB_ROOT \
Lab__Proxy__Storefront__Backends__0=http://127.0.0.1:5091 \
Lab__Proxy__Storefront__Backends__1=http://127.0.0.1:5092 \
Lab__Proxy__RoutingMode=sticky \
Lab__Proxy__Catalog__Enabled=false \
dotnet run --project src/Proxy -- --urls http://127.0.0.1:5090

curl http://127.0.0.1:5090/proxy/status
curl -i -H 'X-Session-Key: sess-demo' http://127.0.0.1:5090/health
```

The response includes:

- `X-Proxy-Backend`
- `X-Proxy-Route`
- `X-Proxy-Routing-Mode`

The proxy service log is written to `logs/proxy.log`.

## Shared Configuration

All hosts and tools use the same shared configuration model from `Lab.Shared`.

The main sections are:

- `Lab:Repository`
- `Lab:DatabasePaths`
- `Lab:LogPaths`
- `Lab:Regions`
- `Lab:RegionalDegradation`
- `Lab:Cache`
- `Lab:Queue`
- `Lab:PaymentSimulator`
- `Lab:RateLimiter`

Paths default to values relative to the repository root and can be overridden through normal .NET configuration sources such as environment variables or command-line arguments.

## Region Simulation

Milestone 9 adds an explicit region model for service instances and a centrally configured network envelope for service-to-service HTTP calls.

The main knobs are:

- `Lab:Regions:CurrentRegion`
- `Lab:Regions:SameRegionLatencyMs`
- `Lab:Regions:CrossRegionLatencyMs`
- `Lab:RegionalDegradation:SimulateLocalReplicaUnavailable`
- `Lab:RegionalDegradation:SimulateLocalCatalogUnavailable`
- `Lab:ServiceEndpoints:CatalogRegion`
- `Lab:ServiceEndpoints:CatalogFailoverBaseUrl`
- `Lab:ServiceEndpoints:CatalogFailoverRegion`
- `Lab:ServiceEndpoints:CartRegion`
- `Lab:ServiceEndpoints:OrderRegion`
- `Lab:ServiceEndpoints:PaymentSimulatorRegion`

The current host region comes from `Lab:Regions:CurrentRegion`. Outbound typed `HttpClient` dependencies then compare that caller region with the configured target service region and inject either the same-region or cross-region delay before sending the request.

The resulting dependency trace metadata now includes:

- `callerRegion`
- `targetRegion`
- `networkScope`
- `injectedDelayMs`

Example east/west product-read topology:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
LAB_ROOT=/tmp/ecommerce-systems-lab-region

Lab__Repository__RootPath=$LAB_ROOT \
dotnet run --project src/SeedData -- --products 4 --users 2 --reset true --sync-replicas true

Lab__Repository__RootPath=$LAB_ROOT \
Lab__Regions__CurrentRegion=us-west \
dotnet run --project src/Catalog.Api -- --urls http://127.0.0.1:5193

Lab__Repository__RootPath=$LAB_ROOT \
Lab__Regions__CurrentRegion=us-east \
Lab__Regions__SameRegionLatencyMs=2 \
Lab__Regions__CrossRegionLatencyMs=17 \
Lab__ServiceEndpoints__CatalogBaseUrl=http://127.0.0.1:5193 \
Lab__ServiceEndpoints__CatalogRegion=us-west \
dotnet run --project src/Storefront.Api -- --urls http://127.0.0.1:5194

curl 'http://127.0.0.1:5194/products/sku-0001?cache=off&readSource=primary'
```

With that topology, `Storefront.Api` remains in `us-east`, `Catalog.Api` runs in `us-west`, and the `catalog-api` dependency trace will show `networkScope = cross-region` plus the injected delay.

Milestone 9 also adds explicit degraded-mode switches for regional reasoning:

- `SimulateLocalReplicaUnavailable` forces `readSource=local` product reads to fall back from the local replica to the primary source when the current region would normally use a replica
- `SimulateLocalCatalogUnavailable` tells `Storefront.Api` to reroute Catalog reads through the configured failover Catalog endpoint instead of the nominal local one

Those switches are not presented as full production failover. They are teaching controls that make “slower but available” regional behavior measurable and traceable.

## Shared Contracts And Tracing

The first contract and telemetry primitives live in shared code:

- `Lab.Shared.Contracts.OperationContractDescriptor`
- `Lab.Shared.Contracts.BusinessOperationContracts`
- `Lab.Telemetry.RequestTracing.RequestTraceContext`
- `Lab.Telemetry.RequestTracing.IRequestTraceFactory`

Endpoints can now begin a request trace, record stage timings and dependency calls, mark contract/cache/rate-limit outcomes, and finalize an immutable request trace record without writing directly to disk.

## Telemetry Persistence

Measurable telemetry and ordinary service logs are now split on purpose:

- `Storefront.Api` is the intended long-term owner of the user-visible `logs/requests.jsonl` boundary
- `Worker` owns `logs/jobs.jsonl`
- every host writes ordinary operational logs through the built-in logging abstraction to its own service log file such as `logs/storefront.log` or `logs/worker.log`

The JSONL writers append one machine-parseable JSON object per line, while operational logs remain human-oriented.

Current milestone note:

- `Catalog.Api`, `Cart.Api`, `Order.Api`, and `PaymentSimulator.Api` still emit request traces directly as a temporary internal-debugging aid
- those downstream traces help us inspect local mechanisms while the system is still being assembled service by service
- the end-state architecture from `project.md` is stricter: `Storefront.Api` remains the only user-visible request boundary, and downstream timing should ultimately flow upward as debug telemetry or merged dependency detail instead of acting like a second top-level source of truth

## Analyze CLI

The first analyzer reads `logs/requests.jsonl` and `logs/jobs.jsonl`, computes latency and concurrency metrics, and emits:

- `logs/runs/{runId}/summary.json`
- `analysis/{runId}/report.md`

The report now also includes a live queue-state snapshot from `primary.db` when that database is available:

- pending, ready, delayed, in-progress, completed, and failed queue counts
- oldest queued item age
- processed-job metrics from `jobs.jsonl` so waiting and execution can be discussed separately
- overload breakdowns that separate rejected requests from admitted requests
- timeout rate, admitted throughput, admitted p95 latency, and aggregate retry counts so limiter experiments can compare shedding versus slow-path pain
- read-freshness metrics that quantify stale-read incidence, max observed lag, and latency split by `readSource`

When `--run-id` is supplied, the queue snapshot is filtered by the queue job payload `runId` when available.

Example:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
dotnet run --project src/Analyze -- --run-id storefront.api-20260331T110557496Z-abc123
dotnet run --project src/Analyze -- --run-id milestone-2-cache-on --operation product-page
```

Optional filters:

- `--run-id`
- `--from`
- `--to`
- `--operation`

## Seed And Load

The first experiment helpers are now available:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
dotnet run --project src/SeedData -- --products 100 --users 25 --reset true
dotnet run --project src/LoadGen -- --target-url http://127.0.0.1:5081/ --rps 5 --duration-seconds 5 --concurrency-cap 2 --run-id demo-run
```

`LoadGen` stamps `X-Run-Id` and `X-Correlation-Id` automatically so the server-side request telemetry can be filtered by run.

## Primary Database And Migrations

`Lab.Persistence` now owns the authoritative SQLite schema for `primary.db`.

- `PrimaryDatabaseInitializer` applies EF migrations on first use
- `SeedData` seeds against the migrated schema instead of hand-writing tables
- SQLite is configured with WAL mode and a `5000 ms` busy timeout through the shared context factory

The checkout write path groundwork now also lives in `Lab.Persistence.Checkout`:

- `CheckoutPersistenceService` transactionally reserves inventory with `available_quantity -= n` and `reserved_quantity += n`
- order, order-item, and payment rows can be persisted in the same local transaction
- inventory is protected by DB-level nonnegative check constraints

The first durable queue primitive now also lives in `Lab.Persistence.Queueing`:

- `IDurableQueueStore` and `SqliteDurableQueueStore`
- explicit operations for `enqueue`, `claim`, `complete`, `fail`, `reschedule`, and `abandon expired lease`
- queue backlog snapshots with ready vs delayed pending counts
- lease ownership so two workers do not normally process the same job at the same time

The first real background worker now also lives in `Worker`:

- `BackgroundWorker` polls the durable queue and delegates real work to `WorkerQueueProcessor`
- `WorkerQueueProcessor` measures queue delay and execution time separately and writes one `JobTraceRecord` per attempt to `logs/jobs.jsonl`
- the current job handlers are:
  - `payment-confirmation-retry`
  - `order-history-projection-update`
  - `product-page-projection-rebuild`
- payment retry jobs can reschedule themselves with updated payload state, which is how a pending confirmation flips from provider authorization into later status checks
- retry, pending, completed, and failed queue states are all visible in both `queue_jobs` and `logs/jobs.jsonl`

Useful commands:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
dotnet tool restore
dotnet tool run dotnet-ef migrations list --project src/Lab.Persistence/Lab.Persistence.csproj --context PrimaryDbContext
dotnet tool run dotnet-ef database update --project src/Lab.Persistence/Lab.Persistence.csproj --context PrimaryDbContext
dotnet run --project src/SeedData -- --products 100 --users 25 --reset true
dotnet run --project src/Worker
```

## Storefront Lab Endpoints

The first measurable lab endpoints are now available in `Storefront.Api`:

- `GET /health`
- `GET /cpu?workFactor=20&iterations=1000`
- `GET /io?delayMs=80&jitterMs=0`
- `GET /products/{id}?cache=on|off&readSource=local|primary|replica-east|replica-west`

The `/cpu` endpoint performs deterministic CPU work, returns a checksum so the work cannot be optimized away trivially, and emits explicit request tracing stages:

- `request_received`
- `cpu_work_started`
- `cpu_work_completed`
- `response_sent`

The `/io` endpoint simulates downstream wait with intentionally low CPU cost, returns the applied delay, and emits explicit request tracing stages:

- `request_received`
- `downstream_wait_started`
- `downstream_wait`
- `downstream_wait_completed`
- `response_sent`

Example:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
dotnet run --project src/Storefront.Api -- --urls http://127.0.0.1:5083
curl 'http://127.0.0.1:5083/cpu?workFactor=20&iterations=1000'
curl 'http://127.0.0.1:5083/io?delayMs=80&jitterMs=0'
```

## Storefront Product Read Path

`Storefront.Api` is now the user-facing observation boundary for product reads:

- `GET /products/{id}?cache=on|off`
- `GET /products/{id}?cache=on|off&readSource=local|primary|replica-east|replica-west`

The Storefront product path:

- calls `Catalog.Api` instead of reading `primary.db` directly
- can bypass or use its own in-memory cache per request
- supports `local`, which resolves to the Catalog region's same-region physical source when available
- falls back explicitly when `local` cannot map to a same-region physical source
- keeps Storefront cache scope separated by Storefront region and by the effective physical read source
- records top-level request stages for `request_received`, `cache_lookup`, `catalog_call_started`, `catalog_call_completed`, and `response_sent`
- records whether the Storefront response was a cache hit in `logs/requests.jsonl`
- records requested vs effective read source plus fallback metadata in the stage metadata so replica experiments stay explicit
- returns freshness metadata and writes top-level stale-read metrics into `logs/requests.jsonl` so replica lag is measurable instead of anecdotal

Configuration:

- `Lab:ServiceEndpoints:CatalogBaseUrl` defaults to `http://localhost:5203`

Example:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/SeedData -- --products 25 --users 5 --reset true
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/Catalog.Api -- --urls http://127.0.0.1:5084
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
Lab__ServiceEndpoints__CatalogBaseUrl=http://127.0.0.1:5084 \
dotnet run --project src/Storefront.Api -- --urls http://127.0.0.1:5083
curl -H 'X-Debug-Telemetry: true' 'http://127.0.0.1:5083/products/sku-0001?cache=on'
curl -H 'X-Debug-Telemetry: true' 'http://127.0.0.1:5083/products/sku-0001?cache=on'
curl 'http://127.0.0.1:5083/products/sku-0001?cache=off'
```

## Storefront Cart Orchestration

`Storefront.Api` now exposes the user-facing add-to-cart boundary:

- `POST /cart/items`

The Storefront cart path:

- forwards the mutation to `Cart.Api` instead of mutating `primary.db` directly
- records a dependency call for `cart-api` in the top-level request trace
- includes `userId` in request trace metadata
- includes `sessionKey` in request traces so later sticky-routing experiments have a stable routing identity
- distinguishes explicit business failures from upstream technical failures
- validates the returned cart state before treating the operation as successful

Configuration:

- `Lab:ServiceEndpoints:CartBaseUrl` defaults to `http://localhost:5204`

Routing convention:

- request header `X-Session-Key`
- request cookie `lab-session`
- if both are missing, Storefront generates a session key, returns it in `X-Session-Key`, and also issues `lab-session`

Background reading:

- `docs/routing/cart-state-routing-primer.md`

Example:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/SeedData -- --products 25 --users 5 --reset true
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/Cart.Api -- --urls http://127.0.0.1:5085
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
Lab__ServiceEndpoints__CartBaseUrl=http://127.0.0.1:5085 \
dotnet run --project src/Storefront.Api -- --urls http://127.0.0.1:5083
curl -X POST 'http://127.0.0.1:5083/cart/items' \
  -H 'Content-Type: application/json' \
  -d '{"userId":"user-0001","productId":"sku-0001","quantity":2}'
```

## Catalog Service

`Catalog.Api` now exposes the first real product read path backed by `primary.db`:

- `GET /catalog/products/{id}`
- `GET /catalog/products/{id}?readSource=local|primary|replica-east|replica-west`

The response returns:

- product identity and descriptive fields
- price data
- inventory summary and stock status
- version
- the chosen `readSource`
- freshness comparison fields showing whether the selected source was stale relative to primary

`local` is region-aware:

- in the primary region it resolves to `primary`
- in a configured replica region it resolves to that replica
- if no same-region physical source is available, Catalog falls back explicitly and records the fallback reason in the trace metadata

Catalog now keeps cache entries separated by read source, so a cached `replica-east` lookup cannot accidentally satisfy a later `primary` lookup for the same product id.

When the caller sends `X-Debug-Telemetry: true`, the response also includes internal stage metadata for:

- `request_received`
- `db_query_started`
- `db_query_completed`
- `response_sent`

Example:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
dotnet run --project src/SeedData -- --products 25 --users 5 --reset true
dotnet run --project src/Catalog.Api -- --urls http://127.0.0.1:5084
curl -H 'X-Debug-Telemetry: true' 'http://127.0.0.1:5084/catalog/products/sku-0001'
```

## In-Memory Cache

The first measurable cache abstraction now lives in `Lab.Shared.Caching`.

It provides:

- `ICacheStore` with `get`, `set`, `invalidate`, and proactive `expire`
- TTL-aware in-memory entries
- region-scoped namespaces through `CacheScope`
- in-memory metrics snapshots through `ICacheSnapshotProvider`

`Catalog.Api` now uses this cache for product detail reads, and `Storefront.Api` uses a separate namespace for user-facing product-page caching. A repeated request to the same product should show a miss on the first debug-telemetry response and a hit on the second one when the relevant cache is enabled.

## Read-Model Projection Store

The first denormalized read-model store now lives in `readmodels.db` through `ReadModelDbContext`.

Current read-model table:

- `ReadModel_ProductPage`

Each row stores:

- `product_id`
- `region`
- `projection_version`
- `summary_json`
- `projected_utc`

`summary_json` is built from authoritative product and inventory tables and is intentionally document-like so later tickets can compare primary reads, cache reads, and read-model reads without changing the underlying source of truth.

Build or rebuild the product-page projection with `SeedData`:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
dotnet run --project src/SeedData -- --products 25 --users 5 --reset true --rebuild-product-page-projection true

Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
dotnet run --project src/SeedData -- --skip-primary-seed true --rebuild-product-page-projection true
```

The first command seeds the authoritative primary store and then rebuilds the projection. The second command rebuilds the projection only, which is useful when the source-of-truth tables already exist and you want to prove the rebuild path is repeatable.

## Replica Sync

Milestone 8 now has a first explicit replica mechanism.

Current v1 behavior:

- `SeedData` can copy a lagged snapshot of the primary catalog source tables into `replica-east.db` and `replica-west.db`
- the replica snapshot currently includes `products` and `inventory`
- the mechanism is intentionally simple: capture a source snapshot first, then apply that snapshot to each replica after the configured lag

That last detail is the important one. If primary changes during the configured lag window, the replica still applies the older captured snapshot. This is how the lab makes stale reads intentional instead of accidental.

Seed and sync replicas in one run:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
Lab__ReplicaSync__EastLagMilliseconds=250 \
Lab__ReplicaSync__WestLagMilliseconds=500 \
dotnet run --project src/SeedData -- --products 25 --users 5 --reset true --sync-replicas true
```

Resync replicas without reseeding primary:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
dotnet run --project src/SeedData -- --skip-primary-seed true --sync-replicas true
```

Optional one-off CLI lag overrides:

- `--replica-east-lag-ms <milliseconds>`
- `--replica-west-lag-ms <milliseconds>`

## Payment Simulator

`PaymentSimulator.Api` now acts as the local external payment-provider boundary.

Endpoints:

- `GET /`
- `POST /payments/authorize`
- `GET /payments/authorizations/{paymentId}`

Mode selection is explicit and per request:

- query string `?mode=fast_success`
- request header `X-Payment-Simulator-Mode: slow_success`
- fallback to `Lab:PaymentSimulator:DefaultMode`

Resolution order is:

1. query string
2. request header
3. configured default mode

Supported modes:

- `fast_success`
- `slow_success`
- `timeout`
- `transient_failure`
- `duplicate_callback`
- `delayed_confirmation`

The authorization request body accepts:

- `paymentId`
- `orderId`
- `amountCents`
- `currency`
- `callbackUrl`

`delayed_confirmation` and `duplicate_callback` schedule background callbacks inside `PaymentSimulator.Api`. If no `callbackUrl` is provided, the callback is still materialized in simulator-side state and eventually marked `SkippedNoTarget`, which makes the mechanism visible without requiring `Order.Api` to be live yet.

Useful configuration:

- `Lab:PaymentSimulator:DefaultMode`
- `Lab:PaymentSimulator:FastLatencyMilliseconds`
- `Lab:PaymentSimulator:SlowLatencyMilliseconds`
- `Lab:PaymentSimulator:TimeoutLatencyMilliseconds`
- `Lab:PaymentSimulator:DelayedConfirmationMilliseconds`
- `Lab:PaymentSimulator:DuplicateCallbackSpacingMilliseconds`
- `Lab:PaymentSimulator:DispatcherPollMilliseconds`

Example:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
dotnet run --project src/PaymentSimulator.Api -- --urls http://127.0.0.1:5086

curl -X POST 'http://127.0.0.1:5086/payments/authorize?mode=fast_success' \
  -H 'Content-Type: application/json' \
  -d '{"paymentId":"pay-fast-001","orderId":"order-fast-001","amountCents":1500,"currency":"USD"}'

curl -X POST 'http://127.0.0.1:5086/payments/authorize' \
  -H 'Content-Type: application/json' \
  -H 'X-Payment-Simulator-Mode: timeout' \
  -d '{"paymentId":"pay-timeout-001","orderId":"order-timeout-001","amountCents":1500,"currency":"USD"}'

curl -X POST 'http://127.0.0.1:5086/payments/authorize?mode=delayed_confirmation' \
  -H 'Content-Type: application/json' \
  -d '{"paymentId":"pay-delayed-001","orderId":"order-delayed-001","amountCents":1500,"currency":"USD"}'

curl 'http://127.0.0.1:5086/payments/authorizations/pay-delayed-001'
```

## Checkout

Checkout now has two explicit boundaries and two explicit execution modes.

User-visible boundary:

- `Storefront.Api`
- `POST /checkout?mode=sync|async`

Downstream execution boundary:

- `Order.Api`
- `POST /orders/checkout?mode=sync|async`

Both routes require the `Idempotency-Key` header. The key defines the retry boundary for checkout:

- the same idempotency key replays the original persisted result
- a duplicate key does not reserve inventory again
- a duplicate key does not create a second payment attempt
- a different idempotency key is treated as a new checkout attempt

The contract difference is now explicit in code and traces:

- `mode=sync` means payment confirmation is part of the request contract
- `mode=async` means “order accepted and pending” is success, while payment confirmation moves to `Worker`

Current trace operations:

- `storefront-checkout-sync`
- `storefront-checkout-async`
- `checkout-sync`
- `checkout-async`

Sync flow:

1. load the active cart from `primary.db`
2. validate the cart contents
3. reserve inventory and persist the order plus pending payment locally
4. call `PaymentSimulator.Api` synchronously over HTTP
5. finalize persisted order and payment status before the response closes

Async flow:

1. load the active cart from `primary.db`
2. validate the cart contents
3. reserve inventory
4. persist the order in `PendingPayment`
5. enqueue a `payment-confirmation-retry` job
6. return `202 Accepted` quickly with `checkoutMode = "async"` and `backgroundJobId`

What `contractSatisfied` means now:

- sync: `true` means the synchronous checkout contract resolved before the boundary closed
- async: `true` means the checkout was accepted and persisted as pending before the boundary closed

Current order statuses:

- `PendingPayment`
- `Paid`
- `Failed`
- `Cancelled`

Current payment statuses used by checkout:

- `Pending`
- `Authorized`
- `Failed`
- `Timeout`

Current sync rule:

- `fast_success` produces `Paid`
- `timeout` and non-successful provider failures produce `Failed`
- delayed confirmation modes leave the order in `PendingPayment`

Current async rule:

- a successful async response returns `202 Accepted`
- the order remains `PendingPayment` until `Worker` finishes the queued payment-confirmation job
- the request trace ends without a `payment-simulator` dependency call because that work moved outside the request boundary

Configuration:

- `Lab:ServiceEndpoints:OrderBaseUrl` defaults to `http://localhost:5205`
- `Lab:ServiceEndpoints:PaymentSimulatorBaseUrl` defaults to `http://localhost:5206`
- `Lab:RateLimiter:Checkout:Enabled` controls the custom token-bucket guard on `POST /checkout`
- `Lab:RateLimiter:Checkout:TokenBucketCapacity` controls the allowed burst per `route + user/session` bucket
- `Lab:RateLimiter:Checkout:TokensPerSecond` controls refill speed for that bucket

Checkout overload behavior:

- the limiter is our own in-memory token bucket, not the built-in ASP.NET rate limiter
- the current bucket key is `route + userId`, with session/client fallback when `userId` is missing
- rejected requests return `429 Too Many Requests`
- rejected requests include `Retry-After`
- rejected Storefront traces record `rateLimited = true`

Checkout request body:

- `userId`
- optional `paymentMode`
- optional `paymentCallbackUrl`

Example:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/SeedData -- --products 25 --users 5 --reset true
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/Cart.Api -- --urls http://127.0.0.1:5085
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/PaymentSimulator.Api -- --urls http://127.0.0.1:5086
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
Lab__ServiceEndpoints__PaymentSimulatorBaseUrl=http://127.0.0.1:5086 \
dotnet run --project src/Order.Api -- --urls http://127.0.0.1:5087
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
Lab__ServiceEndpoints__OrderBaseUrl=http://127.0.0.1:5087 \
dotnet run --project src/Storefront.Api -- --urls http://127.0.0.1:5088

curl -X POST 'http://127.0.0.1:5085/cart/items' \
  -H 'Content-Type: application/json' \
  -d '{"userId":"user-0001","productId":"sku-0001","quantity":2}'

curl -X POST 'http://127.0.0.1:5088/checkout?mode=sync' \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: checkout-user-0001-sync-001' \
  -d '{"userId":"user-0001","paymentMode":"fast_success"}'

curl -X POST 'http://127.0.0.1:5088/checkout?mode=async' \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: checkout-user-0001-async-001' \
  -d '{"userId":"user-0001","paymentMode":"slow_success"}'

curl -X POST 'http://127.0.0.1:5087/orders/checkout?mode=async' \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: checkout-user-0001-order-async-001' \
  -d '{"userId":"user-0001","paymentMode":"slow_success"}'
```

## Storefront Order History Read Model

`Storefront.Api` now exposes the user-visible order-history read boundary:

- `GET /orders/{userId}`
- `GET /orders/{userId}?readSource=local|read-model|primary-projection`

This path now supports two explicit read modes:

- `read-model`
  reads from `readmodels.db`, which can lag behind `primary.db`
- `primary-projection`
  reads authoritative order state from `primary.db` and materializes the denormalized projection during the request
- `local`
  prefers the same-region `read-model` path and falls back explicitly to `primary-projection` if the local read model is invalid

The mechanism is intentionally explicit:

1. `Order.Api` persists the authoritative order and payment state in `primary.db`
2. order changes enqueue an `order-history-projection-update` job
3. `Worker` applies that job and writes a denormalized summary row into `ReadModel_OrderHistory`
4. `Storefront.Api` serves `GET /orders/{userId}` from that read model

That means projection lag is both possible and measurable:

- a newly completed checkout can exist in `primary.db` before it appears in `GET /orders/{userId}`
- the lag ends only after `Worker` processes the queued projection update
- this is intentional because the lab is teaching read/write separation, not hiding it

Current order-history behavior:

- every order-history response includes freshness metadata
- `read-model` can be stale relative to `primary-projection`, and that stale-read incidence is recorded in the top-level request trace
- responses return denormalized summaries rather than raw normalized table rows
- empty history is a valid successful result
- explicit `read-model` corruption is surfaced as a failure instead of silently falling back
- `local` keeps that fallback explicit by recording requested vs effective source plus fallback reason in the `order_history_read` stage metadata
- the current Storefront trace operation is `order-history`
- the current Storefront stage of interest is `order_history_read`
- the chosen order-history read source is visible in that stage's metadata

Useful commands:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/SeedData -- --products 25 --users 5 --reset true
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/Cart.Api -- --urls http://127.0.0.1:5085
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/PaymentSimulator.Api -- --urls http://127.0.0.1:5086
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
Lab__ServiceEndpoints__PaymentSimulatorBaseUrl=http://127.0.0.1:5086 \
dotnet run --project src/Order.Api -- --urls http://127.0.0.1:5087
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab \
Lab__ServiceEndpoints__OrderBaseUrl=http://127.0.0.1:5087 \
dotnet run --project src/Storefront.Api -- --urls http://127.0.0.1:5088
Lab__Repository__RootPath=/tmp/ecommerce-systems-lab dotnet run --project src/Worker

curl -X POST 'http://127.0.0.1:5085/cart/items' \
  -H 'Content-Type: application/json' \
  -d '{"userId":"user-0001","productId":"sku-0001","quantity":1}'

curl -X POST 'http://127.0.0.1:5088/checkout?mode=sync' \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: order-history-demo-001' \
  -d '{"userId":"user-0001","paymentMode":"fast_success"}'

curl 'http://127.0.0.1:5088/orders/user-0001'
```

## Milestone 1 Experiment

For the standard experiment folder layout and the full index of runnable milestone scripts, see:

- `docs/experiments/README.md`

The first side-by-side CPU-vs-I/O experiment is automated here:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-1-cpu-vs-io.sh
```

The script performs a clean run, drives both endpoints, analyzes both run IDs, and copies the resulting artifacts into:

- `docs/experiments/milestone-1-cpu-vs-io/artifacts/`

The comparative write-up lives at:

- `docs/experiments/milestone-1-cpu-vs-io/report.md`

## Milestone 2 Experiment

The cache-off vs cache-on experiment is automated here:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-2-cache-off-vs-cache-on.sh
```

The script performs a clean run, seeds the primary store and projection store, disables `Catalog.Api` cache to isolate the Storefront cache decision, drives the hot product-page workload, analyzes both runs at the `product-page` boundary, and copies the resulting artifacts into:

- `docs/experiments/milestone-2-cache-off-vs-cache-on/artifacts/`

The comparative write-up lives at:

- `docs/experiments/milestone-2-cache-off-vs-cache-on/report.md`

## Milestone 4 Experiment

The synchronous checkout experiment is automated here:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-4-synchronous-checkout.sh
```

The script performs a clean run, seeds the primary store, starts `Cart.Api`, `PaymentSimulator.Api`, and `Order.Api`, performs one warm-up checkout to avoid cold-start distortion, prepares one cart per measured user, executes four checkout runs (`fast_success`, `slow_success`, `timeout`, and `transient_failure`), analyzes each run at both the `checkout-sync` and `payment-authorize` boundaries, and copies the resulting artifacts into:

- `docs/experiments/milestone-4-synchronous-checkout/artifacts/`

The comparative write-up lives at:

- `docs/experiments/milestone-4-synchronous-checkout/report.md`

## Milestone 5 Experiment

The sync-vs-async checkout experiment is automated here:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-5-sync-vs-async-checkout.sh
```

The script performs two isolated clean runs against the `Storefront.Api` checkout boundary using the same slow payment workload. It warms each mode separately, captures the immediate user-visible measurements for `storefront-checkout-sync` and `storefront-checkout-async`, snapshots the queue before `Worker` starts, drains the backlog with `Worker`, then analyzes the drained state so the moved background cost stays explicit.

Generated artifacts live in:

- `docs/experiments/milestone-5-sync-vs-async-checkout/artifacts/sync/`
- `docs/experiments/milestone-5-sync-vs-async-checkout/artifacts/async/`

The comparative write-up lives at:

- `docs/experiments/milestone-5-sync-vs-async-checkout/report.md`

## Milestone 6 Experiment

The no-limit vs rate-limit overload experiment is automated here:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-6-no-limit-vs-rate-limit-overload.sh
```

The script performs two isolated clean runs against the same synchronous Storefront checkout boundary. It keeps the topology fixed, intentionally hammers one hot user partition, runs once with checkout rate limiting disabled, runs once with the custom token bucket enabled, then analyzes both:

- `storefront-checkout-sync` for the user-visible boundary
- `payment-authorize` for downstream pressure

Generated artifacts live in:

- `docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/artifacts/no-limit/`
- `docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/artifacts/rate-limit/`
- `docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/artifacts/comparison.json`

The comparative write-up lives at:

- `docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/report.md`

## Milestone 7 Experiment

The one-instance vs two-instance scaling experiment is automated here:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-7-one-instance-vs-two-instance-scaling.sh
```

The script performs two different scaling comparisons behind the same local reverse proxy:

- a constrained frontend CPU case where adding a second `Storefront.Api` instance helps
- a shared synchronous checkout case where adding a second `Storefront.Api` instance does not materially help because the expensive work stays in the shared downstream path

Generated artifacts live in:

- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/frontend-cpu/one-instance/`
- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/frontend-cpu/two-instance/`
- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/shared-checkout/one-instance/`
- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/shared-checkout/two-instance/`
- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/artifacts/comparison.json`

The comparative write-up lives at:

- `docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/report.md`

## Milestone 8 Experiment

The primary-vs-replica / read-model experiment is automated here:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-8-primary-vs-replica-read-model.sh
```

The script performs two read-side comparisons:

- product reads from `primary` vs `replica-east` with Storefront and Catalog caches disabled so the measured change comes from read source selection
- order-history reads from `primary-projection` vs `read-model` during an intentionally created stale window with one pending projection job

Generated artifacts live in:

- `docs/experiments/milestone-8-primary-vs-replica-read-model/artifacts/product-reads/`
- `docs/experiments/milestone-8-primary-vs-replica-read-model/artifacts/order-history/`
- `docs/experiments/milestone-8-primary-vs-replica-read-model/artifacts/comparison.json`

The comparative write-up lives at:

- `docs/experiments/milestone-8-primary-vs-replica-read-model/report.md`

## Milestone 9 Experiment

The same-region vs cross-region dependency experiment is automated here:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-9-same-region-vs-cross-region.sh
```

The script performs three isolated product-read comparisons against the same
Storefront product-page boundary with `cache=on&readSource=local`:

- east caller -> east Storefront/Catalog stack
- west caller -> west local Storefront/Catalog stack
- west caller -> forced east dependency

The measured product mix is chosen so each arm pays the same first-miss shape
and then converges to the same cache hit rate, which keeps the geography penalty
visible instead of hiding it behind different cache warmth.

Generated artifacts live in:

- `docs/experiments/milestone-9-same-region-vs-cross-region/artifacts/east-local/`
- `docs/experiments/milestone-9-same-region-vs-cross-region/artifacts/west-local/`
- `docs/experiments/milestone-9-same-region-vs-cross-region/artifacts/west-forced-east/`
- `docs/experiments/milestone-9-same-region-vs-cross-region/artifacts/comparison.json`

The comparative write-up lives at:

- `docs/experiments/milestone-9-same-region-vs-cross-region/report.md`

## Milestone 9 Degraded-Mode Experiment

The degraded-mode / failover experiment is automated here:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-9-degraded-mode-failover.sh
```

The script performs three west-side product-read comparisons against the same
Storefront product-page boundary:

- healthy west-local reads through west Catalog and west replica
- local replica unavailable, where Catalog falls back from the west replica to the east primary
- local catalog unavailable, where Storefront reroutes the Catalog dependency to the east failover Catalog

Generated artifacts live in:

- `docs/experiments/milestone-9-degraded-mode-failover/artifacts/healthy-west-local/`
- `docs/experiments/milestone-9-degraded-mode-failover/artifacts/local-replica-unavailable/`
- `docs/experiments/milestone-9-degraded-mode-failover/artifacts/local-catalog-unavailable/`
- `docs/experiments/milestone-9-degraded-mode-failover/artifacts/comparison.json`

The comparative write-up lives at:

- `docs/experiments/milestone-9-degraded-mode-failover/report.md`
