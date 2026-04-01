#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXPERIMENT_ROOT="$PROJECT_ROOT/docs/experiments/milestone-9-same-region-vs-cross-region"
WORKSPACE_ROOT="${LAB_EXPERIMENT_ROOT:-$EXPERIMENT_ROOT/workspace}"
ARTIFACT_ROOT="$EXPERIMENT_ROOT/artifacts"
COMPARISON_PATH="$ARTIFACT_ROOT/comparison.json"
PRETTY_COMPARISON_PATH="$ARTIFACT_ROOT/comparison-pretty.txt"
BUILD_LOG_PATH="$ARTIFACT_ROOT/build.log"

PRIMARY_REGION="${LAB_PRIMARY_REGION:-us-east}"
EAST_REGION="${LAB_EAST_REGION:-us-east}"
WEST_REGION="${LAB_WEST_REGION:-us-west}"

SAME_REGION_LATENCY_MS="${LAB_SAME_REGION_LATENCY_MS:-2}"
CROSS_REGION_LATENCY_MS="${LAB_CROSS_REGION_LATENCY_MS:-35}"

PRODUCT_COUNT="${LAB_PRODUCT_COUNT:-16}"
USER_COUNT="${LAB_USER_COUNT:-4}"
MEASURED_PRODUCT_COUNT="${LAB_MEASURED_PRODUCT_COUNT:-8}"
RPS_PER_PRODUCT="${LAB_RPS_PER_PRODUCT:-1}"
DURATION_SECONDS="${LAB_DURATION_SECONDS:-6}"
CONCURRENCY_CAP="${LAB_CONCURRENCY_CAP:-1}"

EAST_LOCAL_RUN_ID="${LAB_EAST_LOCAL_RUN_ID:-milestone-9-east-local}"
WEST_LOCAL_RUN_ID="${LAB_WEST_LOCAL_RUN_ID:-milestone-9-west-local}"
WEST_FORCED_EAST_RUN_ID="${LAB_WEST_FORCED_EAST_RUN_ID:-milestone-9-west-forced-east}"

EAST_LOCAL_WARMUP_RUN_ID="${LAB_EAST_LOCAL_WARMUP_RUN_ID:-milestone-9-east-local-warmup}"
WEST_LOCAL_WARMUP_RUN_ID="${LAB_WEST_LOCAL_WARMUP_RUN_ID:-milestone-9-west-local-warmup}"
WEST_FORCED_EAST_WARMUP_RUN_ID="${LAB_WEST_FORCED_EAST_WARMUP_RUN_ID:-milestone-9-west-forced-east-warmup}"

EAST_LOCAL_SAMPLE_RUN_ID="${LAB_EAST_LOCAL_SAMPLE_RUN_ID:-milestone-9-east-local-sample}"
WEST_LOCAL_SAMPLE_RUN_ID="${LAB_WEST_LOCAL_SAMPLE_RUN_ID:-milestone-9-west-local-sample}"
WEST_FORCED_EAST_SAMPLE_RUN_ID="${LAB_WEST_FORCED_EAST_SAMPLE_RUN_ID:-milestone-9-west-forced-east-sample}"

EAST_LOCAL_STOREFRONT_URL="${LAB_EAST_LOCAL_STOREFRONT_URL:-http://127.0.0.1:5141}"
EAST_LOCAL_CATALOG_URL="${LAB_EAST_LOCAL_CATALOG_URL:-http://127.0.0.1:5142}"
WEST_LOCAL_STOREFRONT_URL="${LAB_WEST_LOCAL_STOREFRONT_URL:-http://127.0.0.1:5143}"
WEST_LOCAL_CATALOG_URL="${LAB_WEST_LOCAL_CATALOG_URL:-http://127.0.0.1:5144}"
WEST_FORCED_EAST_STOREFRONT_URL="${LAB_WEST_FORCED_EAST_STOREFRONT_URL:-http://127.0.0.1:5145}"
WEST_FORCED_EAST_CATALOG_URL="${LAB_WEST_FORCED_EAST_CATALOG_URL:-http://127.0.0.1:5146}"

EAST_LOCAL_WORKSPACE="$WORKSPACE_ROOT/east-local"
WEST_LOCAL_WORKSPACE="$WORKSPACE_ROOT/west-local"
WEST_FORCED_EAST_WORKSPACE="$WORKSPACE_ROOT/west-forced-east"

EAST_LOCAL_ARTIFACT_ROOT="$ARTIFACT_ROOT/east-local"
WEST_LOCAL_ARTIFACT_ROOT="$ARTIFACT_ROOT/west-local"
WEST_FORCED_EAST_ARTIFACT_ROOT="$ARTIFACT_ROOT/west-forced-east"

declare -a SERVICE_PIDS=()

build_measured_products() {
    local -a products=()

    for index in $(seq 1 "$MEASURED_PRODUCT_COUNT"); do
        products+=("$(printf 'sku-%04d' "$index")")
    done

    printf '%s\n' "${products[@]}"
}

MEASURED_PRODUCTS=()
while IFS= read -r product_id; do
    MEASURED_PRODUCTS+=("$product_id")
done < <(build_measured_products)

WARMUP_PRODUCT_ID="$(printf 'sku-%04d' "$PRODUCT_COUNT")"
SAMPLE_PRODUCT_ID="$(printf 'sku-%04d' "$((MEASURED_PRODUCT_COUNT + 1))")"

register_pid() {
    SERVICE_PIDS+=("$1")
}

stop_services() {
    local pid

    for pid in "${SERVICE_PIDS[@]:-}"; do
        if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
            kill "$pid" 2>/dev/null || true
            wait "$pid" 2>/dev/null || true
        fi
    done

    SERVICE_PIDS=()
}

cleanup() {
    stop_services
}

trap cleanup EXIT

wait_for_http() {
    local url="$1"
    local process_id="$2"
    local log_path="$3"
    local service_name="$4"

    for attempt in {1..80}; do
        if curl -fsS "$url" > /dev/null 2>&1; then
            return 0
        fi

        if ! kill -0 "$process_id" 2>/dev/null; then
            echo "$service_name exited before becoming healthy. See $log_path" >&2
            exit 1
        fi

        sleep 0.2

        if [[ "$attempt" -eq 80 ]]; then
            echo "Timed out waiting for $service_name to become healthy. See $log_path" >&2
            exit 1
        fi
    done
}

prepare_workspace() {
    local workspace="$1"
    local artifact_dir="$2"

    rm -rf "$workspace" "$artifact_dir"
    mkdir -p "$workspace" "$artifact_dir"
}

run_build() {
    dotnet build "$PROJECT_ROOT/ecommerce-systems-lab.sln" --no-restore > "$BUILD_LOG_PATH" 2>&1
}

seed_workspace() {
    local workspace="$1"
    local artifact_dir="$2"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/SeedData --no-build -- \
        --products "$PRODUCT_COUNT" \
        --users "$USER_COUNT" \
        --reset true \
        --sync-replicas true \
        --replica-east-lag-ms 0 \
        --replica-west-lag-ms 0 > "$artifact_dir/seed-data.txt" 2>&1
}

start_catalog() {
    local workspace="$1"
    local region="$2"
    local url="$3"
    local artifact_dir="$4"
    local label="$5"
    local stdout_log="$artifact_dir/${label}.stdout.log"

    LAB__Repository__RootPath="$workspace" \
    LAB__Regions__CurrentRegion="$region" \
    LAB__Regions__PrimaryRegion="$PRIMARY_REGION" \
    LAB__Regions__EastReplicaRegion="$EAST_REGION" \
    LAB__Regions__WestReplicaRegion="$WEST_REGION" \
    dotnet run --project src/Catalog.Api --no-build -- --urls "$url" > "$stdout_log" 2>&1 &

    local pid="$!"
    register_pid "$pid"
    wait_for_http "$url/" "$pid" "$stdout_log" "Catalog.Api ($label)"
}

start_storefront() {
    local workspace="$1"
    local storefront_region="$2"
    local catalog_region="$3"
    local catalog_url="$4"
    local storefront_url="$5"
    local artifact_dir="$6"
    local label="$7"
    local stdout_log="$artifact_dir/${label}.stdout.log"

    LAB__Repository__RootPath="$workspace" \
    LAB__Regions__CurrentRegion="$storefront_region" \
    LAB__Regions__PrimaryRegion="$PRIMARY_REGION" \
    LAB__Regions__EastReplicaRegion="$EAST_REGION" \
    LAB__Regions__WestReplicaRegion="$WEST_REGION" \
    LAB__Regions__SameRegionLatencyMs="$SAME_REGION_LATENCY_MS" \
    LAB__Regions__CrossRegionLatencyMs="$CROSS_REGION_LATENCY_MS" \
    LAB__ServiceEndpoints__CatalogBaseUrl="$catalog_url" \
    LAB__ServiceEndpoints__CatalogRegion="$catalog_region" \
    dotnet run --project src/Storefront.Api --no-build -- --urls "$storefront_url" > "$stdout_log" 2>&1 &

    local pid="$!"
    register_pid "$pid"
    wait_for_http "$storefront_url/health" "$pid" "$stdout_log" "Storefront.Api ($label)"
}

capture_get_json() {
    local url="$1"
    local run_id="$2"
    local correlation_id="$3"
    local body_path="$4"
    local headers_path="$5"
    local expected_status="${6:-200}"
    local status_code

    status_code="$(curl -sS -D "$headers_path" -o "$body_path" -w '%{http_code}' \
        "$url" \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $correlation_id" \
        -H "X-Debug-Telemetry: true")"

    if [[ "$status_code" != "$expected_status" ]]; then
        echo "GET $url returned HTTP $status_code. See $body_path" >&2
        exit 1
    fi
}

warm_storefront() {
    local storefront_url="$1"
    local run_id="$2"
    local artifact_dir="$3"
    local label="$4"

    capture_get_json \
        "$storefront_url/products/$WARMUP_PRODUCT_ID?cache=off&readSource=local" \
        "$run_id" \
        "$label-warmup-correlation" \
        "$artifact_dir/${run_id}-warmup-response.json" \
        "$artifact_dir/${run_id}-warmup-response-headers.txt"
}

run_product_mix() {
    local workspace="$1"
    local storefront_url="$2"
    local run_id="$3"
    local artifact_dir="$4"
    local label="$5"
    local -a loadgen_pids=()
    local product_id

    for product_id in "${MEASURED_PRODUCTS[@]}"; do
        LAB__Repository__RootPath="$workspace" \
        dotnet run --project src/LoadGen --no-build -- \
            --target-url "$storefront_url/products/$product_id?cache=on&readSource=local" \
            --method GET \
            --rps "$RPS_PER_PRODUCT" \
            --duration-seconds "$DURATION_SECONDS" \
            --concurrency-cap "$CONCURRENCY_CAP" \
            --run-id "$run_id" > "$artifact_dir/${label}-${product_id}-loadgen.txt" 2>&1 &
        loadgen_pids+=("$!")
    done

    local failed=0
    local pid
    for pid in "${loadgen_pids[@]}"; do
        if ! wait "$pid"; then
            failed=1
        fi
    done

    if [[ "$failed" -ne 0 ]]; then
        echo "Product-mix load generation failed for $label." >&2
        exit 1
    fi
}

capture_sample() {
    local storefront_url="$1"
    local run_id="$2"
    local artifact_dir="$3"
    local label="$4"

    capture_get_json \
        "$storefront_url/products/$SAMPLE_PRODUCT_ID?cache=off&readSource=local" \
        "$run_id" \
        "$label-sample-correlation" \
        "$artifact_dir/${run_id}-sample-response.json" \
        "$artifact_dir/${run_id}-sample-response-headers.txt"
}

analyze_run() {
    local workspace="$1"
    local run_id="$2"
    local artifact_dir="$3"
    local label="$4"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/Analyze --no-build -- \
        --run-id "$run_id" \
        --operation product-page > "$artifact_dir/${label}-analyze.txt" 2>&1

    cp "$workspace/logs/runs/$run_id/summary.json" "$artifact_dir/${run_id}-summary.json"
    cp "$workspace/analysis/$run_id/report.md" "$artifact_dir/${run_id}-analysis.md"
}

copy_workspace_logs() {
    local workspace="$1"
    local artifact_dir="$2"

    if [[ -f "$workspace/logs/requests.jsonl" ]]; then
        cp "$workspace/logs/requests.jsonl" "$artifact_dir/requests.jsonl"
    fi

    if [[ -f "$workspace/logs/jobs.jsonl" ]]; then
        cp "$workspace/logs/jobs.jsonl" "$artifact_dir/jobs.jsonl"
    fi

    if [[ -d "$workspace/logs" ]]; then
        local file
        for file in "$workspace"/logs/*.log; do
            if [[ -f "$file" ]]; then
                cp "$file" "$artifact_dir/$(basename "$file")"
            fi
        done
    fi

    if [[ -f "$workspace/data/primary.db" ]]; then
        cp "$workspace/data/primary.db" "$artifact_dir/primary.db"
    fi

    if [[ -f "$workspace/data/replica-east.db" ]]; then
        cp "$workspace/data/replica-east.db" "$artifact_dir/replica-east.db"
    fi

    if [[ -f "$workspace/data/replica-west.db" ]]; then
        cp "$workspace/data/replica-west.db" "$artifact_dir/replica-west.db"
    fi
}

run_scenario() {
    local workspace="$1"
    local artifact_dir="$2"
    local storefront_region="$3"
    local catalog_region="$4"
    local storefront_url="$5"
    local catalog_url="$6"
    local run_id="$7"
    local warmup_run_id="$8"
    local sample_run_id="$9"
    local label="${10}"

    prepare_workspace "$workspace" "$artifact_dir"
    seed_workspace "$workspace" "$artifact_dir"
    start_catalog "$workspace" "$catalog_region" "$catalog_url" "$artifact_dir" "$label-catalog"
    start_storefront "$workspace" "$storefront_region" "$catalog_region" "$catalog_url" "$storefront_url" "$artifact_dir" "$label-storefront"
    warm_storefront "$storefront_url" "$warmup_run_id" "$artifact_dir" "$label"
    run_product_mix "$workspace" "$storefront_url" "$run_id" "$artifact_dir" "$label"
    analyze_run "$workspace" "$run_id" "$artifact_dir" "$label"
    capture_sample "$storefront_url" "$sample_run_id" "$artifact_dir" "$label"
    copy_workspace_logs "$workspace" "$artifact_dir"
    stop_services
}

compose_comparison() {
    python3 - \
        "$COMPARISON_PATH" \
        "$EAST_LOCAL_ARTIFACT_ROOT" \
        "$WEST_LOCAL_ARTIFACT_ROOT" \
        "$WEST_FORCED_EAST_ARTIFACT_ROOT" \
        "$EAST_LOCAL_RUN_ID" \
        "$WEST_LOCAL_RUN_ID" \
        "$WEST_FORCED_EAST_RUN_ID" \
        "$EAST_LOCAL_SAMPLE_RUN_ID" \
        "$WEST_LOCAL_SAMPLE_RUN_ID" \
        "$WEST_FORCED_EAST_SAMPLE_RUN_ID" \
        "$MEASURED_PRODUCT_COUNT" \
        "$RPS_PER_PRODUCT" \
        "$DURATION_SECONDS" \
        "$CONCURRENCY_CAP" \
        "$PRODUCT_COUNT" <<'PY'
import json
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

comparison_path = Path(sys.argv[1])
scenario_dirs = {
    "eastLocal": Path(sys.argv[2]),
    "westLocal": Path(sys.argv[3]),
    "westForcedEast": Path(sys.argv[4]),
}
east_local_run_id = sys.argv[5]
west_local_run_id = sys.argv[6]
west_forced_east_run_id = sys.argv[7]
east_local_sample_run_id = sys.argv[8]
west_local_sample_run_id = sys.argv[9]
west_forced_east_sample_run_id = sys.argv[10]
measured_product_count = int(sys.argv[11])
rps_per_product = int(sys.argv[12])
duration_seconds = int(sys.argv[13])
concurrency_cap = int(sys.argv[14])
product_count = int(sys.argv[15])

scenario_files = {
    "eastLocal": {
        "run_id": east_local_run_id,
        "summary": f"{east_local_run_id}-summary.json",
        "sample": f"{east_local_sample_run_id}-sample-response.json",
    },
    "westLocal": {
        "run_id": west_local_run_id,
        "summary": f"{west_local_run_id}-summary.json",
        "sample": f"{west_local_sample_run_id}-sample-response.json",
    },
    "westForcedEast": {
        "run_id": west_forced_east_run_id,
        "summary": f"{west_forced_east_run_id}-summary.json",
        "sample": f"{west_forced_east_sample_run_id}-sample-response.json",
    },
}

def load_json(path: Path):
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)

def percentile(values, fraction):
    if not values:
        return None

    ordered = sorted(values)
    index = round((len(ordered) - 1) * fraction)
    return ordered[index]

def pct_change(new_value, old_value):
    if new_value is None or old_value in (None, 0):
        return None
    return ((new_value - old_value) / old_value) * 100.0

def analyze_requests(requests_path: Path, run_id: str):
    traces = []
    with requests_path.open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            record = json.loads(line)
            if record.get("service") == "Storefront.Api" and record.get("operation") == "product-page" and record.get("runId") == run_id:
                traces.append(record)

    misses = [trace for trace in traces if trace.get("dependencyCalls")]
    dependency_elapsed = [trace["dependencyCalls"][0]["elapsedMs"] for trace in misses]
    dependency_share = [
        (trace["dependencyCalls"][0]["elapsedMs"] / trace["latencyMs"]) * 100.0
        for trace in misses
        if trace.get("latencyMs", 0) > 0
    ]

    network_scope_counts = Counter(
        trace["dependencyCalls"][0].get("metadata", {}).get("networkScope", "unknown")
        for trace in misses
    )
    read_target_region_counts = Counter(
        trace["dependencyCalls"][0].get("metadata", {}).get("readTargetRegion")
        or trace["dependencyCalls"][0].get("metadata", {}).get("targetRegion")
        or "unknown"
        for trace in misses
    )
    read_source_counts = Counter(trace.get("readSource") or "unknown" for trace in traces)

    return {
        "requestCount": len(traces),
        "missRequestCount": len(misses),
        "readSourceCounts": dict(sorted(read_source_counts.items())),
        "dependencyNetworkScopeCounts": dict(sorted(network_scope_counts.items())),
        "dependencyReadTargetRegionCounts": dict(sorted(read_target_region_counts.items())),
        "averageDependencyElapsedMsOnMisses": sum(dependency_elapsed) / len(dependency_elapsed) if dependency_elapsed else None,
        "p95DependencyElapsedMsOnMisses": percentile(dependency_elapsed, 0.95),
        "averageDependencyShareOfMissLatencyPct": sum(dependency_share) / len(dependency_share) if dependency_share else None,
        "dependencyDominantMissFraction": (
            sum(1 for share in dependency_share if share >= 50.0) / len(dependency_share)
            if dependency_share else None
        ),
    }

def summarize_scenario(name: str):
    config = scenario_files[name]
    root = scenario_dirs[name]
    summary = load_json(root / config["summary"])
    sample = load_json(root / config["sample"])
    request_analysis = analyze_requests(root / "requests.jsonl", config["run_id"])

    requests = summary["requests"]
    return {
        "runId": config["run_id"],
        "requestMetrics": {
            "averageLatencyMs": requests.get("averageLatencyMs"),
            "p95LatencyMs": requests.get("p95LatencyMs"),
            "p99LatencyMs": requests.get("p99LatencyMs"),
            "throughputPerSecond": requests.get("throughputPerSecond"),
            "averageConcurrency": requests.get("averageConcurrency"),
            "cacheHitRate": requests.get("cacheHitRate"),
            "cacheMissRate": requests.get("cacheMissRate"),
            "requestCount": requests.get("requestCount"),
        },
        "requestTraceAnalysis": request_analysis,
        "sample": {
            "source": sample.get("source"),
            "readSource": sample.get("readSource"),
            "cacheMode": sample.get("cache", {}).get("mode"),
            "cacheHit": sample.get("cache", {}).get("hit"),
            "version": sample.get("version"),
            "sellableQuantity": sample.get("inventory", {}).get("sellableQuantity"),
            "staleRead": sample.get("freshness", {}).get("staleRead"),
        },
    }

east_local = summarize_scenario("eastLocal")
west_local = summarize_scenario("westLocal")
west_forced_east = summarize_scenario("westForcedEast")

comparison = {
    "generatedUtc": datetime.now(timezone.utc).isoformat(),
    "experiment": {
        "name": "same-region-vs-cross-region",
        "measuredOperation": "product-page",
        "requestedReadSource": "local",
        "cacheMode": "on",
    },
    "workload": {
        "productCount": product_count,
        "measuredProductCount": measured_product_count,
        "rpsPerProduct": rps_per_product,
        "durationSeconds": duration_seconds,
        "concurrencyCapPerProduct": concurrency_cap,
    },
    "scenarios": {
        "eastLocal": east_local,
        "westLocal": west_local,
        "westForcedEast": west_forced_east,
    },
    "deltas": {
        "westLocalVsEastLocal": {
            "averageLatencyDeltaMs": (
                (west_local["requestMetrics"]["averageLatencyMs"] or 0.0)
                - (east_local["requestMetrics"]["averageLatencyMs"] or 0.0)
            ),
            "averageLatencyChangePct": pct_change(
                west_local["requestMetrics"]["averageLatencyMs"],
                east_local["requestMetrics"]["averageLatencyMs"]),
            "p95LatencyDeltaMs": (
                (west_local["requestMetrics"]["p95LatencyMs"] or 0.0)
                - (east_local["requestMetrics"]["p95LatencyMs"] or 0.0)
            ),
            "cacheHitRateDeltaPctPoints": (
                ((west_local["requestMetrics"]["cacheHitRate"] or 0.0) * 100.0)
                - ((east_local["requestMetrics"]["cacheHitRate"] or 0.0) * 100.0)
            ),
        },
        "westForcedEastVsWestLocal": {
            "averageLatencyDeltaMs": (
                (west_forced_east["requestMetrics"]["averageLatencyMs"] or 0.0)
                - (west_local["requestMetrics"]["averageLatencyMs"] or 0.0)
            ),
            "averageLatencyChangePct": pct_change(
                west_forced_east["requestMetrics"]["averageLatencyMs"],
                west_local["requestMetrics"]["averageLatencyMs"]),
            "p95LatencyDeltaMs": (
                (west_forced_east["requestMetrics"]["p95LatencyMs"] or 0.0)
                - (west_local["requestMetrics"]["p95LatencyMs"] or 0.0)
            ),
            "p95LatencyChangePct": pct_change(
                west_forced_east["requestMetrics"]["p95LatencyMs"],
                west_local["requestMetrics"]["p95LatencyMs"]),
            "cacheHitRateDeltaPctPoints": (
                ((west_forced_east["requestMetrics"]["cacheHitRate"] or 0.0) * 100.0)
                - ((west_local["requestMetrics"]["cacheHitRate"] or 0.0) * 100.0)
            ),
            "averageDependencyElapsedDeltaMsOnMisses": (
                (west_forced_east["requestTraceAnalysis"]["averageDependencyElapsedMsOnMisses"] or 0.0)
                - (west_local["requestTraceAnalysis"]["averageDependencyElapsedMsOnMisses"] or 0.0)
            ),
            "averageDependencyShareDeltaPctPoints": (
                (west_forced_east["requestTraceAnalysis"]["averageDependencyShareOfMissLatencyPct"] or 0.0)
                - (west_local["requestTraceAnalysis"]["averageDependencyShareOfMissLatencyPct"] or 0.0)
            ),
        },
    },
}

comparison_path.write_text(json.dumps(comparison, indent=2) + "\n", encoding="utf-8")
PY
}

main() {
    mkdir -p "$ARTIFACT_ROOT" "$WORKSPACE_ROOT"

    run_build

    run_scenario \
        "$EAST_LOCAL_WORKSPACE" \
        "$EAST_LOCAL_ARTIFACT_ROOT" \
        "$EAST_REGION" \
        "$EAST_REGION" \
        "$EAST_LOCAL_STOREFRONT_URL" \
        "$EAST_LOCAL_CATALOG_URL" \
        "$EAST_LOCAL_RUN_ID" \
        "$EAST_LOCAL_WARMUP_RUN_ID" \
        "$EAST_LOCAL_SAMPLE_RUN_ID" \
        "east-local"

    run_scenario \
        "$WEST_LOCAL_WORKSPACE" \
        "$WEST_LOCAL_ARTIFACT_ROOT" \
        "$WEST_REGION" \
        "$WEST_REGION" \
        "$WEST_LOCAL_STOREFRONT_URL" \
        "$WEST_LOCAL_CATALOG_URL" \
        "$WEST_LOCAL_RUN_ID" \
        "$WEST_LOCAL_WARMUP_RUN_ID" \
        "$WEST_LOCAL_SAMPLE_RUN_ID" \
        "west-local"

    run_scenario \
        "$WEST_FORCED_EAST_WORKSPACE" \
        "$WEST_FORCED_EAST_ARTIFACT_ROOT" \
        "$WEST_REGION" \
        "$EAST_REGION" \
        "$WEST_FORCED_EAST_STOREFRONT_URL" \
        "$WEST_FORCED_EAST_CATALOG_URL" \
        "$WEST_FORCED_EAST_RUN_ID" \
        "$WEST_FORCED_EAST_WARMUP_RUN_ID" \
        "$WEST_FORCED_EAST_SAMPLE_RUN_ID" \
        "west-forced-east"

    compose_comparison
    jq '.' "$COMPARISON_PATH" > "$PRETTY_COMPARISON_PATH"

    echo "Milestone 9 experiment complete."
    echo "Comparison JSON: $COMPARISON_PATH"
}

main "$@"
