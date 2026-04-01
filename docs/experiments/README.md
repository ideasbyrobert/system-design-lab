# Experiment Reports

This directory is the stable home for runnable milestone experiments and their write-ups.

## Standard Structure

Each experiment folder follows the same layout:

```text
docs/experiments/<experiment-slug>/
  report.md
  artifacts/
  workspace/
```

- `report.md`
  the curated milestone write-up using the standard section order:
  `Contract`, `Observation Boundary`, `Topology`, `Workload`, `Results`, `Interpretation`, `Architectural Justification`
- `artifacts/`
  copied summaries, analyzer reports, comparison files, response samples, and other durable outputs worth keeping
- `workspace/`
  the disposable run workspace used by the script: local databases, logs, and generated analysis output

Inside `artifacts/`, the per-run analyzer reports are generated automatically by `Analyze`. Those reports now use the same section skeleton every time, even when the surrounding milestone report carries richer scenario-specific prose.

## Running Experiments

All commands assume:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
```

### Milestone 1

- Script: `./scripts/experiments/run-milestone-1-cpu-vs-io.sh`
- Folder: [`milestone-1-cpu-vs-io`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-1-cpu-vs-io)
- Report: [report.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-1-cpu-vs-io/report.md)

### Milestone 2

- Script: `./scripts/experiments/run-milestone-2-cache-off-vs-cache-on.sh`
- Folder: [`milestone-2-cache-off-vs-cache-on`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-2-cache-off-vs-cache-on)
- Report: [report.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-2-cache-off-vs-cache-on/report.md)

### Milestone 4

- Script: `./scripts/experiments/run-milestone-4-synchronous-checkout.sh`
- Folder: [`milestone-4-synchronous-checkout`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-4-synchronous-checkout)
- Report: [report.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-4-synchronous-checkout/report.md)

### Milestone 5

- Script: `./scripts/experiments/run-milestone-5-sync-vs-async-checkout.sh`
- Folder: [`milestone-5-sync-vs-async-checkout`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-5-sync-vs-async-checkout)
- Report: [report.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-5-sync-vs-async-checkout/report.md)

### Milestone 6

- Script: `./scripts/experiments/run-milestone-6-no-limit-vs-rate-limit-overload.sh`
- Folder: [`milestone-6-no-limit-vs-rate-limit-overload`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-6-no-limit-vs-rate-limit-overload)
- Report: [report.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-6-no-limit-vs-rate-limit-overload/report.md)

### Milestone 7

- Script: `./scripts/experiments/run-milestone-7-one-instance-vs-two-instance-scaling.sh`
- Folder: [`milestone-7-one-instance-vs-two-instance-scaling`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-7-one-instance-vs-two-instance-scaling)
- Report: [report.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-7-one-instance-vs-two-instance-scaling/report.md)

### Milestone 8

- Script: `./scripts/experiments/run-milestone-8-primary-vs-replica-read-model.sh`
- Folder: [`milestone-8-primary-vs-replica-read-model`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-8-primary-vs-replica-read-model)
- Report: [report.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-8-primary-vs-replica-read-model/report.md)

### Milestone 9

- Script: `./scripts/experiments/run-milestone-9-same-region-vs-cross-region.sh`
- Folder: [`milestone-9-same-region-vs-cross-region`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-same-region-vs-cross-region)
- Report: [report.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-same-region-vs-cross-region/report.md)

- Script: `./scripts/experiments/run-milestone-9-degraded-mode-failover.sh`
- Folder: [`milestone-9-degraded-mode-failover`](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-degraded-mode-failover)
- Report: [report.md](/Users/robertkarapetyan/git/project/source/ecommerce-systems-lab/docs/experiments/milestone-9-degraded-mode-failover/report.md)

## Analyzer Output

To generate a standard per-run report directly:

```bash
cd /Users/robertkarapetyan/git/project/source/ecommerce-systems-lab
Lab__Repository__RootPath=/path/to/workspace \
dotnet run --project src/Analyze -- --run-id <run-id> [--operation <operation>]
```

That produces:

- `logs/runs/<run-id>/summary.json`
- `analysis/<run-id>/report.md`

The auto-generated report now always uses the same section template:

1. `Contract`
2. `Observation Boundary`
3. `Topology`
4. `Workload`
5. `Results`
6. `Interpretation`
7. `Architectural Justification`
