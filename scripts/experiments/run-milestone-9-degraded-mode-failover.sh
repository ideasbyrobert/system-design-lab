#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXPERIMENT_ROOT="$PROJECT_ROOT/docs/experiments/milestone-9-degraded-mode-failover"
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

HEALTHY_RUN_ID="${LAB_HEALTHY_RUN_ID:-milestone-9-healthy-west-local}"
REPLICA_UNAVAILABLE_RUN_ID="${LAB_REPLICA_UNAVAILABLE_RUN_ID:-milestone-9-local-replica-unavailable}"
CATALOG_UNAVAILABLE_RUN_ID="${LAB_CATALOG_UNAVAILABLE_RUN_ID:-milestone-9-local-catalog-unavailable}"

HEALTHY_WARMUP_RUN_ID="${LAB_HEALTHY_WARMUP_RUN_ID:-milestone-9-healthy-west-local-warmup}"
REPLICA_UNAVAILABLE_WARMUP_RUN_ID="${LAB_REPLICA_UNAVAILABLE_WARMUP_RUN_ID:-milestone-9-local-replica-unavailable-warmup}"
CATALOG_UNAVAILABLE_WARMUP_RUN_ID="${LAB_CATALOG_UNAVAILABLE_WARMUP_RUN_ID:-milestone-9-local-catalog-unavailable-warmup}"

HEALTHY_SAMPLE_RUN_ID="${LAB_HEALTHY_SAMPLE_RUN_ID:-milestone-9-healthy-west-local-sample}"
REPLICA_UNAVAILABLE_SAMPLE_RUN_ID="${LAB_REPLICA_UNAVAILABLE_SAMPLE_RUN_ID:-milestone-9-local-replica-unavailable-sample}"
CATALOG_UNAVAILABLE_SAMPLE_RUN_ID="${LAB_CATALOG_UNAVAILABLE_SAMPLE_RUN_ID:-milestone-9-local-catalog-unavailable-sample}"

HEALTHY_STOREFRONT_URL="${LAB_HEALTHY_STOREFRONT_URL:-http://127.0.0.1:5161}"
HEALTHY_WEST_CATALOG_URL="${LAB_HEALTHY_WEST_CATALOG_URL:-http://127.0.0.1:5162}"
REPLICA_UNAVAILABLE_STOREFRONT_URL="${LAB_REPLICA_UNAVAILABLE_STOREFRONT_URL:-http://127.0.0.1:5163}"
REPLICA_UNAVAILABLE_WEST_CATALOG_URL="${LAB_REPLICA_UNAVAILABLE_WEST_CATALOG_URL:-http://127.0.0.1:5164}"
CATALOG_UNAVAILABLE_STOREFRONT_URL="${LAB_CATALOG_UNAVAILABLE_STOREFRONT_URL:-http://127.0.0.1:5165}"
CATALOG_UNAVAILABLE_EAST_CATALOG_URL="${LAB_CATALOG_UNAVAILABLE_EAST_CATALOG_URL:-http://127.0.0.1:5166}"
CATALOG_UNAVAILABLE_DEAD_WEST_CATALOG_URL="${LAB_CATALOG_UNAVAILABLE_DEAD_WEST_CATALOG_URL:-http://127.0.0.1:5999}"

HEALTHY_WORKSPACE="$WORKSPACE_ROOT/healthy-west-local"
REPLICA_UNAVAILABLE_WORKSPACE="$WORKSPACE_ROOT/local-replica-unavailable"
CATALOG_UNAVAILABLE_WORKSPACE="$WORKSPACE_ROOT/local-catalog-unavailable"

HEALTHY_ARTIFACT_ROOT="$ARTIFACT_ROOT/healthy-west-local"
REPLICA_UNAVAILABLE_ARTIFACT_ROOT="$ARTIFACT_ROOT/local-replica-unavailable"
CATALOG_UNAVAILABLE_ARTIFACT_ROOT="$ARTIFACT_ROOT/local-catalog-unavailable"

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
    local simulate_local_replica_unavailable="${6:-false}"
    local stdout_log="$artifact_dir/${label}.stdout.log"

    LAB__Repository__RootPath="$workspace" \
    LAB__Regions__CurrentRegion="$region" \
    LAB__Regions__PrimaryRegion="$PRIMARY_REGION" \
    LAB__Regions__EastReplicaRegion="$EAST_REGION" \
    LAB__Regions__WestReplicaRegion="$WEST_REGION" \
    LAB__Regions__SameRegionLatencyMs="$SAME_REGION_LATENCY_MS" \
    LAB__Regions__CrossRegionLatencyMs="$CROSS_REGION_LATENCY_MS" \
    LAB__RegionalDegradation__SimulateLocalReplicaUnavailable="$simulate_local_replica_unavailable" \
    dotnet run --project src/Catalog.Api --no-build -- --urls "$url" > "$stdout_log" 2>&1 &

    local pid="$!"
    register_pid "$pid"
    wait_for_http "$url/" "$pid" "$stdout_log" "Catalog.Api ($label)"
}

start_storefront() {
    local workspace="$1"
    local storefront_region="$2"
    local storefront_url="$3"
    local catalog_base_url="$4"
    local catalog_region="$5"
    local catalog_failover_base_url="$6"
    local catalog_failover_region="$7"
    local artifact_dir="$8"
    local label="$9"
    local simulate_local_replica_unavailable="${10:-false}"
    local simulate_local_catalog_unavailable="${11:-false}"
    local stdout_log="$artifact_dir/${label}.stdout.log"

    LAB__Repository__RootPath="$workspace" \
    LAB__Regions__CurrentRegion="$storefront_region" \
    LAB__Regions__PrimaryRegion="$PRIMARY_REGION" \
    LAB__Regions__EastReplicaRegion="$EAST_REGION" \
    LAB__Regions__WestReplicaRegion="$WEST_REGION" \
    LAB__Regions__SameRegionLatencyMs="$SAME_REGION_LATENCY_MS" \
    LAB__Regions__CrossRegionLatencyMs="$CROSS_REGION_LATENCY_MS" \
    LAB__ServiceEndpoints__CatalogBaseUrl="$catalog_base_url" \
    LAB__ServiceEndpoints__CatalogRegion="$catalog_region" \
    LAB__ServiceEndpoints__CatalogFailoverBaseUrl="$catalog_failover_base_url" \
    LAB__ServiceEndpoints__CatalogFailoverRegion="$catalog_failover_region" \
    LAB__RegionalDegradation__SimulateLocalReplicaUnavailable="$simulate_local_replica_unavailable" \
    LAB__RegionalDegradation__SimulateLocalCatalogUnavailable="$simulate_local_catalog_unavailable" \
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

run_healthy_local_scenario() {
    prepare_workspace "$HEALTHY_WORKSPACE" "$HEALTHY_ARTIFACT_ROOT"
    seed_workspace "$HEALTHY_WORKSPACE" "$HEALTHY_ARTIFACT_ROOT"
    start_catalog "$HEALTHY_WORKSPACE" "$WEST_REGION" "$HEALTHY_WEST_CATALOG_URL" "$HEALTHY_ARTIFACT_ROOT" "healthy-west-catalog"
    start_storefront \
        "$HEALTHY_WORKSPACE" \
        "$WEST_REGION" \
        "$HEALTHY_STOREFRONT_URL" \
        "$HEALTHY_WEST_CATALOG_URL" \
        "$WEST_REGION" \
        "$CATALOG_UNAVAILABLE_EAST_CATALOG_URL" \
        "$EAST_REGION" \
        "$HEALTHY_ARTIFACT_ROOT" \
        "healthy-west-storefront" \
        "false" \
        "false"
    warm_storefront "$HEALTHY_STOREFRONT_URL" "$HEALTHY_WARMUP_RUN_ID" "$HEALTHY_ARTIFACT_ROOT" "healthy-west-local"
    run_product_mix "$HEALTHY_WORKSPACE" "$HEALTHY_STOREFRONT_URL" "$HEALTHY_RUN_ID" "$HEALTHY_ARTIFACT_ROOT" "healthy-west-local"
    analyze_run "$HEALTHY_WORKSPACE" "$HEALTHY_RUN_ID" "$HEALTHY_ARTIFACT_ROOT" "healthy-west-local"
    capture_sample "$HEALTHY_STOREFRONT_URL" "$HEALTHY_SAMPLE_RUN_ID" "$HEALTHY_ARTIFACT_ROOT" "healthy-west-local"
    copy_workspace_logs "$HEALTHY_WORKSPACE" "$HEALTHY_ARTIFACT_ROOT"
    stop_services
}

run_local_replica_unavailable_scenario() {
    prepare_workspace "$REPLICA_UNAVAILABLE_WORKSPACE" "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT"
    seed_workspace "$REPLICA_UNAVAILABLE_WORKSPACE" "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT"
    start_catalog "$REPLICA_UNAVAILABLE_WORKSPACE" "$WEST_REGION" "$REPLICA_UNAVAILABLE_WEST_CATALOG_URL" "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT" "replica-unavailable-west-catalog" "true"
    start_storefront \
        "$REPLICA_UNAVAILABLE_WORKSPACE" \
        "$WEST_REGION" \
        "$REPLICA_UNAVAILABLE_STOREFRONT_URL" \
        "$REPLICA_UNAVAILABLE_WEST_CATALOG_URL" \
        "$WEST_REGION" \
        "$CATALOG_UNAVAILABLE_EAST_CATALOG_URL" \
        "$EAST_REGION" \
        "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT" \
        "replica-unavailable-west-storefront" \
        "true" \
        "false"
    warm_storefront "$REPLICA_UNAVAILABLE_STOREFRONT_URL" "$REPLICA_UNAVAILABLE_WARMUP_RUN_ID" "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT" "local-replica-unavailable"
    run_product_mix "$REPLICA_UNAVAILABLE_WORKSPACE" "$REPLICA_UNAVAILABLE_STOREFRONT_URL" "$REPLICA_UNAVAILABLE_RUN_ID" "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT" "local-replica-unavailable"
    analyze_run "$REPLICA_UNAVAILABLE_WORKSPACE" "$REPLICA_UNAVAILABLE_RUN_ID" "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT" "local-replica-unavailable"
    capture_sample "$REPLICA_UNAVAILABLE_STOREFRONT_URL" "$REPLICA_UNAVAILABLE_SAMPLE_RUN_ID" "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT" "local-replica-unavailable"
    copy_workspace_logs "$REPLICA_UNAVAILABLE_WORKSPACE" "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT"
    stop_services
}

run_local_catalog_unavailable_scenario() {
    prepare_workspace "$CATALOG_UNAVAILABLE_WORKSPACE" "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT"
    seed_workspace "$CATALOG_UNAVAILABLE_WORKSPACE" "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT"
    start_catalog "$CATALOG_UNAVAILABLE_WORKSPACE" "$EAST_REGION" "$CATALOG_UNAVAILABLE_EAST_CATALOG_URL" "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT" "catalog-unavailable-east-catalog"
    start_storefront \
        "$CATALOG_UNAVAILABLE_WORKSPACE" \
        "$WEST_REGION" \
        "$CATALOG_UNAVAILABLE_STOREFRONT_URL" \
        "$CATALOG_UNAVAILABLE_DEAD_WEST_CATALOG_URL" \
        "$WEST_REGION" \
        "$CATALOG_UNAVAILABLE_EAST_CATALOG_URL" \
        "$EAST_REGION" \
        "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT" \
        "catalog-unavailable-west-storefront" \
        "false" \
        "true"
    warm_storefront "$CATALOG_UNAVAILABLE_STOREFRONT_URL" "$CATALOG_UNAVAILABLE_WARMUP_RUN_ID" "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT" "local-catalog-unavailable"
    run_product_mix "$CATALOG_UNAVAILABLE_WORKSPACE" "$CATALOG_UNAVAILABLE_STOREFRONT_URL" "$CATALOG_UNAVAILABLE_RUN_ID" "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT" "local-catalog-unavailable"
    analyze_run "$CATALOG_UNAVAILABLE_WORKSPACE" "$CATALOG_UNAVAILABLE_RUN_ID" "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT" "local-catalog-unavailable"
    capture_sample "$CATALOG_UNAVAILABLE_STOREFRONT_URL" "$CATALOG_UNAVAILABLE_SAMPLE_RUN_ID" "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT" "local-catalog-unavailable"
    copy_workspace_logs "$CATALOG_UNAVAILABLE_WORKSPACE" "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT"
    stop_services
}

compose_comparison() {
    python3 - \
        "$COMPARISON_PATH" \
        "$HEALTHY_ARTIFACT_ROOT" \
        "$REPLICA_UNAVAILABLE_ARTIFACT_ROOT" \
        "$CATALOG_UNAVAILABLE_ARTIFACT_ROOT" \
        "$HEALTHY_RUN_ID" \
        "$REPLICA_UNAVAILABLE_RUN_ID" \
        "$CATALOG_UNAVAILABLE_RUN_ID" \
        "$HEALTHY_SAMPLE_RUN_ID" \
        "$REPLICA_UNAVAILABLE_SAMPLE_RUN_ID" \
        "$CATALOG_UNAVAILABLE_SAMPLE_RUN_ID" \
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
    "healthyWestLocal": Path(sys.argv[2]),
    "localReplicaUnavailable": Path(sys.argv[3]),
    "localCatalogUnavailable": Path(sys.argv[4]),
}
healthy_run_id = sys.argv[5]
replica_run_id = sys.argv[6]
catalog_run_id = sys.argv[7]
healthy_sample_run_id = sys.argv[8]
replica_sample_run_id = sys.argv[9]
catalog_sample_run_id = sys.argv[10]
measured_product_count = int(sys.argv[11])
rps_per_product = int(sys.argv[12])
duration_seconds = int(sys.argv[13])
concurrency_cap = int(sys.argv[14])
product_count = int(sys.argv[15])

scenario_files = {
    "healthyWestLocal": {
        "run_id": healthy_run_id,
        "summary": f"{healthy_run_id}-summary.json",
        "sample": f"{healthy_sample_run_id}-sample-response.json",
    },
    "localReplicaUnavailable": {
        "run_id": replica_run_id,
        "summary": f"{replica_run_id}-summary.json",
        "sample": f"{replica_sample_run_id}-sample-response.json",
    },
    "localCatalogUnavailable": {
        "run_id": catalog_run_id,
        "summary": f"{catalog_run_id}-summary.json",
        "sample": f"{catalog_sample_run_id}-sample-response.json",
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
    storefront_traces = []
    catalog_traces = []

    with requests_path.open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            record = json.loads(line)
            if record.get("runId") != run_id:
                continue
            if record.get("service") == "Storefront.Api" and record.get("operation") == "product-page":
                storefront_traces.append(record)
            elif record.get("service") == "Catalog.Api" and record.get("operation") == "catalog-product-detail":
                catalog_traces.append(record)

    misses = [trace for trace in storefront_traces if trace.get("dependencyCalls")]
    dependency_elapsed = [trace["dependencyCalls"][0]["elapsedMs"] for trace in misses]
    dependency_share = [
        (trace["dependencyCalls"][0]["elapsedMs"] / trace["latencyMs"]) * 100.0
        for trace in misses
        if trace.get("latencyMs", 0) > 0
    ]
    dependency_network_scope_counts = Counter(
        trace["dependencyCalls"][0].get("metadata", {}).get("networkScope", "unknown")
        for trace in misses
    )
    degraded_mode_reasons = Counter(
        trace["dependencyCalls"][0].get("metadata", {}).get("degradedModeReason", "none")
        for trace in misses
    )
    read_source_counts = Counter(trace.get("readSource") or "unknown" for trace in storefront_traces)

    db_query_stages = []
    for trace in catalog_traces:
        for stage in trace.get("stageTimings", []):
            if stage.get("stageName") == "db_query":
                db_query_stages.append(stage)
                break

    db_elapsed = [stage["elapsedMs"] for stage in db_query_stages]
    db_network_scope_counts = Counter(
        stage.get("metadata", {}).get("readNetworkScope", "unknown")
        for stage in db_query_stages
    )
    db_fallback_reason_counts = Counter(
        stage.get("metadata", {}).get("fallbackReason", "none")
        for stage in db_query_stages
    )
    db_delay_values = [
        float(stage.get("metadata", {}).get("readInjectedDelayMs"))
        for stage in db_query_stages
        if stage.get("metadata", {}).get("readInjectedDelayMs") is not None
    ]

    return {
        "storefrontRequestCount": len(storefront_traces),
        "missRequestCount": len(misses),
        "readSourceCounts": dict(sorted(read_source_counts.items())),
        "dependencyNetworkScopeCounts": dict(sorted(dependency_network_scope_counts.items())),
        "degradedModeReasonCounts": dict(sorted(degraded_mode_reasons.items())),
        "averageDependencyElapsedMsOnMisses": sum(dependency_elapsed) / len(dependency_elapsed) if dependency_elapsed else None,
        "p95DependencyElapsedMsOnMisses": percentile(dependency_elapsed, 0.95),
        "averageDependencyShareOfMissLatencyPct": sum(dependency_share) / len(dependency_share) if dependency_share else None,
        "catalogDbQueryAnalysis": {
            "requestCount": len(catalog_traces),
            "dbQueryCount": len(db_query_stages),
            "averageDbQueryElapsedMs": sum(db_elapsed) / len(db_elapsed) if db_elapsed else None,
            "p95DbQueryElapsedMs": percentile(db_elapsed, 0.95),
            "averageInjectedReadDelayMs": sum(db_delay_values) / len(db_delay_values) if db_delay_values else None,
            "readNetworkScopeCounts": dict(sorted(db_network_scope_counts.items())),
            "fallbackReasonCounts": dict(sorted(db_fallback_reason_counts.items())),
        },
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

healthy = summarize_scenario("healthyWestLocal")
replica_unavailable = summarize_scenario("localReplicaUnavailable")
catalog_unavailable = summarize_scenario("localCatalogUnavailable")

comparison = {
    "generatedUtc": datetime.now(timezone.utc).isoformat(),
    "experiment": {
        "name": "degraded-mode-failover",
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
        "healthyWestLocal": healthy,
        "localReplicaUnavailable": replica_unavailable,
        "localCatalogUnavailable": catalog_unavailable,
    },
    "deltas": {
        "localReplicaUnavailableVsHealthy": {
            "averageLatencyDeltaMs": replica_unavailable["requestMetrics"]["averageLatencyMs"] - healthy["requestMetrics"]["averageLatencyMs"],
            "averageLatencyChangePct": pct_change(replica_unavailable["requestMetrics"]["averageLatencyMs"], healthy["requestMetrics"]["averageLatencyMs"]),
            "p95LatencyDeltaMs": replica_unavailable["requestMetrics"]["p95LatencyMs"] - healthy["requestMetrics"]["p95LatencyMs"],
            "catalogDbQueryDeltaMs": replica_unavailable["requestTraceAnalysis"]["catalogDbQueryAnalysis"]["averageDbQueryElapsedMs"] - healthy["requestTraceAnalysis"]["catalogDbQueryAnalysis"]["averageDbQueryElapsedMs"],
            "dependencyElapsedDeltaMs": replica_unavailable["requestTraceAnalysis"]["averageDependencyElapsedMsOnMisses"] - healthy["requestTraceAnalysis"]["averageDependencyElapsedMsOnMisses"],
        },
        "localCatalogUnavailableVsHealthy": {
            "averageLatencyDeltaMs": catalog_unavailable["requestMetrics"]["averageLatencyMs"] - healthy["requestMetrics"]["averageLatencyMs"],
            "averageLatencyChangePct": pct_change(catalog_unavailable["requestMetrics"]["averageLatencyMs"], healthy["requestMetrics"]["averageLatencyMs"]),
            "p95LatencyDeltaMs": catalog_unavailable["requestMetrics"]["p95LatencyMs"] - healthy["requestMetrics"]["p95LatencyMs"],
            "catalogDbQueryDeltaMs": catalog_unavailable["requestTraceAnalysis"]["catalogDbQueryAnalysis"]["averageDbQueryElapsedMs"] - healthy["requestTraceAnalysis"]["catalogDbQueryAnalysis"]["averageDbQueryElapsedMs"],
            "dependencyElapsedDeltaMs": catalog_unavailable["requestTraceAnalysis"]["averageDependencyElapsedMsOnMisses"] - healthy["requestTraceAnalysis"]["averageDependencyElapsedMsOnMisses"],
        },
    },
}

comparison_path.parent.mkdir(parents=True, exist_ok=True)
with comparison_path.open("w", encoding="utf-8") as handle:
    json.dump(comparison, handle, indent=2)
    handle.write("\n")

with comparison_path.with_name("comparison-pretty.txt").open("w", encoding="utf-8") as handle:
    handle.write(json.dumps(comparison, indent=2))
    handle.write("\n")
PY
}

mkdir -p "$ARTIFACT_ROOT"
run_build
run_healthy_local_scenario
run_local_replica_unavailable_scenario
run_local_catalog_unavailable_scenario
compose_comparison

echo "Milestone 9 degraded-mode experiment artifacts generated at $ARTIFACT_ROOT"
