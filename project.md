# E-Commerce Systems Lab — Codex Implementation Backlog (.NET 10)

## 1. Purpose

This document turns the e-commerce systems lab blueprint into an implementation backlog for a coding agent such as Codex.

The goal is **not** to build a production storefront. The goal is to build a **local, measurable teaching system** where every architectural pattern has to earn itself in the metrics.

That means the codebase must make these things visible:

- contract correctness
- observation boundaries
- end-to-end latency
- stage-by-stage latency
- throughput
- concurrency
- queue delay
- cache behavior
- replica behavior
- rate limiting
- load balancing
- region-local vs cross-region cost
- degraded mode and backlog growth

## 2. Non-negotiable rules for Codex

Codex must obey these rules for every ticket.

### 2.1 Build rules

- Target **.NET 10**.
- Use **ASP.NET Core** for HTTP services.
- Use **Entity Framework Core with SQLite** for persisted state.
- Use **built-in dependency injection**, routing, middleware, `HttpClientFactory`, options, exception handling, and `ProblemDetails`.
- Use **built-in logging abstractions** for operational logs.
- Use **local JSONL files** for measurable telemetry.
- Use **MSTest** for test projects so the lab remains aligned with the executable-curriculum style already used in the surrounding repo.

### 2.2 Things we are allowed to simulate ourselves and must not outsource

Do **not** introduce external infrastructure or libraries that hide the mechanism we are trying to teach.

For v1, do **not** use these as substitutes for the concepts we want to expose:

- Redis
- Kafka
- RabbitMQ
- MassTransit
- Hangfire
- Quartz
- YARP
- Serilog
- Polly
- OpenTelemetry SDK
- Elasticsearch
- MongoDB
- PostgreSQL
- Docker-only dependencies
- any hosted cloud service

Instead, implement simplified local versions for the concepts that matter:

- payment provider simulator
- cache abstraction and in-memory implementation
- durable queue
- denormalized read model / “NoSQL-like” projection store
- rate limiter
- reverse proxy / load balancer
- read replica simulation
- region latency injection
- load generator
- analyzer / report generator

### 2.3 Measurement rules

Every endpoint and background job must make the following explicit:

- **what contract it promises**
- **where the observation boundary starts**
- **where the observation boundary ends**
- **which named stages were on the path**
- **whether the contract was satisfied**
- **what work happened after the user-visible boundary**

Do not optimize before these are explicit.

### 2.4 Change-management rules for Codex

For every ticket:

1. Read only the current ticket and its dependencies.
2. Do not implement future tickets “while you are here”.
3. Keep commits scoped to the ticket.
4. Add or update tests for the ticket.
5. Run build and tests locally before declaring the ticket done.
6. Update docs when public behavior changes.
7. Prefer the smallest implementation that makes the system design concept measurable.

## 3. What the final system must contain

## 3.1 Application processes

- `Storefront.Api`
- `Catalog.Api`
- `Cart.Api`
- `Order.Api`
- `PaymentSimulator.Api`
- `Worker`
- `Proxy`

## 3.2 Support processes / tools

- `SeedData`
- `LoadGen`
- `Analyze`

## 3.3 Local data files

- `data/primary.db`
- `data/replica-east.db`
- `data/replica-west.db`
- `data/readmodels.db`

## 3.4 Required log / analysis outputs

- `logs/requests.jsonl`  
  Written by `Storefront.Api` only. This is the user-visible observation boundary.
- `logs/jobs.jsonl`  
  Written by `Worker` only.
- `logs/runs/{runId}/summary.json`
- `analysis/{runId}/report.md`

Operational service logs may live under:

- `logs/storefront.log`
- `logs/catalog.log`
- `logs/cart.log`
- `logs/order.log`
- `logs/payment-simulator.log`
- `logs/proxy.log`
- `logs/worker.log`

## 4. Required repo shape

Use this structure unless a later ticket explicitly changes it.

```text
ecommerce-systems-lab/
  Directory.Build.props
  global.json
  ecommerce-systems-lab.sln

  src/
    Lab.Shared/
    Lab.Persistence/
    Lab.Telemetry/
    Lab.Analysis/

    Storefront.Api/
    Catalog.Api/
    Cart.Api/
    Order.Api/
    PaymentSimulator.Api/
    Worker/
    Proxy/

    SeedData/
    LoadGen/
    Analyze/

  tests/
    Lab.UnitTests/
    Lab.IntegrationTests/

  data/
  logs/
  analysis/
  docs/
    architecture/
    experiments/
    tickets/
```

## 5. Shared architecture decisions Codex should not reinvent

These decisions are already made. Codex should implement them, not debate them.

### 5.1 Source of truth

Use one authoritative SQLite database: `data/primary.db`.

It contains the authoritative tables for:

- users
- products
- inventory
- carts
- cart items
- orders
- order items
- payments
- queue jobs

Enable SQLite WAL mode and a reasonable busy timeout to make the local multi-process setup usable.

### 5.2 Read replicas

Do not attempt multi-primary.

Replicas are simulated by copying or replaying changes from `primary.db` into:

- `replica-east.db`
- `replica-west.db`

Use configurable artificial lag to create stale-read experiments.

### 5.3 Read model / NoSQL simulation

Do not add a real NoSQL database.

Use a separate SQLite store, `readmodels.db`, with denormalized tables such as:

- `ReadModel_ProductPage`
- `ReadModel_OrderHistory`

The point is to demonstrate:

- write model vs read model
- denormalization
- eventual consistency
- projection rebuilds
- stale-read windows

### 5.4 Cache

Implement a local in-memory cache abstraction yourself.

Required operations:

- `TryGet`
- `Set`
- `Invalidate`
- `Expire`
- metrics hooks for hit/miss/ttl

Do not use `IMemoryCache` as the teaching abstraction. You may wrap it internally later, but the public behavior must remain our own and measurable.

### 5.5 Queue

Implement a durable queue yourself.

For v1, use a SQLite table-backed queue with explicit fields for:

- enqueue time
- dequeue time
- completion time
- retry count
- status
- lease / lock information
- next-attempt time

The queue must expose backlog and queue delay directly.

### 5.6 Reverse proxy / load balancer

Do not use YARP.

Build a small ASP.NET Core reverse proxy that:

- forwards requests
- chooses a backend instance with round robin
- optionally uses sticky routing based on a cookie or header
- logs routing decisions

### 5.7 Rate limiting

Do not use the built-in ASP.NET rate limiter for the core teaching path.

Implement a simple algorithm yourself. Use either:

- token bucket, or
- sliding window counter

Prefer token bucket for the first version because it maps more directly to the intended overload experiments.

### 5.8 Timing discipline

Use two notions of time:

- **wall time** for timestamps in logs (`DateTimeOffset.UtcNow` via injected clock abstraction or `TimeProvider`)
- **monotonic elapsed time** for durations (`Stopwatch` / `Stopwatch.GetTimestamp()`)

Never compute request duration by subtracting wall-clock timestamps.

## 6. Required shared contracts

Codex should create these shared contracts very early.

### 6.1 Request trace record

Create a telemetry DTO for `logs/requests.jsonl`.

Required fields:

- `runId`
- `traceId`
- `spanId`
- `requestId`
- `operation`
- `region`
- `service`
- `route`
- `method`
- `arrivalUtc`
- `startUtc`
- `completionUtc`
- `latencyMs`
- `statusCode`
- `contractSatisfied`
- `cacheHit`
- `rateLimited`
- `dependencyCalls`
- `stageTimings`
- `errorCode`
- `userId`
- `correlationId`
- `notes`

### 6.2 Job trace record

Create a telemetry DTO for `logs/jobs.jsonl`.

Required fields:

- `runId`
- `traceId`
- `jobId`
- `jobType`
- `region`
- `service`
- `status`
- `enqueuedUtc`
- `dequeuedUtc`
- `executionStartUtc`
- `executionEndUtc`
- `queueDelayMs`
- `executionMs`
- `retryCount`
- `contractSatisfied`
- `dependencyCalls`
- `stageTimings`
- `errorCode`
- `notes`

### 6.3 Stage timing record

Create a DTO like:

- `stageName`
- `startedUtc`
- `endedUtc`
- `elapsedMs`
- `outcome`
- `metadata`

### 6.4 Dependency call record

Create a DTO like:

- `dependencyName`
- `route`
- `region`
- `startedUtc`
- `endedUtc`
- `elapsedMs`
- `statusCode`
- `outcome`
- `notes`

### 6.5 Operation contract descriptor

Create a small code-level way to define the contract for each operation.

Minimum fields:

- `operationName`
- `inputs`
- `preconditions`
- `postconditions`
- `invariants`
- `observationStart`
- `observationEnd`

Do not overengineer this. A small immutable model is enough.

## 7. Required domain tables

Codex should implement these as EF Core entities and migrations.

### 7.1 Core tables

`Products`

- `ProductId`
- `Name`
- `Description`
- `Price`
- `Category`
- `Version`

`Inventory`

- `ProductId`
- `AvailableQuantity`
- `ReservedQuantity`
- `LastUpdatedUtc`

`Carts`

- `CartId`
- `UserId`
- `UpdatedUtc`

`CartItems`

- `CartId`
- `ProductId`
- `Quantity`
- `UnitPriceSnapshot`

`Orders`

- `OrderId`
- `UserId`
- `Status`
- `CreatedUtc`
- `TotalAmount`

`OrderItems`

- `OrderId`
- `ProductId`
- `Quantity`
- `UnitPrice`

`Payments`

- `PaymentId`
- `OrderId`
- `IdempotencyKey`
- `Status`
- `AttemptCount`
- `ProviderReference`
- `LastAttemptUtc`

`QueueJobs`

- `JobId`
- `JobType`
- `Payload`
- `EnqueuedUtc`
- `DequeuedUtc`
- `CompletedUtc`
- `Status`
- `RetryCount`

### 7.2 Read-model tables

`ReadModel_ProductPage`

- `ProductId`
- `Region`
- `ProjectionVersion`
- `SummaryJson`
- `ProjectedUtc`

`ReadModel_OrderHistory`

- `UserId`
- `OrderId`
- `Status`
- `SummaryJson`
- `ProjectionVersion`
- `ProjectedUtc`

### 7.3 Helpful additions that are worth adding up front

Add these fields even if the original blueprint did not spell them out, because they make the simulations workable:

`QueueJobs`

- `LeaseOwner`
- `LeaseExpiresUtc`
- `NextAttemptUtc`
- `ErrorCode`
- `CorrelationId`

`Payments`

- `FinalizedUtc`
- `LastErrorCode`
- `ModeSnapshot`

`Orders`

- `StatusReason`
- `LastUpdatedUtc`

## 8. Ticket execution template for Codex

Use this template for every ticket run.

```text
Implement ticket LAB-XYZ only.

Constraints:
- Stay within the ticket scope.
- Do not implement future tickets.
- Use .NET 10, ASP.NET Core, EF Core SQLite, MSTest.
- Do not add Redis/Kafka/YARP/Polly/OpenTelemetry/Serilog/MassTransit.
- Keep all system-design mechanisms explicit and local.
- Add or update tests.
- Run build and tests.
- Summarize files changed, commands run, and any follow-up risks.
```

---

# 9. Backlog by milestone

## Milestone 0 — Foundations

### LAB-001 — Scaffold the solution and project graph

**Depends on:** none

**Goal:** Create the repo, solution, project skeleton, references, and base build settings.

**Implementation steps**

1. Create the solution file.
2. Create the projects listed in section 4.
3. Add project references so shared libraries do not depend on concrete hosts.
4. Add `Directory.Build.props` with common target framework, nullable enabled, implicit usings enabled, warnings as errors optional but recommended.
5. Add `global.json` pinned to the intended SDK.
6. Add empty `data/`, `logs/`, `analysis/`, and `docs/` directories.
7. Add a root `README.md` with commands to build and run.

**Acceptance criteria**

- `dotnet build` succeeds.
- The solution contains all required projects.
- The repo layout matches section 4.
- No external infrastructure package is introduced.

**Verification**

- `dotnet build`
- `dotnet sln list`

---

### LAB-002 — Create shared configuration, options, and environment model

**Depends on:** LAB-001

**Goal:** Standardize configuration across all services.

**Implementation steps**

1. In `Lab.Shared`, create options types for:
   - database paths
   - log paths
   - region settings
   - cache settings
   - queue settings
   - payment simulator settings
   - rate limiter settings
2. Create a single configuration extension method to bind these options.
3. Create `EnvironmentLayout` helpers that resolve:
   - primary DB path
   - replica DB paths
   - read-model DB path
   - log paths
   - analysis paths
4. Make sure all paths are relative to the repo root and can be overridden by configuration.
5. Add environment-specific JSON files only if really needed; prefer one clear defaults file per host.

**Acceptance criteria**

- Every host can resolve paths without hardcoding absolute machine-local paths.
- All services share the same option model style.
- A clean machine can run the solution without manual path edits.

**Verification**

- `dotnet build`
- boot each host and inspect startup logs for resolved paths

---

### LAB-003 — Implement operation contracts and request tracing primitives

**Depends on:** LAB-001, LAB-002

**Goal:** Make contract, observation boundary, and stage timing first-class.

**Implementation steps**

1. Create `OperationContractDescriptor`.
2. Create `RequestTraceRecord`, `JobTraceRecord`, `StageTimingRecord`, and `DependencyCallRecord`.
3. Create a per-request trace context object that can:
   - register stages
   - register dependency calls
   - mark `contractSatisfied`
   - mark `cacheHit`
   - mark `rateLimited`
4. Create helpers to define the initial four business contracts:
   - product page
   - add item to cart
   - checkout sync
   - order history
5. Expose a simple pattern for endpoints to:
   - begin trace
   - add stage timings
   - finalize trace on response

**Acceptance criteria**

- Shared telemetry types compile and are host-agnostic.
- Each business flow has an explicit contract descriptor in code.
- A request can accumulate stage timings and dependency calls without direct file I/O from the handler.

**Verification**

- unit tests for trace context
- unit tests for contract descriptor creation

---

### LAB-004 — Add JSONL telemetry writers and file-logging plumbing

**Depends on:** LAB-003

**Goal:** Persist measurable telemetry to local JSONL while keeping ordinary operational logs separate.

**Implementation steps**

1. In `Lab.Telemetry`, create:
   - `IRequestTraceWriter`
   - `IJobTraceWriter`
   - JSONL file-based implementations
2. Ensure `Storefront.Api` is the only process that writes to `logs/requests.jsonl`.
3. Ensure `Worker` is the only process that writes to `logs/jobs.jsonl`.
4. Add safe append behavior.
5. Add operational log setup for each host using the built-in logging abstraction.
6. Make sure telemetry writing failures do not crash the business request, but are logged loudly.

**Acceptance criteria**

- `logs/requests.jsonl` receives one valid JSON record per completed storefront request.
- `logs/jobs.jsonl` receives one valid JSON record per completed worker job.
- Operational logs still go to service-specific files.
- JSONL lines are machine-parseable and one-line each.

**Verification**

- smoke request to a dummy endpoint
- inspect both JSONL files
- run a parser unit test that reads them back

---

### LAB-005 — Create the analysis CLI skeleton

**Depends on:** LAB-004

**Goal:** Build the first analysis tool that turns telemetry into reportable metrics.

**Implementation steps**

1. Create `Analyze` console app.
2. Add ability to read `requests.jsonl` and `jobs.jsonl`.
3. Compute:
   - average latency
   - p50
   - p95
   - p99
   - throughput
   - average concurrency using exact accounting
4. Output:
   - JSON summary to `logs/runs/{runId}/summary.json`
   - markdown report to `analysis/{runId}/report.md`
5. Add a `--run-id` filter and a `--from/--to` time window option.

**Acceptance criteria**

- Analyzer works on a small sample JSONL file.
- Concurrency is computed from request lifetimes, not guessed from thread counts.
- The markdown report includes both raw numbers and a short interpretation section.

**Verification**

- analyzer unit tests with synthetic sample traces
- manual run against a sample file

---

### LAB-006 — Create seed-data and load-generator tools

**Depends on:** LAB-001, LAB-002

**Goal:** Provide the project with repeatable data seeding and controlled load generation.

**Implementation steps**

1. Create `SeedData` console app that can seed:
   - products
   - inventory
   - users
2. Create `LoadGen` console app with configurable:
   - target URL
   - HTTP method
   - RPS
   - duration
   - concurrency cap
   - headers
   - payload file
   - run ID
3. Make `LoadGen` stamp:
   - `X-Run-Id`
   - `X-Correlation-Id`
4. Add result summary output at the client side for sanity, but keep the server telemetry as the source of truth.
5. Add at least two workload modes:
   - constant RPS
   - burst mode

**Acceptance criteria**

- Seed tool can populate the primary DB.
- LoadGen can drive any endpoint in a reproducible way.
- Run IDs are visible in server telemetry.

**Verification**

- seed the DB
- run a small load against a sample endpoint
- verify traces include the run ID

---

## Milestone 1 — Single-service metrics lab

### LAB-010 — Build the first Storefront host with health endpoint and shared middleware

**Depends on:** LAB-003, LAB-004

**Goal:** Bring up a minimal `Storefront.Api` host.

**Implementation steps**

1. Build `Storefront.Api` as an ASP.NET Core minimal API.
2. Add:
   - exception handling
   - `ProblemDetails`
   - trace ID / request ID propagation
   - telemetry context middleware
3. Implement:
   - `GET /health`
4. Make every request produce a request trace record, even if it is just for health for now.

**Acceptance criteria**

- Storefront boots cleanly.
- `/health` returns 200.
- A request to `/health` creates a valid request trace.

**Verification**

- `dotnet run --project src/Storefront.Api`
- `curl http://localhost:.../health`

---

### LAB-011 — Add CPU-bound lab endpoint

**Depends on:** LAB-010

**Goal:** Create a controlled CPU-heavy endpoint for early experiments.

**Implementation steps**

1. Implement `GET /cpu`.
2. Support query parameters such as:
   - `workFactor`
   - `iterations`
3. Ensure the endpoint does real CPU work, not just `Task.Delay`.
4. Emit stage timings:
   - request_received
   - cpu_work_started
   - cpu_work_completed
   - response_sent
5. Return a small JSON body with the selected parameters and a checksum result so the work cannot be optimized away trivially.

**Acceptance criteria**

- `/cpu` latency increases materially when `workFactor` increases.
- The request trace exposes stage timings.
- The handler remains deterministic for the same input.

**Verification**

- manual requests with low and high `workFactor`
- load run and compare latency

---

### LAB-012 — Add I/O-bound lab endpoint

**Depends on:** LAB-010

**Goal:** Create a controlled I/O-shaped endpoint.

**Implementation steps**

1. Implement `GET /io`.
2. Support query parameters such as:
   - `delayMs`
   - `jitterMs`
3. The endpoint should simulate downstream wait rather than CPU burn.
4. Emit stage timings:
   - request_received
   - downstream_wait_started
   - downstream_wait_completed
   - response_sent
5. Keep CPU cost intentionally low.

**Acceptance criteria**

- `/io` latency tracks configured delay.
- Stage timings make the downstream wait dominant.
- The endpoint is suitable for the CPU-vs-I/O experiment.

**Verification**

- manual calls with different delay values
- compare CPU use under `/cpu` and `/io`

---

### LAB-013 — Harden the request telemetry pipeline for experiment use

**Depends on:** LAB-011, LAB-012

**Goal:** Make milestone-1 traces analytically trustworthy.

**Implementation steps**

1. Ensure every request trace records:
   - route
   - method
   - status code
   - stage timings
   - contract satisfaction
2. Add middleware support for:
   - `runId`
   - `correlationId`
   - `region` default
3. Make sure exceptions finalize the trace with an error status.
4. Add a small test helper to read back traces and assert required fields are present.

**Acceptance criteria**

- `/cpu` and `/io` traces are well-formed under success and error paths.
- Analyzer can consume milestone-1 traces without special cases.
- `contractSatisfied` is not omitted.

**Verification**

- integration tests
- manual malformed request case

---

### LAB-014 — Create milestone-1 scripts and first comparative report

**Depends on:** LAB-013, LAB-005, LAB-006

**Goal:** Produce the first real experiment.

**Implementation steps**

1. Add scripts or documented commands for:
   - CPU workload run
   - I/O workload run
2. Generate two runs with different run IDs.
3. Run the analyzer for both.
4. Create a milestone-1 report under `docs/experiments/`.
5. Use the standard report structure:
   - contract
   - observation boundary
   - topology
   - workload
   - results
   - interpretation
   - architectural justification

**Acceptance criteria**

- There is a reproducible side-by-side CPU-vs-I/O report.
- The report includes latency, throughput, and concurrency.
- The report explains why similar throughput can hide different dominant stages.

**Verification**

- complete the experiment end to end on a clean run

---

## Milestone 2 — Product read path and cache

### LAB-020 — Implement primary database schema and migrations

**Depends on:** LAB-001, LAB-002

**Goal:** Create the authoritative persistence layer.

**Implementation steps**

1. Create `Lab.Persistence`.
2. Add `PrimaryDbContext`.
3. Implement entities for:
   - products
   - inventory
   - carts
   - cart items
   - orders
   - order items
   - payments
   - queue jobs
4. Configure SQLite with WAL mode and a busy timeout.
5. Add migrations.
6. Add a DB initializer that can create the schema on first run.

**Acceptance criteria**

- `primary.db` is created automatically when needed.
- EF migrations apply cleanly.
- SeedData can insert product and inventory rows.

**Verification**

- run migrations
- inspect DB schema with a SQLite browser or CLI

---

### LAB-021 — Build Catalog service and product query path

**Depends on:** LAB-020

**Goal:** Create `Catalog.Api` for product details, price, and stock display.

**Implementation steps**

1. Build `Catalog.Api`.
2. Add `GET /catalog/products/{id}`.
3. Query:
   - product
   - inventory summary
4. Return a product detail DTO with:
   - product data
   - price
   - stock status
   - version
5. Add internal stage timings for:
   - request_received
   - db_query_started
   - db_query_completed
   - response_sent
6. Add optional `X-Debug-Telemetry: true` support to include downstream stage metadata in a header or response field.

**Acceptance criteria**

- Product endpoint returns all required fields.
- Not-found case is explicit and traceable.
- Internal timing data is available when debug telemetry is requested.

**Verification**

- integration tests against seeded products
- manual debug-telemetry request

---

### LAB-022 — Implement the cache abstraction and in-memory cache store

**Depends on:** LAB-003

**Goal:** Build a measurable cache layer for product reads.

**Implementation steps**

1. Create `ICacheStore`.
2. Implement a TTL-aware in-memory cache with:
   - get
   - set
   - invalidate
   - expire
3. Track metrics:
   - hit count
   - miss count
   - average hit lookup time
   - average miss lookup time
4. Add support for region-scoped cache namespaces because milestone 9 will need them.
5. Add tests for TTL expiration and invalidation.

**Acceptance criteria**

- Cache behavior is explicit and test-covered.
- TTL and invalidation work.
- Metrics can be queried by the analyzer or a simple in-memory snapshot service.

**Verification**

- unit tests
- manual hit/miss sequence

---

### LAB-023 — Add Storefront product endpoint with cache toggle

**Depends on:** LAB-021, LAB-022

**Goal:** Make `Storefront.Api` the user-facing observation boundary for product reads.

**Implementation steps**

1. Implement `GET /products/{id}` in `Storefront.Api`.
2. The endpoint must call `Catalog.Api`, not the DB directly.
3. Add `?cache=on|off`.
4. When cache is on:
   - check cache
   - return cached result if hit
   - otherwise call `Catalog.Api`, then cache
5. Emit top-level stages:
   - request_received
   - cache_lookup
   - catalog_call_started
   - catalog_call_completed
   - response_sent
6. Record `cacheHit` in the request trace.

**Acceptance criteria**

- Product reads work end to end through Storefront.
- Cache on/off changes the path shape.
- Request traces reflect hit vs miss correctly.

**Verification**

- repeated manual reads of the same product
- inspect `requests.jsonl`

---

### LAB-024 — Add product page projection store for read-model experiments

**Depends on:** LAB-020

**Goal:** Introduce the denormalized read model for later experiments.

**Implementation steps**

1. Create `ReadModelDbContext` for `readmodels.db`.
2. Add `ReadModel_ProductPage`.
3. Create a projector component that can build a document-like product summary row from authoritative tables.
4. Add a rebuild command to `SeedData` or a small admin endpoint to populate the projection.
5. Do not switch production reads to the projection yet; just create the store and rebuild path.

**Acceptance criteria**

- `readmodels.db` exists.
- Product page projections can be built from the source of truth.
- Projection rebuild is repeatable.

**Verification**

- rebuild projection
- inspect projected rows

---

### LAB-025 — Produce the cache-off vs cache-on experiment

**Depends on:** LAB-023, LAB-024, LAB-005, LAB-006

**Goal:** Justify caching with real measurements.

**Implementation steps**

1. Create a hot-key workload against a small product set.
2. Run once with `cache=off`.
3. Run once with `cache=on`.
4. Analyze both.
5. Write the report.

**Acceptance criteria**

- The report shows:
   - hit rate
   - miss rate
   - p50/p95/p99
   - throughput difference
- The interpretation explains why caching acts as a capacity multiplier on read-heavy paths.

**Verification**

- full end-to-end experiment

---

## Milestone 3 — Cart and per-user state

### LAB-030 — Build Cart service and cart persistence

**Depends on:** LAB-020

**Goal:** Implement per-user mutable cart state.

**Implementation steps**

1. Build `Cart.Api`.
2. Add endpoints:
   - `POST /cart/items`
   - `DELETE /cart/items`
   - `GET /cart/{userId}`
3. Persist carts and cart items in `primary.db`.
4. Ensure cart items store `UnitPriceSnapshot`.
5. Add stage timings for:
   - request_received
   - cart_loaded_or_created
   - cart_mutated
   - cart_persisted
   - response_sent

**Acceptance criteria**

- Cart endpoints create and mutate cart state correctly.
- Repeated add operations accumulate quantity or follow the chosen rule consistently.
- Price snapshot is stored at add time.

**Verification**

- integration tests for add/remove/list

---

### LAB-031 — Add Storefront cart orchestration

**Depends on:** LAB-030

**Goal:** Expose cart behavior through the Storefront boundary.

**Implementation steps**

1. Implement `POST /cart/items` in `Storefront.Api`.
2. Pass through to `Cart.Api`.
3. Record dependency call timings.
4. Add contract checks:
   - requested item is present in resulting cart
   - quantity is updated as expected
5. Include `userId` in request traces.

**Acceptance criteria**

- Add-to-cart works through Storefront.
- Request traces include cart dependency timing.
- Contract failures are distinguishable from technical failures.

**Verification**

- integration tests
- inspect traces for successful and invalid requests

---

### LAB-032 — Prepare routing experiments for cart state

**Depends on:** LAB-031

**Goal:** Make sticky vs non-sticky routing discussable later.

**Implementation steps**

1. Define a routing key convention:
   - header `X-Session-Key` or
   - cookie `lab-session`
2. Add support in Storefront for emitting the session key on responses when missing.
3. Keep cart state in the DB, not in server memory, but structure the proxy ticket so that sticky routing can still be demonstrated later.
4. Add documentation explaining:
   - why DB-backed cart state makes correctness independent of stickiness
   - why sticky routing can still affect locality and cache behavior

**Acceptance criteria**

- Session key convention exists and is test-covered.
- No in-memory cart ownership is introduced in Storefront.
- The groundwork for sticky-routing experiments is in place.

**Verification**

- integration tests with and without session key

---

## Milestone 4 — Checkout and payment simulator

### LAB-040 — Implement order, inventory, and payment persistence rules

**Depends on:** LAB-020

**Goal:** Prepare the write path for checkout.

**Implementation steps**

1. Finalize entities and mappings for:
   - orders
   - order items
   - payments
   - inventory
2. Add helper methods for inventory reservation.
3. Decide on one reservation strategy for v1:
   - transactionally decrement available and increment reserved
4. Add tests that verify inventory cannot go negative.

**Acceptance criteria**

- Inventory reservation is transactional inside the local DB.
- Orders, order items, and payments can be persisted together where appropriate.
- Negative inventory is prevented.

**Verification**

- unit/integration tests around inventory reservation

---

### LAB-041 — Build Payment simulator service with configurable modes

**Depends on:** LAB-001, LAB-002

**Goal:** Implement a local external-dependency simulator.

**Implementation steps**

1. Build `PaymentSimulator.Api`.
2. Add endpoint:
   - `POST /payments/authorize`
3. Support modes:
   - `fast_success`
   - `slow_success`
   - `timeout`
   - `transient_failure`
   - `duplicate_callback`
   - `delayed_confirmation`
4. Provide a way to set mode:
   - request header
   - query string
   - or admin endpoint
5. Generate provider references.
6. Persist simulator-side state if needed in a small local SQLite file or keep it in memory if enough for v1.
7. For `delayed_confirmation` and `duplicate_callback`, create a simple scheduler/background dispatcher inside the simulator host that can later post callbacks to `Order.Api`.

**Acceptance criteria**

- Each mode has deterministic, testable behavior.
- The simulator can be driven per request.
- The simulator is clearly external to `Order.Api` and accessed over HTTP.

**Verification**

- integration tests per mode
- manual curl tests

---

### LAB-042 — Implement synchronous checkout in Order service

**Depends on:** LAB-040, LAB-041, LAB-030

**Goal:** Build the synchronous end-to-end checkout path.

**Implementation steps**

1. Build `Order.Api`.
2. Add endpoint:
   - `POST /orders/checkout`
3. Flow:
   - load cart
   - validate items
   - reserve inventory
   - create order
   - call payment simulator synchronously
   - update payment/order status
4. Define order statuses:
   - `PendingPayment`
   - `Paid`
   - `Failed`
   - `Cancelled`
5. Return a checkout result DTO with:
   - order ID
   - status
   - payment status
   - total amount

**Acceptance criteria**

- Fast-success payment produces a paid order.
- Timeout or failure produces a failed or pending contract according to the chosen rule.
- The checkout path is not yet async.

**Verification**

- integration tests for fast success and timeout

---

### LAB-043 — Add idempotency to checkout

**Depends on:** LAB-042

**Goal:** Prevent duplicate charges and duplicate orders on retry.

**Implementation steps**

1. Require an idempotency key on `POST /checkout`.
2. Persist the key in `Payments`.
3. On duplicate key:
   - return the original result if available
   - do not reserve inventory again
   - do not create a second payment attempt
4. Add tests for:
   - exact duplicate request
   - same cart, different idempotency key
   - transient failure followed by retry with same key

**Acceptance criteria**

- Same idempotency key is safe to retry.
- Duplicate key does not create duplicate charges.
- The original result is returned consistently.

**Verification**

- integration tests with repeated identical requests

---

### LAB-044 — Add detailed checkout stage timing and dependency tracing

**Depends on:** LAB-042, LAB-043

**Goal:** Make the checkout critical path explainable.

**Implementation steps**

1. Add these stages in `Order.Api`:
   - request_received
   - cart_loaded
   - inventory_reserved
   - payment_request_started
   - payment_request_completed
   - order_persisted
   - response_sent
2. When `Storefront.Api` calls `Order.Api`, request downstream debug telemetry and merge it into the top-level request trace.
3. Record payment dependency call metadata.
4. Surface `contractSatisfied` explicitly:
   - true only if the chosen sync contract was actually met

**Acceptance criteria**

- A single checkout request trace makes dominant stages visible.
- Fast and slow payment modes produce visibly different stage distributions.
- Storefront traces remain the top-level source of truth.

**Verification**

- inspect traces for fast_success vs slow_success

---

### LAB-045 — Produce the synchronous checkout experiment

**Depends on:** LAB-044, LAB-005, LAB-006

**Goal:** Demonstrate dependency latency and critical-path decomposition.

**Implementation steps**

1. Create runs for:
   - fast success
   - slow success
   - timeout
   - transient failure
2. Analyze all runs.
3. Create a report that answers:
   - which stage dominated
   - what contract the endpoint promised
   - what happened when payment got slow

**Acceptance criteria**

- The report clearly shows payment stage dominance.
- The report distinguishes user-visible latency from background work that does not yet exist in the sync flow.
- Timeout behavior is measurable and not hand-waved.

**Verification**

- full experiment report

---

## Milestone 5 — Async jobs and explicit queue

### LAB-050 — Implement the durable queue store

**Depends on:** LAB-020

**Goal:** Build the queue the worker will own.

**Implementation steps**

1. Create a queue abstraction over `QueueJobs`.
2. Implement operations:
   - enqueue
   - claim next available job
   - complete
   - fail
   - reschedule
   - abandon expired lease
3. Use lease fields so two workers cannot process the same job simultaneously under normal operation.
4. Add retry and next-attempt support.
5. Add queue-specific tests.

**Acceptance criteria**

- Jobs can be enqueued, claimed, retried, and completed.
- Queue delay is directly measurable from stored timestamps.
- Lease expiration can recover abandoned jobs.

**Verification**

- unit tests
- integration tests with simulated expired lease

---

### LAB-051 — Build the Worker host

**Depends on:** LAB-050

**Goal:** Introduce explicit asynchronous processing.

**Implementation steps**

1. Build `Worker` as a hosted-service-based console/worker process.
2. Poll the queue for available jobs.
3. Add job handlers for:
   - payment confirmation retry
   - order history projection update
   - product page projection rebuild
4. Emit `JobTraceRecord` for every job.
5. Track queue delay and execution time separately.

**Acceptance criteria**

- Worker processes jobs from the queue.
- Job telemetry is written to `logs/jobs.jsonl`.
- Retry and failure states are visible.

**Verification**

- enqueue sample jobs manually
- inspect job logs

---

### LAB-052 — Add asynchronous checkout mode with pending state

**Depends on:** LAB-051, LAB-041, LAB-044

**Goal:** Split user-visible completion from background payment completion.

**Implementation steps**

1. Add an async checkout mode in `Order.Api`.
2. In async mode:
   - validate cart
   - reserve inventory
   - create order with `PendingPayment`
   - enqueue payment confirmation job
   - return quickly
3. Define a user-visible contract for async mode:
   - “order accepted and pending” is success
4. Add `POST /checkout?mode=sync|async` at `Storefront.Api`.
5. Ensure traces capture the changed observation boundary.

**Acceptance criteria**

- Async checkout returns materially faster than slow synchronous checkout.
- Orders can remain pending while background processing continues.
- The contract difference is explicit in the code and the report.

**Verification**

- compare sync vs async under slow payment mode

---

### LAB-053 — Build order history read model and projection jobs

**Depends on:** LAB-051, LAB-020

**Goal:** Support the order-history read view via a projection.

**Implementation steps**

1. Add `ReadModel_OrderHistory` in `readmodels.db`.
2. Implement a projection builder that writes denormalized order summaries.
3. Enqueue projection rebuild/update jobs after order changes.
4. Add `GET /orders/{userId}` in `Storefront.Api`.
5. Have Storefront read order history from the read model, not the primary write tables.

**Acceptance criteria**

- Order history endpoint returns denormalized summaries.
- Projection lag is possible and measurable.
- The read model can be rebuilt from authoritative order tables.

**Verification**

- integration tests
- projection rebuild test

---

### LAB-054 — Add queue metrics, backlog reporting, and oldest-item tracking

**Depends on:** LAB-050, LAB-051

**Goal:** Make queueing quantitatively visible.

**Implementation steps**

1. Add queue metrics queries that compute:
   - pending count
   - in-progress count
   - completed count
   - oldest queued item age
   - average enqueue-to-dequeue delay
   - average dequeue-to-complete time
2. Extend analyzer to consume `jobs.jsonl`.
3. Include queue sections in the markdown report.
4. Add a stress test that can intentionally build backlog.

**Acceptance criteria**

- Queue delay and backlog are visible in reports.
- The analyzer can explain whether delay came from waiting vs execution.
- Oldest queued item age is reported.

**Verification**

- force a backlog with slow payment confirmation
- inspect report

---

### LAB-055 — Produce the sync-vs-async checkout experiment

**Depends on:** LAB-052, LAB-053, LAB-054

**Goal:** Justify asynchronous processing with measurement, not slogans.

**Implementation steps**

1. Run slow payment workload in sync mode.
2. Run the same workload in async mode.
3. Analyze user-visible latency and background queue growth.
4. Write the report using the standard structure.

**Acceptance criteria**

- The report shows lower user-visible latency in async mode.
- The report also shows queue growth and pending state cost.
- The report explicitly explains the changed contract and observation boundary.

**Verification**

- full experiment report

---

## Milestone 6 — Rate limiting and overload protection

### LAB-060 — Implement a custom token-bucket rate limiter

**Depends on:** LAB-010

**Goal:** Protect selected endpoints under overload.

**Implementation steps**

1. Create a token-bucket implementation keyed by:
   - route + user ID
   - or route + client identifier
2. Add middleware or endpoint filter support.
3. Configure separate policies for:
   - `/checkout`
   - optionally `/login` later
4. On rejection:
   - return 429
   - include `Retry-After`
5. Record `rateLimited = true` in traces.

**Acceptance criteria**

- The algorithm is ours and test-covered.
- Rejections are fast and explicit.
- Token refill behavior is deterministic enough to test.

**Verification**

- unit tests for bucket refill and burst allowance
- integration test returning 429

---

### LAB-061 — Extend telemetry for overload analysis

**Depends on:** LAB-060

**Goal:** Make overload protection measurable.

**Implementation steps**

1. Extend traces or analyzer-derived metrics for:
   - reject rate
   - timeout rate
   - retry count
   - admitted throughput
2. Make sure 429 responses still emit complete traces.
3. Add summary sections for:
   - rate-limited requests
   - admitted requests

**Acceptance criteria**

- Analyzer can compare overload runs with and without the limiter.
- Rejected work is distinguishable from slow admitted work.
- Admitted p95 is reportable.

**Verification**

- synthetic mixed success/reject dataset in analyzer tests

---

### LAB-062 — Produce the no-limit vs rate-limit overload experiment

**Depends on:** LAB-061, LAB-006, LAB-005

**Goal:** Justify backpressure and admission control.

**Implementation steps**

1. Hammer checkout with a workload above sustainable capacity.
2. Run once without rate limiting.
3. Run once with rate limiting.
4. Compare:
   - admitted throughput
   - p95 latency
   - reject rate
   - downstream payment failure rate if applicable

**Acceptance criteria**

- The report shows why protecting the system can improve admitted-request latency.
- The report does not pretend reject rate is free.
- The interpretation discusses backpressure, not “magical faster system” claims.

**Verification**

- full experiment report

---

## Milestone 7 — Horizontal scale and reverse proxy

### LAB-070 — Build the local reverse proxy with round-robin routing

**Depends on:** LAB-001, LAB-002

**Goal:** Add a local reverse proxy that can balance traffic across multiple instances.

**Implementation steps**

1. Build `Proxy`.
2. Configure backend lists for:
   - Storefront instances
   - optionally Catalog instances
3. Implement simple round-robin routing.
4. Forward method, path, query string, body, and selected headers.
5. Log the chosen backend per request.

**Acceptance criteria**

- Proxy forwards requests correctly.
- Round robin distributes requests across multiple instances.
- Proxy remains simple and explicit, not framework-hidden.

**Verification**

- run two Storefront instances on different ports
- send requests through Proxy
- inspect proxy logs

---

### LAB-071 — Add sticky routing option for cart experiments

**Depends on:** LAB-070, LAB-032

**Goal:** Support stickiness as a controllable routing mode.

**Implementation steps**

1. Add sticky mode based on:
   - session cookie, or
   - `X-Session-Key`
2. Persist proxy-side sticky assignments in memory.
3. Fall back cleanly if a backend disappears.
4. Make the mode configurable:
   - `round_robin`
   - `sticky`
5. Record routing mode and chosen backend in proxy logs.

**Acceptance criteria**

- Same session key maps to the same backend while healthy.
- Backend failure clears or remaps stickiness safely.
- The system still works because cart truth remains in the DB.

**Verification**

- repeated requests with same session key
- kill one backend and verify remap

---

### LAB-072 — Produce the one-instance vs two-instance scaling experiment

**Depends on:** LAB-070, LAB-071, LAB-006, LAB-005

**Goal:** Justify load balancing and show bottleneck relocation.

**Implementation steps**

1. Run one Storefront instance behind Proxy.
2. Run two Storefront instances behind Proxy.
3. Use the same workload.
4. Compare:
   - throughput
   - latency
   - where the bottleneck moved
5. If possible, add a second run that demonstrates no benefit when the downstream bottleneck remains unchanged.

**Acceptance criteria**

- The report shows when horizontal scale helps.
- The report also shows when it does not help because a shared downstream bottleneck remains.
- The report uses measurements, not generic claims.

**Verification**

- full experiment report

---

## Milestone 8 — Read replicas and denormalized reads

### LAB-080 — Implement replica simulation and lagged copy/replay

**Depends on:** LAB-020

**Goal:** Create measurable read replicas.

**Implementation steps**

1. Create a replica-sync component that copies or replays primary data into:
   - `replica-east.db`
   - `replica-west.db`
2. Make lag configurable.
3. Choose one simple replication model for v1:
   - periodic full-table copy for selected tables, or
   - append-only change log replay
4. Record replica sync timing and lag metrics in operational logs.

**Acceptance criteria**

- Replicas can be refreshed from primary.
- Replica freshness can intentionally lag.
- The mechanism is simple enough to explain.

**Verification**

- modify primary data and observe delayed visibility in replica

---

### LAB-081 — Add read-from-replica mode for catalog and order-history reads

**Depends on:** LAB-080, LAB-021, LAB-053

**Goal:** Route read-heavy endpoints to replicas / read models.

**Implementation steps**

1. Add mode flags so Storefront can read:
   - product detail from primary or region replica
   - order history from primary-like projection or lagged read model
2. Include the chosen read source in traces.
3. Keep writes authoritative on primary only.

**Acceptance criteria**

- Reads can switch between primary and replica modes.
- The chosen source is visible in telemetry.
- No write path goes to replicas.

**Verification**

- integration tests with forced mode selection

---

### LAB-082 — Add stale-read detection and replica metrics

**Depends on:** LAB-081

**Goal:** Make freshness tradeoffs measurable.

**Implementation steps**

1. Add version/timestamp fields to compare primary and replica visibility.
2. Compute stale-read incidence in analyzer.
3. Report:
   - reads per replica
   - latency by replica
   - stale-read count or fraction
4. Add a test that intentionally demonstrates a stale read after a primary write but before replica catch-up.

**Acceptance criteria**

- Freshness lag is explicit.
- The analyzer can quantify stale-read incidence.
- Reports can justify replication as a read-scaling tool with a freshness tradeoff.

**Verification**

- controlled stale-read scenario

---

### LAB-083 — Produce the primary-vs-replica / read-model experiment

**Depends on:** LAB-082, LAB-006, LAB-005

**Goal:** Justify read scaling and denormalized views.

**Implementation steps**

1. Run read-heavy product workload against primary.
2. Run the same workload against replicas/cache/read-model mode.
3. Compare:
   - primary load reduction
   - latency
   - throughput
   - stale-read incidence

**Acceptance criteria**

- The report shows read-scaling benefit and freshness cost.
- The read model is explained as a denormalized projection, not just “another DB”.
- The report remains honest about stale windows.

**Verification**

- full experiment report

---

## Milestone 9 — Region simulation

### LAB-090 — Implement region model and latency injection

**Depends on:** LAB-002

**Goal:** Simulate east/west topology.

**Implementation steps**

1. Define two regions:
   - `us-east`
   - `us-west`
2. Add region identity to each service instance.
3. Implement latency injection for service-to-service calls:
   - same-region delay
   - cross-region delay
4. Use a delegating handler or explicit call wrapper in `HttpClientFactory`.
5. Log effective call region and injected delay on dependency calls.

**Acceptance criteria**

- The same logical dependency call can run under different network envelopes based on region.
- Injected delay is visible in traces.
- The mechanism is centrally configurable.

**Verification**

- unit tests for same-region vs cross-region delay
- manual requests with east/west topology

---

### LAB-091 — Add per-region cache and region-aware read selection

**Depends on:** LAB-090, LAB-022, LAB-081

**Goal:** Model why local caches and local replicas matter.

**Implementation steps**

1. Scope caches by region.
2. Prefer same-region replica/read-model reads when available.
3. Keep writes authoritative in the primary region.
4. Add fallback behavior when local read source is unavailable:
   - cross-region read
   - degraded mode
5. Record same-region vs cross-region in traces.

**Acceptance criteria**

- Regions can have different cache hit rates.
- Same-region reads are faster than cross-region reads under configured delay.
- Fallback behavior remains explicit.

**Verification**

- manual and integration tests under both regions

---

### LAB-092 — Produce same-region vs cross-region experiments

**Depends on:** LAB-091, LAB-006, LAB-005

**Goal:** Justify regional deployment and local reads.

**Implementation steps**

1. Run east client -> east stack.
2. Run west client -> west local cache/replica.
3. Run west client -> forced east dependency.
4. Compare:
   - latency
   - cache hit rate by region
   - dependency stage dominance

**Acceptance criteria**

- The report makes the geography penalty visible.
- The report shows why local cache and local replica matter.
- The report does not claim multi-region is “free”.

**Verification**

- full experiment report

---

### LAB-093 — Add degraded-mode / failover scenario for regional reasoning

**Depends on:** LAB-091

**Goal:** Make regional degradation discussable, even if full failover is not built.

**Implementation steps**

1. Add a switch to simulate:
   - local replica unavailable
   - local catalog unavailable
2. In degraded mode, allow:
   - cross-region fallback for reads
   - slower but available path
3. Add clear trace notes marking degraded behavior.
4. Produce one small report showing failover/degraded-mode reasoning.

**Acceptance criteria**

- A local-region dependency outage does not necessarily mean total failure.
- The path becomes slower and that is measurable.
- The report explains degraded mode honestly.

**Verification**

- simulated outage run

---

## Cross-cutting completion tickets

### LAB-100 — Build the end-to-end test suite

**Depends on:** milestones 0–9 incrementally

**Goal:** Keep the lab regression-safe.

**Implementation steps**

1. Create `Lab.UnitTests`.
2. Create `Lab.IntegrationTests`.
3. Cover:
   - cache TTL/invalidation
   - queue claim/retry/lease
   - idempotency
   - inventory non-negative guarantee
   - payment modes
   - rate limiter
   - proxy routing
   - replica lag
   - analyzer calculations
4. Add API integration tests using ASP.NET test hosting where practical.
5. Keep tests deterministic wherever possible.

**Acceptance criteria**

- Core mechanisms are test-covered.
- Deterministic tests exist for queueing, idempotency, and analyzer math.
- Tests do not require external infrastructure.

**Verification**

- `dotnet test`

---

### LAB-101 — Add standard experiment report templates and docs

**Depends on:** LAB-014 onward incrementally

**Goal:** Make every experiment readable in the same format.

**Implementation steps**

1. Create a standard markdown template for reports:
   - contract
   - observation boundary
   - topology
   - workload
   - results
   - interpretation
   - architectural justification
2. Make `Analyze` emit this template automatically.
3. Add docs explaining how to run each milestone experiment.

**Acceptance criteria**

- Every experiment folder follows the same structure.
- Reports are readable without opening source code.
- Architectural justification is always tied back to measured data.

**Verification**

- generate at least one report automatically

---

### LAB-102 — Add final run scripts and operator README

**Depends on:** LAB-100, LAB-101

**Goal:** Make the lab runnable by another engineer.

**Implementation steps**

1. Add scripts or documented command sets to:
   - start all services
   - start east/west topology
   - seed data
   - run each experiment
   - analyze results
2. Add a root README that explains:
   - architecture
   - milestones
   - commands
   - where logs and reports go
3. Add a troubleshooting section for:
   - SQLite locked database
   - missing run ID
   - stale logs
   - port collisions

**Acceptance criteria**

- A new engineer can follow the README and get a report.
- Commands are copy-paste runnable.
- The doc names the limits of the local simulation honestly.

**Verification**

- perform one clean-machine-style walkthrough

---

# 10. Service-by-service implementation guidance

This section is not a ticket. It is a constraint set Codex should follow across tickets.

## 10.1 Storefront.Api

Storefront is the **only user-visible observation boundary**.

It must:

- expose public endpoints
- create and finalize `RequestTraceRecord`
- define the operation contract for each endpoint
- measure top-level stage timings
- wrap outbound calls to downstream services and record `DependencyCallRecord`
- merge downstream debug stage timings into top-level traces where helpful
- never write directly to the primary DB for business data

Required public endpoints by the time the backlog is complete:

- `GET /health`
- `GET /cpu`
- `GET /io`
- `GET /products/{id}`
- `POST /cart/items`
- `POST /checkout`
- `GET /orders/{userId}`

## 10.2 Catalog.Api

Catalog owns:

- product lookup
- price display
- stock summary display

It must be read-heavy and cache-friendly.

It should not become the user-visible trace boundary. Storefront stays the top-level measurement point.

## 10.3 Cart.Api

Cart owns:

- cart creation
- add item
- remove item
- get cart

Do not keep cart truth in server memory. Use the DB.

This preserves correctness even when routing mode changes.

## 10.4 Order.Api

Order owns:

- checkout orchestration
- inventory reservation
- order persistence
- payment attempt initiation
- order status transitions
- payment callback or payment status update logic
- async job enqueueing for background processing

This is the service where transaction boundaries and idempotency matter most.

## 10.5 PaymentSimulator.Api

Payment simulator must be deliberately “fake but honest”.

It is not just `Task.Delay`.

It must model the things system design answers usually hand-wave:

- fast dependency
- slow dependency
- timeout
- transient failure
- delayed confirmation
- duplicate callback

## 10.6 Worker

Worker owns:

- durable queue consumption
- payment retry / confirmation jobs
- projection jobs
- backlog growth
- queue metrics

It must make the split between **queue delay** and **execution time** visible.

## 10.7 Proxy

Proxy owns:

- routing choice
- round robin
- sticky routing
- basic access logs

It exists to show load balancing and locality, not to become a full API gateway.

## 11. Analyzer requirements Codex must satisfy

The analyzer must support all milestone experiments by the end.

## 11.1 Request metrics

For a run window, compute:

- request count
- average latency
- p50
- p95
- p99
- throughput
- average concurrency using exact accounting from lifetimes
- errors by code
- rate-limited fraction
- cache hit rate
- cache miss rate
- same-region vs cross-region counts
- dependency timing breakdown by dependency name

## 11.2 Queue metrics

From `jobs.jsonl`, compute:

- pending/in-progress/completed counts if derivable
- average enqueue-to-dequeue delay
- p95 enqueue-to-dequeue delay
- average execution time
- p95 execution time
- retry count distribution
- oldest queued item age if snapshots/logs support it

## 11.3 Replica metrics

Compute:

- reads by source
- latency by read source
- stale-read fraction
- average staleness age if enough data exists

## 11.4 Report structure

Every generated report must include:

1. Contract
2. Observation boundary
3. Topology
4. Workload
5. Results
6. Interpretation
7. Architectural justification

Do not skip the explanation section.

## 12. Experiment matrix Codex should ultimately support

These are the required named experiments.

1. **CPU-bound endpoint**
2. **I/O-bound endpoint**
3. **Cache off vs cache on**
4. **Single instance vs two instances**
5. **No rate limit vs rate limit**
6. **Synchronous checkout vs async payment confirmation**
7. **Primary read vs read replica**
8. **Region-local cache vs cross-region dependency**

If time permits, add:

9. **Sticky vs round-robin cart locality**
10. **Replica available vs degraded cross-region fallback**

## 13. Definition of done for the whole lab

The lab is done when all of these are true:

- a new engineer can run the system locally
- SeedData creates a usable dataset
- LoadGen can drive controlled experiments
- Storefront writes trustworthy request traces
- Worker writes trustworthy job traces
- Analyze turns traces into metrics and markdown reports
- each architectural pattern can be justified with a before/after run
- no key mechanism is hidden inside an external infrastructure dependency
- the codebase remains small enough to explain in an interview or teaching session

## 14. Recommended sequence if work must be front-loaded for learning value

If the team wants the fastest path to useful results, do the backlog in this priority order:

1. Milestone 0
2. Milestone 1
3. Milestone 2
4. Milestone 4
5. Milestone 5
6. Milestone 6
7. Milestone 7
8. Milestone 8
9. Milestone 9
10. Milestone 3 if not already complete, or earlier if cart discussion is needed sooner

That sequence gets to the most educational measurements fastest:

- CPU vs I/O
- cache justification
- payment dependency latency
- async queue behavior
- rate limiting under overload

## 15. Final instruction to Codex

Do not try to be clever by collapsing the lab into a monolith with hidden helpers.

The whole point is that the system design concepts remain **structurally visible**:

- cache is visible
- queue is visible
- payment dependency is visible
- replication lag is visible
- region cost is visible
- load-balancer choice is visible
- rate limiting is visible
- contract satisfaction is visible
- observation boundary is visible

Build the smallest local system that makes those truths measurable.
