SOLUTION := ecommerce-systems-lab.sln
SHELL_SCRIPTS := $(shell find scripts -name '*.sh' -type f | sort)

.PHONY: audit build check experiment-1 experiment-5 experiment-6 experiment-9-failover format restore test verify-scripts

check: restore format verify-scripts build test audit

restore:
	dotnet tool restore
	dotnet restore $(SOLUTION) --force-evaluate

format:
	dotnet format $(SOLUTION) --no-restore --verify-no-changes

verify-scripts:
	bash -n $(SHELL_SCRIPTS)

build:
	dotnet build $(SOLUTION) --configuration Release --no-restore

test:
	dotnet test $(SOLUTION) --configuration Release --no-build --logger "console;verbosity=minimal"

audit:
	./scripts/verify-vulnerabilities.sh

experiment-1: build
	./scripts/experiments/run-milestone-1-cpu-vs-io.sh

experiment-5: build
	./scripts/experiments/run-milestone-5-sync-vs-async-checkout.sh

experiment-6: build
	./scripts/experiments/run-milestone-6-no-limit-vs-rate-limit-overload.sh

experiment-9-failover: build
	./scripts/experiments/run-milestone-9-degraded-mode-failover.sh
