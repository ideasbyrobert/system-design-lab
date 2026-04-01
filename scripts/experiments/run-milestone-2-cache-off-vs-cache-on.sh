#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKSPACE_ROOT="${LAB_EXPERIMENT_ROOT:-$PROJECT_ROOT/docs/experiments/milestone-2-cache-off-vs-cache-on/workspace}"
ARTIFACT_ROOT="$PROJECT_ROOT/docs/experiments/milestone-2-cache-off-vs-cache-on/artifacts"
STORE_URL="${LAB_STORE_URL:-http://127.0.0.1:5087}"
CATALOG_URL="${LAB_CATALOG_URL:-http://127.0.0.1:5088}"
OFF_RUN_ID="${LAB_CACHE_OFF_RUN_ID:-milestone-2-cache-off}"
ON_RUN_ID="${LAB_CACHE_ON_RUN_ID:-milestone-2-cache-on}"
REQUESTS_PER_SECOND="${LAB_EXPERIMENT_RPS:-600}"
DURATION_SECONDS="${LAB_EXPERIMENT_DURATION_SECONDS:-6}"
CONCURRENCY_CAP="${LAB_EXPERIMENT_CONCURRENCY_CAP:-64}"
PRODUCT_COUNT="${LAB_EXPERIMENT_PRODUCT_COUNT:-4}"
USER_COUNT="${LAB_EXPERIMENT_USER_COUNT:-3}"
HOT_PRODUCT_ID="${LAB_HOT_PRODUCT_ID:-sku-0001}"
ANALYZE_OPERATION="${LAB_ANALYZE_OPERATION:-product-page}"
CATALOG_LOG_PATH="$ARTIFACT_ROOT/catalog.log"
STORE_LOG_PATH="$ARTIFACT_ROOT/storefront.log"

cleanup() {
    if [[ -n "${STORE_PID:-}" ]] && kill -0 "$STORE_PID" 2>/dev/null; then
        kill "$STORE_PID" 2>/dev/null || true
        wait "$STORE_PID" 2>/dev/null || true
    fi

    if [[ -n "${CATALOG_PID:-}" ]] && kill -0 "$CATALOG_PID" 2>/dev/null; then
        kill "$CATALOG_PID" 2>/dev/null || true
        wait "$CATALOG_PID" 2>/dev/null || true
    fi
}

trap cleanup EXIT

echo "Preparing clean milestone-2 experiment workspace at: $WORKSPACE_ROOT"
rm -rf "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"
mkdir -p "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"

cd "$PROJECT_ROOT"

echo "Building solution..."
dotnet build ecommerce-systems-lab.sln > "$ARTIFACT_ROOT/build.log" 2>&1

echo "Seeding primary data and rebuilding the product-page projection..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/SeedData --no-build -- \
    --products "$PRODUCT_COUNT" \
    --users "$USER_COUNT" \
    --reset true \
    --rebuild-product-page-projection true > "$ARTIFACT_ROOT/seed-data.txt" 2>&1

echo "Starting Catalog.Api at $CATALOG_URL with Catalog cache disabled..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
LAB__Cache__Enabled=false \
dotnet run --project src/Catalog.Api --no-build --urls "$CATALOG_URL" > "$CATALOG_LOG_PATH" 2>&1 &
CATALOG_PID=$!

echo "Starting Storefront.Api at $STORE_URL ..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
LAB__Cache__Enabled=false \
LAB__ServiceEndpoints__CatalogBaseUrl="$CATALOG_URL" \
dotnet run --project src/Storefront.Api --no-build --urls "$STORE_URL" > "$STORE_LOG_PATH" 2>&1 &
STORE_PID=$!

echo "Waiting for Storefront.Api and Catalog.Api to become healthy..."
for attempt in {1..60}; do
    if curl -fsS "$STORE_URL/health" > /dev/null 2>&1 && curl -fsS "$CATALOG_URL/" > /dev/null 2>&1; then
        break
    fi

    if ! kill -0 "$STORE_PID" 2>/dev/null; then
        echo "Storefront.Api exited before becoming healthy. See $STORE_LOG_PATH" >&2
        exit 1
    fi

    if ! kill -0 "$CATALOG_PID" 2>/dev/null; then
        echo "Catalog.Api exited before becoming healthy. See $CATALOG_LOG_PATH" >&2
        exit 1
    fi

    sleep 0.2

    if [[ "$attempt" -eq 60 ]]; then
        echo "Timed out waiting for the experiment hosts to become healthy." >&2
        exit 1
    fi
done

OFF_TARGET_URL="$STORE_URL/products/$HOT_PRODUCT_ID?cache=off"
ON_TARGET_URL="$STORE_URL/products/$HOT_PRODUCT_ID?cache=on"

echo "Running cache-off experiment: $OFF_TARGET_URL"
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/LoadGen --no-build -- \
    --target-url "$OFF_TARGET_URL" \
    --rps "$REQUESTS_PER_SECOND" \
    --duration-seconds "$DURATION_SECONDS" \
    --concurrency-cap "$CONCURRENCY_CAP" \
    --run-id "$OFF_RUN_ID" > "$ARTIFACT_ROOT/$OFF_RUN_ID-loadgen.txt" 2>&1

echo "Running cache-on experiment: $ON_TARGET_URL"
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/LoadGen --no-build -- \
    --target-url "$ON_TARGET_URL" \
    --rps "$REQUESTS_PER_SECOND" \
    --duration-seconds "$DURATION_SECONDS" \
    --concurrency-cap "$CONCURRENCY_CAP" \
    --run-id "$ON_RUN_ID" > "$ARTIFACT_ROOT/$ON_RUN_ID-loadgen.txt" 2>&1

echo "Analyzing cache-off run at the product-page boundary..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/Analyze --no-build -- \
    --run-id "$OFF_RUN_ID" \
    --operation "$ANALYZE_OPERATION" > "$ARTIFACT_ROOT/$OFF_RUN_ID-analyze.txt" 2>&1

echo "Analyzing cache-on run at the product-page boundary..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/Analyze --no-build -- \
    --run-id "$ON_RUN_ID" \
    --operation "$ANALYZE_OPERATION" > "$ARTIFACT_ROOT/$ON_RUN_ID-analyze.txt" 2>&1

cp "$WORKSPACE_ROOT/logs/runs/$OFF_RUN_ID/summary.json" "$ARTIFACT_ROOT/$OFF_RUN_ID-summary.json"
cp "$WORKSPACE_ROOT/logs/runs/$ON_RUN_ID/summary.json" "$ARTIFACT_ROOT/$ON_RUN_ID-summary.json"
cp "$WORKSPACE_ROOT/analysis/$OFF_RUN_ID/report.md" "$ARTIFACT_ROOT/$OFF_RUN_ID-analysis.md"
cp "$WORKSPACE_ROOT/analysis/$ON_RUN_ID/report.md" "$ARTIFACT_ROOT/$ON_RUN_ID-analysis.md"
cp "$WORKSPACE_ROOT/logs/requests.jsonl" "$ARTIFACT_ROOT/requests.jsonl"

echo "Milestone-2 experiment completed."
echo "Workspace:          $WORKSPACE_ROOT"
echo "Artifacts:          $ARTIFACT_ROOT"
echo "Cache-off summary:  $ARTIFACT_ROOT/$OFF_RUN_ID-summary.json"
echo "Cache-on summary:   $ARTIFACT_ROOT/$ON_RUN_ID-summary.json"
