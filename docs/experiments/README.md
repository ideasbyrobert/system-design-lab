# Experiment Reports

Nine curated reports connect one architectural question to one reproducible
local experiment.

| Milestone | Question | Script | Report |
| --- | --- | --- | --- |
| 1 · Featured | CPU-bound or I/O-bound? | run-milestone-1-cpu-vs-io.sh | [Read](milestone-1-cpu-vs-io-report.md) |
| 2 | Does a cache change the measured boundary? | run-milestone-2-cache-off-vs-cache-on.sh | [Read](milestone-2-cache-off-vs-cache-on-report.md) |
| 4 | How do dependency modes shape synchronous checkout? | run-milestone-4-synchronous-checkout.sh | [Read](milestone-4-synchronous-checkout-report.md) |
| 5 · Featured | Where does asynchronous checkout move cost? | run-milestone-5-sync-vs-async-checkout.sh | [Read](milestone-5-sync-vs-async-checkout-report.md) |
| 6 · Featured | What does admission control preserve and refuse? | run-milestone-6-no-limit-vs-rate-limit-overload.sh | [Read](milestone-6-no-limit-vs-rate-limit-overload-report.md) |
| 7 | When does another instance help? | run-milestone-7-one-instance-vs-two-instance-scaling.sh | [Read](milestone-7-one-instance-vs-two-instance-scaling-report.md) |
| 8 | What freshness cost follows replicas and read models? | run-milestone-8-primary-vs-replica-read-model.sh | [Read](milestone-8-primary-vs-replica-read-model-report.md) |
| 9 | What does cross-region distance cost? | run-milestone-9-same-region-vs-cross-region.sh | [Read](milestone-9-same-region-vs-cross-region-report.md) |
| 9 · Featured | How does degraded failover move latency? | run-milestone-9-degraded-mode-failover.sh | [Read](milestone-9-degraded-mode-failover-report.md) |

Run a script from the repository root after make build. Outputs default to
/tmp/system-design-lab-experiments and remain untracked. Each report states its
contract, observation boundary, topology, workload, results, interpretation,
architectural justification, and nonclaims.
