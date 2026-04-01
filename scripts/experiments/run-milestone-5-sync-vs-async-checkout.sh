#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXPERIMENT_ROOT="$PROJECT_ROOT/docs/experiments/milestone-5-sync-vs-async-checkout"
WORKSPACE_ROOT="${LAB_EXPERIMENT_ROOT:-$EXPERIMENT_ROOT/workspace}"
SYNC_WORKSPACE="$WORKSPACE_ROOT/sync"
ASYNC_WORKSPACE="$WORKSPACE_ROOT/async"
ARTIFACT_ROOT="$EXPERIMENT_ROOT/artifacts"
SYNC_ARTIFACT_ROOT="$ARTIFACT_ROOT/sync"
ASYNC_ARTIFACT_ROOT="$ARTIFACT_ROOT/async"

CART_URL="${LAB_CART_URL:-http://127.0.0.1:5085}"
PAYMENT_URL="${LAB_PAYMENT_URL:-http://127.0.0.1:5086}"
ORDER_URL="${LAB_ORDER_URL:-http://127.0.0.1:5087}"
STOREFRONT_URL="${LAB_STOREFRONT_URL:-http://127.0.0.1:5088}"

MEASURED_REQUEST_COUNT="${LAB_MEASURED_REQUEST_COUNT:-6}"
PRODUCT_COUNT="${LAB_EXPERIMENT_PRODUCT_COUNT:-8}"
USER_COUNT="${LAB_EXPERIMENT_USER_COUNT:-8}"
ITEM_QUANTITY="${LAB_EXPERIMENT_ITEM_QUANTITY:-1}"
PAYMENT_MODE="${LAB_EXPERIMENT_PAYMENT_MODE:-slow_success}"
SLOW_LATENCY_MS="${LAB_EXPERIMENT_SLOW_LATENCY_MS:-350}"
FAST_LATENCY_MS="${LAB_EXPERIMENT_FAST_LATENCY_MS:-5}"
TIMEOUT_LATENCY_MS="${LAB_EXPERIMENT_TIMEOUT_LATENCY_MS:-700}"
DELAYED_CONFIRMATION_MS="${LAB_EXPERIMENT_DELAYED_CONFIRMATION_MS:-300}"
DUPLICATE_CALLBACK_SPACING_MS="${LAB_EXPERIMENT_DUPLICATE_CALLBACK_SPACING_MS:-50}"
DISPATCHER_POLL_MS="${LAB_EXPERIMENT_DISPATCHER_POLL_MS:-20}"
QUEUE_DRAIN_TIMEOUT_SECONDS="${LAB_QUEUE_DRAIN_TIMEOUT_SECONDS:-60}"

SYNC_RUN_ID="${LAB_SYNC_RUN_ID:-milestone-5-storefront-sync-slow}"
ASYNC_RUN_ID="${LAB_ASYNC_RUN_ID:-milestone-5-storefront-async-slow}"
SYNC_WARMUP_RUN_ID="${LAB_SYNC_WARMUP_RUN_ID:-milestone-5-storefront-sync-warmup}"
ASYNC_WARMUP_RUN_ID="${LAB_ASYNC_WARMUP_RUN_ID:-milestone-5-storefront-async-warmup}"

CART_PID=""
PAYMENT_PID=""
ORDER_PID=""
STOREFRONT_PID=""
WORKER_PID=""

reset_service_pids() {
    CART_PID=""
    PAYMENT_PID=""
    ORDER_PID=""
    STOREFRONT_PID=""
    WORKER_PID=""
}

stop_services() {
    local pid

    for pid in "$WORKER_PID" "$STOREFRONT_PID" "$ORDER_PID" "$PAYMENT_PID" "$CART_PID"; do
        if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
            kill "$pid" 2>/dev/null || true
            wait "$pid" 2>/dev/null || true
        fi
    done

    reset_service_pids
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
    mkdir -p "$workspace" "$artifact_dir" "$artifact_dir/responses" "$artifact_dir/headers" "$artifact_dir/status"
}

seed_workspace() {
    local workspace="$1"
    local artifact_dir="$2"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/SeedData --no-build -- \
        --products "$PRODUCT_COUNT" \
        --users "$USER_COUNT" \
        --reset true > "$artifact_dir/seed-data.txt" 2>&1
}

start_core_services() {
    local workspace="$1"
    local artifact_dir="$2"

    stop_services

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/Cart.Api --no-build --urls "$CART_URL" > "$artifact_dir/cart.log" 2>&1 &
    CART_PID=$!

    LAB__Repository__RootPath="$workspace" \
    LAB__PaymentSimulator__FastLatencyMilliseconds="$FAST_LATENCY_MS" \
    LAB__PaymentSimulator__SlowLatencyMilliseconds="$SLOW_LATENCY_MS" \
    LAB__PaymentSimulator__TimeoutLatencyMilliseconds="$TIMEOUT_LATENCY_MS" \
    LAB__PaymentSimulator__DelayedConfirmationMilliseconds="$DELAYED_CONFIRMATION_MS" \
    LAB__PaymentSimulator__DuplicateCallbackSpacingMilliseconds="$DUPLICATE_CALLBACK_SPACING_MS" \
    LAB__PaymentSimulator__DispatcherPollMilliseconds="$DISPATCHER_POLL_MS" \
    dotnet run --project src/PaymentSimulator.Api --no-build --urls "$PAYMENT_URL" > "$artifact_dir/payment-simulator.log" 2>&1 &
    PAYMENT_PID=$!

    LAB__Repository__RootPath="$workspace" \
    LAB__ServiceEndpoints__PaymentSimulatorBaseUrl="$PAYMENT_URL" \
    dotnet run --project src/Order.Api --no-build --urls "$ORDER_URL" > "$artifact_dir/order.log" 2>&1 &
    ORDER_PID=$!

    LAB__Repository__RootPath="$workspace" \
    LAB__ServiceEndpoints__OrderBaseUrl="$ORDER_URL" \
    dotnet run --project src/Storefront.Api --no-build --urls "$STOREFRONT_URL" > "$artifact_dir/storefront.log" 2>&1 &
    STOREFRONT_PID=$!

    wait_for_http "$CART_URL/" "$CART_PID" "$artifact_dir/cart.log" "Cart.Api"
    wait_for_http "$PAYMENT_URL/" "$PAYMENT_PID" "$artifact_dir/payment-simulator.log" "PaymentSimulator.Api"
    wait_for_http "$ORDER_URL/" "$ORDER_PID" "$artifact_dir/order.log" "Order.Api"
    wait_for_http "$STOREFRONT_URL/health" "$STOREFRONT_PID" "$artifact_dir/storefront.log" "Storefront.Api"
}

start_worker() {
    local workspace="$1"
    local artifact_dir="$2"

    LAB__Repository__RootPath="$workspace" \
    LAB__Queue__PollIntervalMilliseconds=50 \
    LAB__ServiceEndpoints__PaymentSimulatorBaseUrl="$PAYMENT_URL" \
    dotnet run --project src/Worker --no-build > "$artifact_dir/worker.log" 2>&1 &
    WORKER_PID=$!
}

wait_for_queue_idle() {
    local workspace="$1"
    local run_id="$2"
    local timeout_seconds="$3"

    python3 - "$workspace" "$run_id" "$timeout_seconds" <<'PY'
import json
import sqlite3
import sys
import time
from pathlib import Path

workspace = Path(sys.argv[1])
run_id = sys.argv[2]
timeout_seconds = int(sys.argv[3])
database_path = workspace / "data" / "primary.db"
deadline = time.time() + timeout_seconds

def count_active_jobs():
    connection = sqlite3.connect(database_path)
    try:
        cursor = connection.execute("select payload_json, status from queue_jobs")
        count = 0
        for payload_json, status in cursor.fetchall():
            try:
                payload = json.loads(payload_json)
            except Exception:
                continue

            if payload.get("runId") != run_id:
                continue

            if status in {"pending", "inprogress"}:
                count += 1

        return count
    finally:
        connection.close()

while time.time() <= deadline:
    if count_active_jobs() == 0:
        sys.exit(0)

    time.sleep(0.2)

print(f"Timed out waiting for queue to become idle for run '{run_id}'.", file=sys.stderr)
sys.exit(1)
PY
}

capture_queue_state() {
    local workspace="$1"
    local run_id="$2"
    local output_path="$3"

    python3 - "$workspace" "$run_id" "$output_path" <<'PY'
import json
import sqlite3
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

workspace = Path(sys.argv[1])
run_id = sys.argv[2]
output_path = Path(sys.argv[3])
database_path = workspace / "data" / "primary.db"

rows = []
connection = sqlite3.connect(database_path)
try:
    cursor = connection.execute(
        "select queue_job_id, job_type, status, enqueued_utc, available_utc, started_utc, completed_utc, payload_json from queue_jobs")
    rows = cursor.fetchall()
finally:
    connection.close()

filtered = []
for queue_job_id, job_type, status, enqueued_utc, available_utc, started_utc, completed_utc, payload_json in rows:
    try:
        payload = json.loads(payload_json)
    except Exception:
        continue

    if payload.get("runId") != run_id:
        continue

    filtered.append(
        {
            "queueJobId": queue_job_id,
            "jobType": job_type,
            "status": status,
            "enqueuedUtc": enqueued_utc,
            "availableUtc": available_utc,
            "startedUtc": started_utc,
            "completedUtc": completed_utc,
        }
    )

status_counts = Counter(item["status"] for item in filtered)
job_type_status_counts = Counter(f'{item["jobType"]}|{item["status"]}' for item in filtered)
oldest_enqueued = min((item["enqueuedUtc"] for item in filtered), default=None)

oldest_age_ms = None
if oldest_enqueued:
    now_utc = datetime.now(timezone.utc)
    oldest_dt = datetime.fromisoformat(oldest_enqueued.replace("Z", "+00:00"))
    oldest_age_ms = max(0.0, (now_utc - oldest_dt).total_seconds() * 1000.0)

output = {
    "snapshotUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    "runId": run_id,
    "matchingJobCount": len(filtered),
    "statusCounts": dict(status_counts),
    "jobTypeStatusCounts": dict(job_type_status_counts),
    "oldestQueuedEnqueuedUtc": oldest_enqueued,
    "oldestQueuedAgeMs": oldest_age_ms,
}

output_path.parent.mkdir(parents=True, exist_ok=True)
output_path.write_text(json.dumps(output, indent=2), encoding="utf-8")
PY
}

add_cart_item() {
    local run_id="$1"
    local user_id="$2"
    local product_id="$3"
    local quantity="$4"
    local artifact_dir="$5"
    local body_path="$artifact_dir/responses/${run_id}-cart-${user_id}.json"
    local headers_path="$artifact_dir/headers/${run_id}-cart-${user_id}.txt"
    local status_code

    status_code="$(curl -sS -D "$headers_path" -o "$body_path" -w '%{http_code}' \
        -X POST "$CART_URL/cart/items" \
        -H 'Content-Type: application/json' \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $run_id-cart-$user_id" \
        -d "{\"userId\":\"$user_id\",\"productId\":\"$product_id\",\"quantity\":$quantity}")"

    if [[ "$status_code" != "200" ]]; then
        echo "Cart setup failed for $user_id/$product_id with HTTP $status_code. See $body_path" >&2
        exit 1
    fi
}

prepare_carts() {
    local run_id="$1"
    local count="$2"
    local artifact_dir="$3"

    local index user_id product_id
    for index in $(seq 1 "$count"); do
        printf -v user_id 'user-%04d' "$index"
        printf -v product_id 'sku-%04d' "$index"
        add_cart_item "$run_id" "$user_id" "$product_id" "$ITEM_QUANTITY" "$artifact_dir"
    done
}

warm_up_checkout_path() {
    local run_id="$1"
    local user_index="$2"
    local mode="$3"
    local artifact_dir="$4"
    local user_id product_id expected_status status_code body_path

    printf -v user_id 'user-%04d' "$user_index"
    printf -v product_id 'sku-%04d' "$user_index"
    expected_status="$([[ "$mode" == "async" ]] && echo "202" || echo "200")"
    body_path="$artifact_dir/responses/${run_id}-warmup.json"

    add_cart_item "${run_id}-setup" "$user_id" "$product_id" "$ITEM_QUANTITY" "$artifact_dir"

    status_code="$(curl -sS -o "$body_path" -w '%{http_code}' \
        -X POST "$STOREFRONT_URL/checkout?mode=$mode" \
        -H 'Content-Type: application/json' \
        -H 'X-Debug-Telemetry: true' \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $run_id-warmup" \
        -H "Idempotency-Key: idem-$run_id" \
        -d "{\"userId\":\"$user_id\",\"paymentMode\":\"$PAYMENT_MODE\"}")"

    if [[ "$status_code" != "$expected_status" ]]; then
        echo "Warm-up checkout failed for $run_id with HTTP $status_code. See $body_path" >&2
        exit 1
    fi
}

run_checkout_wave() {
    local run_id="$1"
    local mode="$2"
    local count="$3"
    local artifact_dir="$4"
    local expected_status
    expected_status="$([[ "$mode" == "async" ]] && echo "202" || echo "200")"

    prepare_carts "${run_id}-setup" "$count" "$artifact_dir"

    local index user_id body_path headers_path status_path status_code
    local pids=()
    local pid

    for index in $(seq 1 "$count"); do
        printf -v user_id 'user-%04d' "$index"
        body_path="$artifact_dir/responses/${run_id}-${user_id}.json"
        headers_path="$artifact_dir/headers/${run_id}-${user_id}.txt"
        status_path="$artifact_dir/status/${run_id}-${user_id}.txt"

        (
            status_code="$(curl -sS -D "$headers_path" -o "$body_path" -w '%{http_code}' \
                -X POST "$STOREFRONT_URL/checkout?mode=$mode" \
                -H 'Content-Type: application/json' \
                -H 'X-Debug-Telemetry: true' \
                -H "X-Run-Id: $run_id" \
                -H "X-Correlation-Id: $run_id-$user_id" \
                -H "Idempotency-Key: idem-$run_id-$user_id" \
                -d "{\"userId\":\"$user_id\",\"paymentMode\":\"$PAYMENT_MODE\"}")"

            printf '%s\n' "$status_code" > "$status_path"

            if [[ "$status_code" != "$expected_status" ]]; then
                echo "Checkout request for $run_id/$user_id failed with HTTP $status_code. See $body_path" >&2
                exit 1
            fi
        ) &

        pids+=("$!")
    done

    local failed=0
    for pid in "${pids[@]}"; do
        if ! wait "$pid"; then
            failed=1
        fi
    done

    if [[ "$failed" -ne 0 ]]; then
        exit 1
    fi
}

summarize_responses() {
    local artifact_dir="$1"
    local run_id="$2"

    python3 - "$artifact_dir" "$run_id" <<'PY'
import glob
import json
import sys
from collections import Counter
from pathlib import Path

artifact_dir = Path(sys.argv[1])
run_id = sys.argv[2]

body_paths = sorted(glob.glob(str(artifact_dir / "responses" / f"{run_id}-user-*.json")))
status_paths = sorted(glob.glob(str(artifact_dir / "status" / f"{run_id}-user-*.txt")))

summary = {
    "runId": run_id,
    "requestCount": len(body_paths),
    "httpStatusCounts": {},
    "checkoutStatusCounts": {},
    "paymentStatusCounts": {},
    "paymentOutcomeCounts": {},
    "checkoutModeCounts": {},
    "contractSatisfiedCounts": {},
    "backgroundJobIdPresentCount": 0,
}

http_status_counts = Counter()
checkout_status_counts = Counter()
payment_status_counts = Counter()
payment_outcome_counts = Counter()
checkout_mode_counts = Counter()
contract_satisfied_counts = Counter()
background_job_present_count = 0

for path in status_paths:
    http_status_counts[Path(path).read_text(encoding="utf-8").strip()] += 1

for path in body_paths:
    payload = json.loads(Path(path).read_text(encoding="utf-8"))
    checkout_status_counts[str(payload.get("status"))] += 1
    payment_status_counts[str(payload.get("paymentStatus"))] += 1
    payment_outcome_counts[str(payload.get("paymentOutcome"))] += 1
    checkout_mode_counts[str(payload.get("checkoutMode"))] += 1
    contract_satisfied_counts[str(payload.get("contractSatisfied"))] += 1
    if payload.get("backgroundJobId"):
        background_job_present_count += 1

summary["httpStatusCounts"] = dict(http_status_counts)
summary["checkoutStatusCounts"] = dict(checkout_status_counts)
summary["paymentStatusCounts"] = dict(payment_status_counts)
summary["paymentOutcomeCounts"] = dict(payment_outcome_counts)
summary["checkoutModeCounts"] = dict(checkout_mode_counts)
summary["contractSatisfiedCounts"] = dict(contract_satisfied_counts)
summary["backgroundJobIdPresentCount"] = background_job_present_count

output_path = artifact_dir / f"{run_id}-response-summary.json"
output_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")
PY
}

analyze_run() {
    local workspace="$1"
    local run_id="$2"
    local operation="$3"
    local artifact_dir="$4"
    local phase="$5"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/Analyze --no-build -- \
        --run-id "$run_id" \
        --operation "$operation" > "$artifact_dir/${run_id}-${phase}-analyze.txt" 2>&1

    cp "$workspace/logs/runs/$run_id/summary.json" "$artifact_dir/${run_id}-${phase}-summary.json"
    cp "$workspace/analysis/$run_id/report.md" "$artifact_dir/${run_id}-${phase}-report.md"
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
}

run_scenario() {
    local scenario_name="$1"
    local workspace="$2"
    local artifact_dir="$3"
    local run_id="$4"
    local mode="$5"
    local warmup_run_id="$6"
    local warmup_user_index="$7"
    local operation

    operation="$([[ "$mode" == "async" ]] && echo "storefront-checkout-async" || echo "storefront-checkout-sync")"

    echo "Preparing $scenario_name workspace at $workspace"
    prepare_workspace "$workspace" "$artifact_dir"
    seed_workspace "$workspace" "$artifact_dir"
    start_core_services "$workspace" "$artifact_dir"
    warm_up_checkout_path "$warmup_run_id" "$warmup_user_index" "$mode" "$artifact_dir"

    echo "Running $scenario_name workload ($MEASURED_REQUEST_COUNT requests, payment mode $PAYMENT_MODE, checkout mode $mode)"
    run_checkout_wave "$run_id" "$mode" "$MEASURED_REQUEST_COUNT" "$artifact_dir"
    summarize_responses "$artifact_dir" "$run_id"
    capture_queue_state "$workspace" "$run_id" "$artifact_dir/${run_id}-queue-immediate.json"

    echo "Analyzing immediate $scenario_name boundary"
    analyze_run "$workspace" "$run_id" "$operation" "$artifact_dir" "immediate"

    echo "Starting worker to drain $scenario_name backlog"
    start_worker "$workspace" "$artifact_dir"
    wait_for_queue_idle "$workspace" "$run_id" "$QUEUE_DRAIN_TIMEOUT_SECONDS"
    capture_queue_state "$workspace" "$run_id" "$artifact_dir/${run_id}-queue-drained.json"

    echo "Analyzing drained $scenario_name boundary"
    analyze_run "$workspace" "$run_id" "$operation" "$artifact_dir" "drained"
    copy_workspace_logs "$workspace" "$artifact_dir"

    stop_services
}

echo "Preparing clean milestone-5 experiment roots..."
rm -rf "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"
mkdir -p "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"

cd "$PROJECT_ROOT"

echo "Building solution..."
dotnet build ecommerce-systems-lab.sln > "$ARTIFACT_ROOT/build.log" 2>&1

run_scenario "sync" "$SYNC_WORKSPACE" "$SYNC_ARTIFACT_ROOT" "$SYNC_RUN_ID" "sync" "$SYNC_WARMUP_RUN_ID" 7
run_scenario "async" "$ASYNC_WORKSPACE" "$ASYNC_ARTIFACT_ROOT" "$ASYNC_RUN_ID" "async" "$ASYNC_WARMUP_RUN_ID" 8

echo "Milestone-5 sync-vs-async checkout experiment completed."
echo "Workspaces:"
echo "  sync:   $SYNC_WORKSPACE"
echo "  async:  $ASYNC_WORKSPACE"
echo "Artifacts:"
echo "  sync:   $SYNC_ARTIFACT_ROOT"
echo "  async:  $ASYNC_ARTIFACT_ROOT"
echo "Immediate summaries:"
echo "  sync:   $SYNC_ARTIFACT_ROOT/$SYNC_RUN_ID-immediate-summary.json"
echo "  async:  $ASYNC_ARTIFACT_ROOT/$ASYNC_RUN_ID-immediate-summary.json"
echo "Drained summaries:"
echo "  sync:   $SYNC_ARTIFACT_ROOT/$SYNC_RUN_ID-drained-summary.json"
echo "  async:  $ASYNC_ARTIFACT_ROOT/$ASYNC_RUN_ID-drained-summary.json"
