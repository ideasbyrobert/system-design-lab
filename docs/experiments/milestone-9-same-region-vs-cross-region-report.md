# Milestone 9 Experiment: Same-Region vs Cross-Region

Generated from the clean milestone-9 run on `2026-04-01` UTC.

## Contract

Milestone 9 is supposed to justify two concrete claims:

1. putting a read stack in the same region as the caller removes geography cost on dependency misses
2. local reads only stay local when the stack actually has a local cache and a local physical read source to use

The point is not to say “multi-region is faster.” The point is to make the penalty of cross-region dependency explicit, and then show what local cache plus local read selection buys us.

This experiment is the lab version of the named experiment from the blueprint:

- `Region-local cache vs cross-region dependency`

## Observation Boundary

The measured user-visible boundary is:

- `GET /products/{id}?cache=on&readSource=local`

The top-level measured operation is:

- `product-page`

All three arms used the same user-visible path. What changed was topology.

The experiment was produced by:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
./scripts/experiments/run-milestone-9-same-region-vs-cross-region.sh
```

Generated artifacts live in:

- `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-same-region-vs-cross-region/artifacts/east-local/`
- `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-same-region-vs-cross-region/artifacts/west-local/`
- `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-same-region-vs-cross-region/artifacts/west-forced-east/`
- `/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-same-region-vs-cross-region/artifacts/comparison.json`

## Topology

Three product-read scenarios were measured.

### 1. East client -> east stack

- `Storefront.Api` in `us-east`
- `Catalog.Api` in `us-east`
- `readSource=local`
- effective physical read source: `primary`

### 2. West client -> west local cache/replica

- `Storefront.Api` in `us-west`
- `Catalog.Api` in `us-west`
- `readSource=local`
- effective physical read source: `replica-west`

### 3. West client -> forced east dependency

- `Storefront.Api` in `us-west`
- `Catalog.Api` in `us-east`
- `readSource=local`
- effective physical read source: `primary`

That last arm is important. The request still says `local`, but because the dependency stack sits in `us-east`, “local” resolves to the east-side authoritative source. The user-visible API surface stays the same while the physical path changes underneath it.

## Workload

The run intentionally used a product mix rather than a single hot key.

- seeded products: `16`
- measured products: `8`
- request rate: `1 req/s` per product
- duration: `6 s`
- total measured requests per arm: `48`
- Storefront cache mode: `on`
- requested read source: `local`
- sample cold request captured separately with `cache=off`

This shape was chosen for one reason: keep enough misses in the measured window to make the dependency path visible.

With `8` products and `48` total requests, each arm paid about one miss per product:

- miss request count: `8`
- hit request count: `40`
- cache hit rate: `83.333%`

That means the cache hit rate is effectively held constant across all three arms. So when latency changes, the cleaner explanation is geography and source placement, not a lucky cache run.

## Results

### Top-level product-page metrics

| Arm | Effective source | Avg latency (ms) | P95 (ms) | Throughput (req/s) | Cache hit rate |
| --- | --- | ---: | ---: | ---: | ---: |
| east-local | `primary` | `4.568` | `24.629` | `9.014` | `83.333%` |
| west-local | `replica-west` | `7.467` | `50.237` | `9.002` | `83.333%` |
| west-forced-east | `primary` | `9.966` | `58.326` | `8.865` | `83.333%` |

### Miss-path dependency metrics

These numbers are computed only over the miss requests, because that is where geography actually matters.

| Arm | Dependency network scope | Avg dependency elapsed on misses (ms) | P95 dependency elapsed on misses (ms) | Avg dependency share of miss latency |
| --- | --- | ---: | ---: | ---: |
| east-local | `same-region` | `24.353` | `41.619` | `94.489%` |
| west-local | `same-region` | `41.958` | `53.287` | `96.965%` |
| west-forced-east | `cross-region` | `56.731` | `74.526` | `97.385%` |

### Read-source resolution samples

Cold sample requests with `cache=off&readSource=local` returned:

| Arm | Sample `readSource` | Meaning |
| --- | --- | --- |
| east-local | `primary` | east stack resolved `local` to the east authoritative source |
| west-local | `replica-west` | west stack resolved `local` to the west local replica |
| west-forced-east | `primary` | west caller still had to cross to east because the dependency stack lived there |

## Key Comparisons

### West forced east vs west local

This is the cleanest geography comparison because both arms share:

- caller region: `us-west`
- cache hit rate: `83.333%`
- miss count: `8`

Only the dependency placement changes.

Measured change:

- average latency increased by `2.498 ms` (`33.459%`)
- p95 latency increased by `8.089 ms` (`16.101%`)
- average dependency elapsed on misses increased by `14.773 ms`
- throughput fell from `9.002` to `8.865 req/s`

This is the geography penalty made visible. Nothing magical happened to the cache. The misses simply had farther to travel.

### West local vs east local

This comparison is useful for honesty.

- west-local was slower than east-local by `2.899 ms` on average (`63.456%`)
- both arms were same-region
- both arms had the same `83.333%` hit rate

So the lesson is not “all local stacks behave identically.” In this run:

- east-local hit the east primary locally
- west-local hit the west replica locally

Both are local, but they are still different physical paths with different costs in this implementation.

That is exactly why the report should not pretend multi-region is free. Extra regional machinery can help, but it also adds more moving parts and more paths to reason about.

## Interpretation

Three facts stand out.

### 1. The dependency path dominates miss latency

In every arm, the dependency call consumed almost all miss-path latency:

- `94.489%` in east-local
- `96.965%` in west-local
- `97.385%` in west-forced-east

So the dominant term is not hidden. The product miss path is dependency-bound.

### 2. Cross-region placement hurts even when cache hit rate stays the same

The strongest result of the run is that west-local and west-forced-east had identical hit rates, but the forced-east arm was still slower.

That means the measured penalty is not:

- “west got a worse cache run”
- “the app just happened to be noisy”

It is the cost of crossing regions on misses.

### 3. Local reads matter because `local` becomes a different physical source

The west-local arm did not merely reuse the same east source with a nicer label. Its cold sample resolved to:

- `replica-west`

The west-forced-east arm resolved to:

- `primary`

So local replica placement is not a naming exercise. It changes which database file and which region the request actually touches.

## Architectural Justification

This milestone justifies three careful claims.

### 1. Regional placement can remove a real miss-path penalty

For west traffic, keeping the dependency local removed about `14.773 ms` of average dependency time on misses relative to forcing that same west traffic through east.

### 2. Per-region cache keeps the hot path cheap once warmed

All three arms converged to the same `83.333%` hit rate under the same product mix. That means the fast path is still cache-dominated after the first misses are paid.

### 3. Multi-region is not free

The experiment does **not** support any simplistic claim like:

- “put replicas everywhere and latency disappears”
- “local reads are automatically cheapest”

In this run:

- east-local was the fastest arm
- west-local was slower than east-local
- west-forced-east was slower still

So the honest conclusion is:

- local cache helps
- local physical reads help
- cross-region dependency hurts
- but every added regional path introduces real operational and measurement complexity

That is exactly the teaching target of milestone 9.
