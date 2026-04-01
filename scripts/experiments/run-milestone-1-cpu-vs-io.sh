#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKSPACE_ROOT="${LAB_EXPERIMENT_ROOT:-$PROJECT_ROOT/docs/experiments/milestone-1-cpu-vs-io/workspace}"
ARTIFACT_ROOT="$PROJECT_ROOT/docs/experiments/milestone-1-cpu-vs-io/artifacts"
HOST_URL="${LAB_STORE_URL:-http://127.0.0.1:5086}"
CPU_RUN_ID="${LAB_CPU_RUN_ID:-milestone-1-cpu}"
IO_RUN_ID="${LAB_IO_RUN_ID:-milestone-1-io}"
REQUESTS_PER_SECOND="${LAB_EXPERIMENT_RPS:-2}"
DURATION_SECONDS="${LAB_EXPERIMENT_DURATION_SECONDS:-8}"
CONCURRENCY_CAP="${LAB_EXPERIMENT_CONCURRENCY_CAP:-1}"
CPU_TARGET_URL="${LAB_CPU_TARGET_URL:-$HOST_URL/cpu?workFactor=20&iterations=1000}"
IO_TARGET_URL="${LAB_IO_TARGET_URL:-$HOST_URL/io?delayMs=90&jitterMs=0}"
STORE_LOG_PATH="$ARTIFACT_ROOT/storefront.log"

cleanup() {
    if [[ -n "${STORE_PID:-}" ]] && kill -0 "$STORE_PID" 2>/dev/null; then
        kill "$STORE_PID" 2>/dev/null || true
        wait "$STORE_PID" 2>/dev/null || true
    fi
}

trap cleanup EXIT

echo "Preparing clean milestone-1 experiment workspace at: $WORKSPACE_ROOT"
rm -rf "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"
mkdir -p "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"

cd "$PROJECT_ROOT"

echo "Building solution..."
dotnet build ecommerce-systems-lab.sln > "$ARTIFACT_ROOT/build.log" 2>&1

echo "Starting Storefront.Api at $HOST_URL ..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/Storefront.Api --no-build --urls "$HOST_URL" > "$STORE_LOG_PATH" 2>&1 &
STORE_PID=$!

echo "Waiting for Storefront.Api to become healthy..."
for attempt in {1..50}; do
    if curl -fsS "$HOST_URL/health" > /dev/null 2>&1; then
        break
    fi

    if ! kill -0 "$STORE_PID" 2>/dev/null; then
        echo "Storefront.Api exited before becoming healthy. See $STORE_LOG_PATH" >&2
        exit 1
    fi

    sleep 0.2

    if [[ "$attempt" -eq 50 ]]; then
        echo "Timed out waiting for Storefront.Api health endpoint. See $STORE_LOG_PATH" >&2
        exit 1
    fi
done

echo "Running CPU experiment: $CPU_TARGET_URL"
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/LoadGen --no-build -- \
    --target-url "$CPU_TARGET_URL" \
    --rps "$REQUESTS_PER_SECOND" \
    --duration-seconds "$DURATION_SECONDS" \
    --concurrency-cap "$CONCURRENCY_CAP" \
    --run-id "$CPU_RUN_ID" > "$ARTIFACT_ROOT/$CPU_RUN_ID-loadgen.txt" 2>&1

echo "Running I/O experiment: $IO_TARGET_URL"
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/LoadGen --no-build -- \
    --target-url "$IO_TARGET_URL" \
    --rps "$REQUESTS_PER_SECOND" \
    --duration-seconds "$DURATION_SECONDS" \
    --concurrency-cap "$CONCURRENCY_CAP" \
    --run-id "$IO_RUN_ID" > "$ARTIFACT_ROOT/$IO_RUN_ID-loadgen.txt" 2>&1

echo "Analyzing CPU run..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/Analyze --no-build -- --run-id "$CPU_RUN_ID" > "$ARTIFACT_ROOT/$CPU_RUN_ID-analyze.txt" 2>&1

echo "Analyzing I/O run..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/Analyze --no-build -- --run-id "$IO_RUN_ID" > "$ARTIFACT_ROOT/$IO_RUN_ID-analyze.txt" 2>&1

cp "$WORKSPACE_ROOT/logs/runs/$CPU_RUN_ID/summary.json" "$ARTIFACT_ROOT/$CPU_RUN_ID-summary.json"
cp "$WORKSPACE_ROOT/logs/runs/$IO_RUN_ID/summary.json" "$ARTIFACT_ROOT/$IO_RUN_ID-summary.json"
cp "$WORKSPACE_ROOT/analysis/$CPU_RUN_ID/report.md" "$ARTIFACT_ROOT/$CPU_RUN_ID-analysis.md"
cp "$WORKSPACE_ROOT/analysis/$IO_RUN_ID/report.md" "$ARTIFACT_ROOT/$IO_RUN_ID-analysis.md"
cp "$WORKSPACE_ROOT/logs/requests.jsonl" "$ARTIFACT_ROOT/requests.jsonl"

echo "Milestone-1 experiment completed."
echo "Workspace:  $WORKSPACE_ROOT"
echo "Artifacts:  $ARTIFACT_ROOT"
echo "CPU summary: $ARTIFACT_ROOT/$CPU_RUN_ID-summary.json"
echo "I/O summary: $ARTIFACT_ROOT/$IO_RUN_ID-summary.json"
