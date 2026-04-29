# Milestone 2 Experiment: Cache Off vs Cache On

Generated from the clean milestone-2 run on `2026-03-31`.

## Contract

This experiment compares the same user-visible `Storefront.Api` contract under the same offered load:

- `GET /products/sku-0001?cache=off`
- `GET /products/sku-0001?cache=on`

The purpose is to justify caching with measurements instead of slogans. We want to see what happens to the same hot read path when the top-level Storefront cache is bypassed versus when it is allowed to absorb repeated reads.

## Observation Boundary

The source of truth for this report is the server-side `product-page` contract boundary:

- start: request arrival at `Storefront.Api`
- end: final HTTP response emitted by `Storefront.Api`

The analyzer is intentionally filtered with `--operation product-page` so the summary excludes internal `Catalog.Api` traces. That matters because this experiment is about the user-visible product-page contract, not the mixed run-wide telemetry from every participating service.

Average concurrency is reconstructed from full request lifetimes inside that boundary, not from thread counts.

## Topology

- one `Storefront.Api` process
- one `Catalog.Api` process
- `Catalog.Api` cache disabled on purpose to isolate the Storefront cache decision
- `primary.db` seeded with `4` products and `3` users
- `ReadModel_ProductPage` rebuilt during setup, but not used on the live read path yet
- one clean experiment workspace under `docs/experiments/milestone-2-cache-off-vs-cache-on/workspace`

The experiment was produced by:

```bash
./scripts/experiments/run-milestone-2-cache-off-vs-cache-on.sh
```

Generated artifacts:

- `docs/experiments/milestone-2-cache-off-vs-cache-on/artifacts/milestone-2-cache-off-summary.json`
- `docs/experiments/milestone-2-cache-off-vs-cache-on/artifacts/milestone-2-cache-on-summary.json`
- `docs/experiments/milestone-2-cache-off-vs-cache-on/artifacts/milestone-2-cache-off-analysis.md`
- `docs/experiments/milestone-2-cache-off-vs-cache-on/artifacts/milestone-2-cache-on-analysis.md`
- `docs/experiments/milestone-2-cache-off-vs-cache-on/artifacts/milestone-2-cache-off-loadgen.txt`
- `docs/experiments/milestone-2-cache-off-vs-cache-on/artifacts/milestone-2-cache-on-loadgen.txt`
- `docs/experiments/milestone-2-cache-off-vs-cache-on/artifacts/requests.jsonl`

## Workload

Both runs used the same offered load shape:

- mode: constant
- target rate: `600` requests per second
- duration: `6` seconds
- concurrency cap: `64`
- planned request count per run: `3600`

The seeded product set is small (`4` products), but the offered load is intentionally concentrated on the hottest key: `sku-0001`.

That concentration is deliberate. This is a hot-read experiment, so we want the cache to face the exact pattern that makes caching architecturally meaningful: repeated requests for the same already-materialized product page.

## Results

Server-side analyzer summary at the `product-page` boundary:

| Metric | Cache off | Cache on |
| --- | --- | --- |
| Request count | 3600 | 3600 |
| Average latency (ms) | 111.058 | 0.103 |
| P50 latency (ms) | 99.141 | 0.037 |
| P95 latency (ms) | 204.367 | 0.100 |
| P99 latency (ms) | 300.778 | 0.209 |
| Throughput (req/s) | 281.008 | 603.289 |
| Average concurrency | 31.208 | 0.062 |
| Cache hit rate | 0.00% | 99.36% |
| Cache miss rate | 100.00% | 0.64% |
| Errors | 0 | 0 |

The `cache=on` run recorded `3577` hits and only `23` misses. Those misses were the short warm-up burst while the first overlapping requests populated the cache.

The throughput difference is the key result:

- cache off: `281.008 req/s`
- cache on: `603.289 req/s`

That is about a `2.15x` throughput gain at the same external offered load, with the cached path also cutting average latency from `111.058 ms` to `0.103 ms`.

Representative trace evidence:

- Cache-off trace `bcc8f8ad05b548d0b013fb2673f42f06` took `254.906 ms`, recorded `cacheHit=false`, and contained one `catalog-api` dependency call lasting `245.633 ms`.
- Cache-on trace `baad461ab0c74038b20a2b9a47563d84` took `0.099 ms`, recorded `cacheHit=true`, and contained `0` dependency calls.
- A representative cache-on warm-up miss such as `8fc8b1846def486497b7d0313446078b` still completed in only `9.048 ms`, because it fetched once, populated the Storefront cache, and then allowed the rest of the run to collapse to memory-speed reads.

Client-side `LoadGen` averages were higher:

- cache off: `226.740 ms`
- cache on: `1.973 ms`

That gap is expected because the client also pays for its own timing and network overhead. The server-side analyzer summaries remain the source of truth for this report because they live exactly on the measured contract boundary.

## Interpretation

The cache-off run could not keep up with the offered load. We offered `600 req/s` for `6` seconds, but the product-page boundary only sustained `281.008 req/s`. That means requests accumulated in the system and had to drain after the offered-load window ended. The analyzer confirms this directly:

- cache off window duration: `12811.025 ms`
- cache on window duration: `5967.292 ms`

So the cache-off run stretched a nominal `6` second workload into about `12.8` seconds of observed request completions, while the cache-on run stayed close to the offered window.

The latency distributions tell the same story:

- cache off median latency was already about `99 ms`
- cache off `P95` climbed above `204 ms`
- cache off `P99` reached about `301 ms`
- cache on stayed below `0.21 ms` even at `P99`

This is not merely “cache makes things faster.” It is more structural than that.

With `cache=off`, every Storefront request must:

- miss at the Storefront boundary by definition
- call `Catalog.Api`
- force `Catalog.Api` to bypass its own cache
- read from `primary.db`
- return through the dependency chain before the user-visible response can complete

With `cache=on`, almost all requests stop at the Storefront boundary itself:

- `3577` out of `3600` requests were satisfied from Storefront memory
- those hits needed no downstream dependency call at all
- the synchronous work per request collapsed so much that the same offered load no longer built a queue

That is why caching behaves like a capacity multiplier on a hot read path. It does not merely shave milliseconds off a fixed amount of work. It removes repeated downstream work from the steady-state path, which means the system can complete more user-visible contracts per second before concurrency starts to pile up.

## Architectural Justification

This experiment justifies three design instincts for the lab:

- cache experiments must be measured at the user-visible boundary, not only at internal services
- hot-key read paths should be evaluated under offered load high enough to reveal queueing, not only at low load where every design looks fine
- internal caches must be isolated when the teaching goal is to prove one specific cache layer

It also explains why the product-page projection store was introduced before it is used. The projection gives us another future way to shorten the read path, but this milestone proves that even before swapping read models, a boundary-local cache can more than double sustainable throughput on a repeated-read workload.

In short: the cache-on path is not just lower-latency. Under the same offered load, it preserves the contract boundary by preventing queue growth, collapsing dependency traffic, and roughly doubling completed throughput.
