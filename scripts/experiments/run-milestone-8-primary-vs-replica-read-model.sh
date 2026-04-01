#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXPERIMENT_ROOT="$PROJECT_ROOT/docs/experiments/milestone-8-primary-vs-replica-read-model"
WORKSPACE_ROOT="${LAB_EXPERIMENT_ROOT:-$EXPERIMENT_ROOT/workspace}"
ARTIFACT_ROOT="$EXPERIMENT_ROOT/artifacts"
COMPARISON_PATH="$ARTIFACT_ROOT/comparison.json"

PRODUCT_WORKSPACE="$WORKSPACE_ROOT/product-reads"
ORDER_HISTORY_WORKSPACE="$WORKSPACE_ROOT/order-history"

PRODUCT_ARTIFACT_ROOT="$ARTIFACT_ROOT/product-reads"
ORDER_HISTORY_ARTIFACT_ROOT="$ARTIFACT_ROOT/order-history"

PRODUCT_STOREFRONT_URL="${LAB_PRODUCT_STOREFRONT_URL:-http://127.0.0.1:5101}"
PRODUCT_CATALOG_URL="${LAB_PRODUCT_CATALOG_URL:-http://127.0.0.1:5102}"

ORDER_HISTORY_STOREFRONT_URL="${LAB_ORDER_HISTORY_STOREFRONT_URL:-http://127.0.0.1:5111}"
ORDER_HISTORY_CART_URL="${LAB_ORDER_HISTORY_CART_URL:-http://127.0.0.1:5112}"
ORDER_HISTORY_PAYMENT_URL="${LAB_ORDER_HISTORY_PAYMENT_URL:-http://127.0.0.1:5113}"
ORDER_HISTORY_ORDER_URL="${LAB_ORDER_HISTORY_ORDER_URL:-http://127.0.0.1:5114}"

PRODUCT_PRIMARY_RUN_ID="${LAB_PRODUCT_PRIMARY_RUN_ID:-milestone-8-product-primary}"
PRODUCT_REPLICA_RUN_ID="${LAB_PRODUCT_REPLICA_RUN_ID:-milestone-8-product-replica-east}"
PRODUCT_PRIMARY_WARMUP_RUN_ID="${LAB_PRODUCT_PRIMARY_WARMUP_RUN_ID:-milestone-8-product-primary-warmup}"
PRODUCT_REPLICA_WARMUP_RUN_ID="${LAB_PRODUCT_REPLICA_WARMUP_RUN_ID:-milestone-8-product-replica-east-warmup}"
PRODUCT_PRIMARY_SAMPLE_RUN_ID="${LAB_PRODUCT_PRIMARY_SAMPLE_RUN_ID:-milestone-8-product-primary-sample}"
PRODUCT_REPLICA_SAMPLE_RUN_ID="${LAB_PRODUCT_REPLICA_SAMPLE_RUN_ID:-milestone-8-product-replica-east-sample}"

ORDER_HISTORY_PRIMARY_RUN_ID="${LAB_ORDER_HISTORY_PRIMARY_RUN_ID:-milestone-8-order-history-primary-projection}"
ORDER_HISTORY_READ_MODEL_RUN_ID="${LAB_ORDER_HISTORY_READ_MODEL_RUN_ID:-milestone-8-order-history-read-model}"
ORDER_HISTORY_BASELINE_RUN_ID="${LAB_ORDER_HISTORY_BASELINE_RUN_ID:-milestone-8-order-history-baseline}"
ORDER_HISTORY_STALE_SEED_RUN_ID="${LAB_ORDER_HISTORY_STALE_SEED_RUN_ID:-milestone-8-order-history-stale-seed}"
ORDER_HISTORY_PRIMARY_WARMUP_RUN_ID="${LAB_ORDER_HISTORY_PRIMARY_WARMUP_RUN_ID:-milestone-8-order-history-primary-projection-warmup}"
ORDER_HISTORY_READ_MODEL_WARMUP_RUN_ID="${LAB_ORDER_HISTORY_READ_MODEL_WARMUP_RUN_ID:-milestone-8-order-history-read-model-warmup}"
ORDER_HISTORY_PRIMARY_SAMPLE_RUN_ID="${LAB_ORDER_HISTORY_PRIMARY_SAMPLE_RUN_ID:-milestone-8-order-history-primary-projection-sample}"
ORDER_HISTORY_READ_MODEL_SAMPLE_RUN_ID="${LAB_ORDER_HISTORY_READ_MODEL_SAMPLE_RUN_ID:-milestone-8-order-history-read-model-sample}"

PRODUCT_REQUESTS_PER_SECOND="${LAB_PRODUCT_RPS:-220}"
PRODUCT_DURATION_SECONDS="${LAB_PRODUCT_DURATION_SECONDS:-6}"
PRODUCT_CONCURRENCY_CAP="${LAB_PRODUCT_CONCURRENCY_CAP:-64}"
PRODUCT_WARMUP_RPS="${LAB_PRODUCT_WARMUP_RPS:-40}"
PRODUCT_WARMUP_DURATION_SECONDS="${LAB_PRODUCT_WARMUP_DURATION_SECONDS:-1}"
PRODUCT_WARMUP_CONCURRENCY_CAP="${LAB_PRODUCT_WARMUP_CONCURRENCY_CAP:-16}"
PRODUCT_COUNT="${LAB_PRODUCT_COUNT:-8}"
PRODUCT_USER_COUNT="${LAB_PRODUCT_USER_COUNT:-4}"
HOT_PRODUCT_ID="${LAB_HOT_PRODUCT_ID:-sku-0001}"

ORDER_HISTORY_REQUESTS_PER_SECOND="${LAB_ORDER_HISTORY_RPS:-180}"
ORDER_HISTORY_DURATION_SECONDS="${LAB_ORDER_HISTORY_DURATION_SECONDS:-6}"
ORDER_HISTORY_CONCURRENCY_CAP="${LAB_ORDER_HISTORY_CONCURRENCY_CAP:-48}"
ORDER_HISTORY_WARMUP_RPS="${LAB_ORDER_HISTORY_WARMUP_RPS:-30}"
ORDER_HISTORY_WARMUP_DURATION_SECONDS="${LAB_ORDER_HISTORY_WARMUP_DURATION_SECONDS:-1}"
ORDER_HISTORY_WARMUP_CONCURRENCY_CAP="${LAB_ORDER_HISTORY_WARMUP_CONCURRENCY_CAP:-8}"
ORDER_HISTORY_PRODUCT_COUNT="${LAB_ORDER_HISTORY_PRODUCT_COUNT:-4}"
ORDER_HISTORY_USER_COUNT="${LAB_ORDER_HISTORY_USER_COUNT:-2}"
ORDER_HISTORY_BASELINE_ORDER_COUNT="${LAB_ORDER_HISTORY_BASELINE_ORDER_COUNT:-24}"
ORDER_HISTORY_USER_ID="${LAB_ORDER_HISTORY_USER_ID:-user-0001}"
ORDER_HISTORY_PRODUCT_ID="${LAB_ORDER_HISTORY_PRODUCT_ID:-sku-0001}"
ORDER_HISTORY_ITEM_QUANTITY="${LAB_ORDER_HISTORY_ITEM_QUANTITY:-1}"

PAYMENT_FAST_LATENCY_MS="${LAB_PAYMENT_FAST_LATENCY_MS:-5}"
PAYMENT_SLOW_LATENCY_MS="${LAB_PAYMENT_SLOW_LATENCY_MS:-40}"
PAYMENT_TIMEOUT_LATENCY_MS="${LAB_PAYMENT_TIMEOUT_LATENCY_MS:-120}"
QUEUE_POLL_INTERVAL_MS="${LAB_QUEUE_POLL_INTERVAL_MS:-25}"
QUEUE_MAX_DEQUEUE_BATCH_SIZE="${LAB_QUEUE_MAX_DEQUEUE_BATCH_SIZE:-16}"

PRODUCT_CATALOG_PID=""
PRODUCT_STOREFRONT_PID=""
ORDER_HISTORY_CART_PID=""
ORDER_HISTORY_PAYMENT_PID=""
ORDER_HISTORY_ORDER_PID=""
ORDER_HISTORY_STOREFRONT_PID=""
ORDER_HISTORY_WORKER_PID=""

reset_service_pids() {
    PRODUCT_CATALOG_PID=""
    PRODUCT_STOREFRONT_PID=""
    ORDER_HISTORY_CART_PID=""
    ORDER_HISTORY_PAYMENT_PID=""
    ORDER_HISTORY_ORDER_PID=""
    ORDER_HISTORY_STOREFRONT_PID=""
    ORDER_HISTORY_WORKER_PID=""
}

stop_services() {
    local pid

    for pid in \
        "$ORDER_HISTORY_WORKER_PID" \
        "$ORDER_HISTORY_STOREFRONT_PID" \
        "$ORDER_HISTORY_ORDER_PID" \
        "$ORDER_HISTORY_PAYMENT_PID" \
        "$ORDER_HISTORY_CART_PID" \
        "$PRODUCT_STOREFRONT_PID" \
        "$PRODUCT_CATALOG_PID"; do
        if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
            kill "$pid" 2>/dev/null || true
            wait "$pid" 2>/dev/null || true
        fi
    done

    reset_service_pids
}

stop_order_history_worker() {
    if [[ -n "$ORDER_HISTORY_WORKER_PID" ]] && kill -0 "$ORDER_HISTORY_WORKER_PID" 2>/dev/null; then
        kill "$ORDER_HISTORY_WORKER_PID" 2>/dev/null || true
        wait "$ORDER_HISTORY_WORKER_PID" 2>/dev/null || true
    fi

    ORDER_HISTORY_WORKER_PID=""
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

run_loadgen() {
    local workspace="$1"
    local target_url="$2"
    local run_id="$3"
    local rps="$4"
    local duration_seconds="$5"
    local concurrency_cap="$6"
    local artifact_dir="$7"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/LoadGen --no-build -- \
        --target-url "$target_url" \
        --method GET \
        --rps "$rps" \
        --duration-seconds "$duration_seconds" \
        --concurrency-cap "$concurrency_cap" \
        --run-id "$run_id" > "$artifact_dir/${run_id}-loadgen.txt" 2>&1
}

analyze_run() {
    local workspace="$1"
    local artifact_dir="$2"
    local run_id="$3"
    local operation="$4"
    local suffix="$5"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/Analyze --no-build -- \
        --run-id "$run_id" \
        --operation "$operation" > "$artifact_dir/${run_id}-${suffix}-analyze.txt" 2>&1

    cp "$workspace/logs/runs/$run_id/summary.json" "$artifact_dir/${run_id}-${suffix}-summary.json"
    cp "$workspace/analysis/$run_id/report.md" "$artifact_dir/${run_id}-${suffix}-analysis.md"
}

copy_workspace_logs() {
    local workspace="$1"
    local artifact_dir="$2"

    mkdir -p "$artifact_dir"

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
        -H "X-Correlation-Id: $correlation_id")"

    if [[ "$status_code" != "$expected_status" ]]; then
        echo "GET $url returned HTTP $status_code. See $body_path" >&2
        exit 1
    fi
}

sync_replicas() {
    local workspace="$1"
    local artifact_dir="$2"
    local label="$3"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/SeedData --no-build -- \
        --skip-primary-seed true \
        --sync-replicas true \
        --replica-east-lag-ms 0 \
        --replica-west-lag-ms 0 > "$artifact_dir/${label}-sync-replicas.txt" 2>&1
}

mutate_primary_product() {
    local workspace="$1"
    local product_id="$2"
    local product_version="$3"
    local inventory_version="$4"
    local available_quantity="$5"
    local reserved_quantity="$6"

    python3 - "$workspace" "$product_id" "$product_version" "$inventory_version" "$available_quantity" "$reserved_quantity" <<'PY'
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path

workspace = Path(sys.argv[1])
product_id = sys.argv[2]
product_version = int(sys.argv[3])
inventory_version = int(sys.argv[4])
available_quantity = int(sys.argv[5])
reserved_quantity = int(sys.argv[6])

db_path = workspace / "data" / "primary.db"
connection = sqlite3.connect(db_path)
try:
    now = datetime.now(timezone.utc).isoformat()
    connection.execute(
        """
        update products
           set version = ?,
               updated_utc = ?
         where product_id = ?
        """,
        (product_version, now, product_id),
    )
    connection.execute(
        """
        update inventory
           set available_quantity = ?,
               reserved_quantity = ?,
               version = ?,
               updated_utc = ?
         where product_id = ?
        """,
        (available_quantity, reserved_quantity, inventory_version, now, product_id),
    )
    connection.commit()
finally:
    connection.close()
PY
}

query_order_history_queue_count() {
    local workspace="$1"

    python3 - "$workspace" <<'PY'
import sqlite3
import sys
from pathlib import Path

workspace = Path(sys.argv[1])
db_path = workspace / "data" / "primary.db"
connection = sqlite3.connect(db_path)
try:
    count, = connection.execute(
        """
        select count(*)
          from queue_jobs
         where job_type = 'order-history-projection-update'
           and status in ('pending', 'in_progress')
        """
    ).fetchone()
    print(count)
finally:
    connection.close()
PY
}

wait_for_order_history_queue_drain() {
    local workspace="$1"
    local timeout_seconds="$2"

    python3 - "$workspace" "$timeout_seconds" <<'PY'
import sqlite3
import sys
import time
from pathlib import Path

workspace = Path(sys.argv[1])
timeout_seconds = float(sys.argv[2])
db_path = workspace / "data" / "primary.db"
deadline = time.time() + timeout_seconds

while time.time() < deadline:
    connection = sqlite3.connect(db_path)
    try:
        count, = connection.execute(
            """
            select count(*)
              from queue_jobs
             where job_type = 'order-history-projection-update'
               and status in ('pending', 'in_progress')
            """
        ).fetchone()
    finally:
        connection.close()

    if count == 0:
        print(0)
        sys.exit(0)

    time.sleep(0.1)

print("Timed out waiting for order-history projection queue to drain.", file=sys.stderr)
sys.exit(1)
PY
}

add_cart_item() {
    local cart_url="$1"
    local run_id="$2"
    local user_id="$3"
    local product_id="$4"
    local quantity="$5"
    local artifact_dir="$6"
    local body_path="$artifact_dir/${run_id}-cart-response.json"
    local headers_path="$artifact_dir/${run_id}-cart-response-headers.txt"
    local status_code

    status_code="$(curl -sS -D "$headers_path" -o "$body_path" -w '%{http_code}' \
        -X POST "$cart_url/cart/items" \
        -H 'Content-Type: application/json' \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $run_id-cart" \
        -d "{\"userId\":\"$user_id\",\"productId\":\"$product_id\",\"quantity\":$quantity}")"

    if [[ "$status_code" != "200" ]]; then
        echo "Cart setup failed for $user_id/$product_id with HTTP $status_code. See $body_path" >&2
        exit 1
    fi
}

run_sync_checkout_batch() {
    local storefront_url="$1"
    local run_id="$2"
    local request_count="$3"
    local user_id="$4"
    local payment_mode="$5"
    local artifact_dir="$6"

    python3 - "$storefront_url" "$run_id" "$request_count" "$user_id" "$payment_mode" "$artifact_dir" <<'PY'
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

storefront_url = sys.argv[1].rstrip("/")
run_id = sys.argv[2]
request_count = int(sys.argv[3])
user_id = sys.argv[4]
payment_mode = sys.argv[5]
artifact_dir = Path(sys.argv[6])
artifact_dir.mkdir(parents=True, exist_ok=True)

target_url = storefront_url + "/checkout?mode=sync"
results = []

for sequence in range(1, request_count + 1):
    correlation_id = f"{run_id}-{sequence:06d}"
    idempotency_key = f"idem-{run_id}-{sequence:06d}"
    body = json.dumps(
        {
            "userId": user_id,
            "paymentMode": payment_mode,
        }
    ).encode("utf-8")

    request = urllib.request.Request(
        target_url,
        data=body,
        method="POST",
        headers={
            "Content-Type": "application/json",
            "X-Run-Id": run_id,
            "X-Correlation-Id": correlation_id,
            "Idempotency-Key": idempotency_key,
        },
    )

    response_status = None
    error_code = None

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            response_status = response.getcode()
            payload = json.loads(response.read().decode("utf-8"))
            error_code = payload.get("paymentErrorCode") or payload.get("error")
    except urllib.error.HTTPError as error:
        response_status = error.code
        try:
            payload = json.loads(error.read().decode("utf-8"))
            error_code = payload.get("paymentErrorCode") or payload.get("error")
        except Exception:
            error_code = "http_error"
    except Exception as error:
        response_status = None
        error_code = type(error).__name__

    results.append(
        {
            "sequence": sequence,
            "statusCode": response_status,
            "errorCode": error_code,
        }
    )

    if response_status != 200:
        raise SystemExit(f"Checkout batch failed at request {sequence} with status {response_status} and error {error_code}.")

summary = {
    "runId": run_id,
    "requestCount": request_count,
    "statusCounts": {},
}

for item in results:
    key = str(item["statusCode"])
    summary["statusCounts"][key] = summary["statusCounts"].get(key, 0) + 1

(artifact_dir / f"{run_id}-summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps(summary, indent=2))
PY
}

run_single_checkout() {
    local storefront_url="$1"
    local run_id="$2"
    local user_id="$3"
    local payment_mode="$4"
    local artifact_dir="$5"
    local body_path="$artifact_dir/${run_id}-checkout-response.json"
    local headers_path="$artifact_dir/${run_id}-checkout-response-headers.txt"
    local status_code

    status_code="$(curl -sS -D "$headers_path" -o "$body_path" -w '%{http_code}' \
        -X POST "$storefront_url/checkout?mode=sync" \
        -H 'Content-Type: application/json' \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $run_id-checkout" \
        -H "Idempotency-Key: idem-$run_id" \
        -d "{\"userId\":\"$user_id\",\"paymentMode\":\"$payment_mode\"}")"

    if [[ "$status_code" != "200" ]]; then
        echo "Checkout failed for run $run_id with HTTP $status_code. See $body_path" >&2
        exit 1
    fi
}

start_product_services() {
    local workspace="$1"
    local artifact_dir="$2"

    LAB__Repository__RootPath="$workspace" \
    LAB__Cache__Enabled=false \
    dotnet run --project src/Catalog.Api --no-build --urls "$PRODUCT_CATALOG_URL" > "$artifact_dir/catalog.stdout.log" 2>&1 &
    PRODUCT_CATALOG_PID=$!

    LAB__Repository__RootPath="$workspace" \
    LAB__Cache__Enabled=false \
    LAB__ServiceEndpoints__CatalogBaseUrl="$PRODUCT_CATALOG_URL" \
    dotnet run --project src/Storefront.Api --no-build --urls "$PRODUCT_STOREFRONT_URL" > "$artifact_dir/storefront.stdout.log" 2>&1 &
    PRODUCT_STOREFRONT_PID=$!

    wait_for_http "$PRODUCT_CATALOG_URL/" "$PRODUCT_CATALOG_PID" "$artifact_dir/catalog.stdout.log" "Catalog.Api"
    wait_for_http "$PRODUCT_STOREFRONT_URL/health" "$PRODUCT_STOREFRONT_PID" "$artifact_dir/storefront.stdout.log" "Storefront.Api"
}

start_order_history_services() {
    local workspace="$1"
    local artifact_dir="$2"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/Cart.Api --no-build --urls "$ORDER_HISTORY_CART_URL" > "$artifact_dir/cart.stdout.log" 2>&1 &
    ORDER_HISTORY_CART_PID=$!

    LAB__Repository__RootPath="$workspace" \
    LAB__PaymentSimulator__FastLatencyMilliseconds="$PAYMENT_FAST_LATENCY_MS" \
    LAB__PaymentSimulator__SlowLatencyMilliseconds="$PAYMENT_SLOW_LATENCY_MS" \
    LAB__PaymentSimulator__TimeoutLatencyMilliseconds="$PAYMENT_TIMEOUT_LATENCY_MS" \
    dotnet run --project src/PaymentSimulator.Api --no-build --urls "$ORDER_HISTORY_PAYMENT_URL" > "$artifact_dir/payment-simulator.stdout.log" 2>&1 &
    ORDER_HISTORY_PAYMENT_PID=$!

    LAB__Repository__RootPath="$workspace" \
    LAB__ServiceEndpoints__PaymentSimulatorBaseUrl="$ORDER_HISTORY_PAYMENT_URL" \
    dotnet run --project src/Order.Api --no-build --urls "$ORDER_HISTORY_ORDER_URL" > "$artifact_dir/order.stdout.log" 2>&1 &
    ORDER_HISTORY_ORDER_PID=$!

    LAB__Repository__RootPath="$workspace" \
    LAB__ServiceEndpoints__OrderBaseUrl="$ORDER_HISTORY_ORDER_URL" \
    LAB__RateLimiter__Checkout__Enabled="false" \
    dotnet run --project src/Storefront.Api --no-build --urls "$ORDER_HISTORY_STOREFRONT_URL" > "$artifact_dir/storefront.stdout.log" 2>&1 &
    ORDER_HISTORY_STOREFRONT_PID=$!

    wait_for_http "$ORDER_HISTORY_CART_URL/" "$ORDER_HISTORY_CART_PID" "$artifact_dir/cart.stdout.log" "Cart.Api"
    wait_for_http "$ORDER_HISTORY_PAYMENT_URL/" "$ORDER_HISTORY_PAYMENT_PID" "$artifact_dir/payment-simulator.stdout.log" "PaymentSimulator.Api"
    wait_for_http "$ORDER_HISTORY_ORDER_URL/" "$ORDER_HISTORY_ORDER_PID" "$artifact_dir/order.stdout.log" "Order.Api"
    wait_for_http "$ORDER_HISTORY_STOREFRONT_URL/health" "$ORDER_HISTORY_STOREFRONT_PID" "$artifact_dir/storefront.stdout.log" "Storefront.Api"
}

start_order_history_worker() {
    local workspace="$1"
    local artifact_dir="$2"

    LAB__Repository__RootPath="$workspace" \
    LAB__ServiceEndpoints__PaymentSimulatorBaseUrl="$ORDER_HISTORY_PAYMENT_URL" \
    LAB__Queue__PollIntervalMilliseconds="$QUEUE_POLL_INTERVAL_MS" \
    LAB__Queue__MaxDequeueBatchSize="$QUEUE_MAX_DEQUEUE_BATCH_SIZE" \
    dotnet run --project src/Worker --no-build > "$artifact_dir/worker.stdout.log" 2>&1 &
    ORDER_HISTORY_WORKER_PID=$!

    sleep 1

    if ! kill -0 "$ORDER_HISTORY_WORKER_PID" 2>/dev/null; then
        echo "Worker exited before becoming ready. See $artifact_dir/worker.stdout.log" >&2
        exit 1
    fi
}

run_product_scenario() {
    prepare_workspace "$PRODUCT_WORKSPACE" "$PRODUCT_ARTIFACT_ROOT"

    LAB__Repository__RootPath="$PRODUCT_WORKSPACE" \
    dotnet run --project src/SeedData --no-build -- \
        --products "$PRODUCT_COUNT" \
        --users "$PRODUCT_USER_COUNT" \
        --reset true \
        --sync-replicas true \
        --replica-east-lag-ms 0 \
        --replica-west-lag-ms 0 > "$PRODUCT_ARTIFACT_ROOT/seed-data.txt" 2>&1

    mutate_primary_product "$PRODUCT_WORKSPACE" "$HOT_PRODUCT_ID" 30 40 101 1
    sync_replicas "$PRODUCT_WORKSPACE" "$PRODUCT_ARTIFACT_ROOT" "baseline-current"

    start_product_services "$PRODUCT_WORKSPACE" "$PRODUCT_ARTIFACT_ROOT"

    run_loadgen \
        "$PRODUCT_WORKSPACE" \
        "$PRODUCT_STOREFRONT_URL/products/$HOT_PRODUCT_ID?cache=off&readSource=primary" \
        "$PRODUCT_PRIMARY_WARMUP_RUN_ID" \
        "$PRODUCT_WARMUP_RPS" \
        "$PRODUCT_WARMUP_DURATION_SECONDS" \
        "$PRODUCT_WARMUP_CONCURRENCY_CAP" \
        "$PRODUCT_ARTIFACT_ROOT"

    capture_get_json \
        "$PRODUCT_STOREFRONT_URL/products/$HOT_PRODUCT_ID?cache=off&readSource=primary" \
        "$PRODUCT_PRIMARY_SAMPLE_RUN_ID" \
        "$PRODUCT_PRIMARY_SAMPLE_RUN_ID" \
        "$PRODUCT_ARTIFACT_ROOT/${PRODUCT_PRIMARY_SAMPLE_RUN_ID}-response.json" \
        "$PRODUCT_ARTIFACT_ROOT/${PRODUCT_PRIMARY_SAMPLE_RUN_ID}-response-headers.txt"

    run_loadgen \
        "$PRODUCT_WORKSPACE" \
        "$PRODUCT_STOREFRONT_URL/products/$HOT_PRODUCT_ID?cache=off&readSource=primary" \
        "$PRODUCT_PRIMARY_RUN_ID" \
        "$PRODUCT_REQUESTS_PER_SECOND" \
        "$PRODUCT_DURATION_SECONDS" \
        "$PRODUCT_CONCURRENCY_CAP" \
        "$PRODUCT_ARTIFACT_ROOT"

    analyze_run "$PRODUCT_WORKSPACE" "$PRODUCT_ARTIFACT_ROOT" "$PRODUCT_PRIMARY_RUN_ID" "product-page" "storefront"
    analyze_run "$PRODUCT_WORKSPACE" "$PRODUCT_ARTIFACT_ROOT" "$PRODUCT_PRIMARY_RUN_ID" "catalog-product-detail" "catalog"

    sync_replicas "$PRODUCT_WORKSPACE" "$PRODUCT_ARTIFACT_ROOT" "after-primary"
    mutate_primary_product "$PRODUCT_WORKSPACE" "$HOT_PRODUCT_ID" 45 53 8 0

    run_loadgen \
        "$PRODUCT_WORKSPACE" \
        "$PRODUCT_STOREFRONT_URL/products/$HOT_PRODUCT_ID?cache=off&readSource=replica-east" \
        "$PRODUCT_REPLICA_WARMUP_RUN_ID" \
        "$PRODUCT_WARMUP_RPS" \
        "$PRODUCT_WARMUP_DURATION_SECONDS" \
        "$PRODUCT_WARMUP_CONCURRENCY_CAP" \
        "$PRODUCT_ARTIFACT_ROOT"

    capture_get_json \
        "$PRODUCT_STOREFRONT_URL/products/$HOT_PRODUCT_ID?cache=off&readSource=replica-east" \
        "$PRODUCT_REPLICA_SAMPLE_RUN_ID" \
        "$PRODUCT_REPLICA_SAMPLE_RUN_ID" \
        "$PRODUCT_ARTIFACT_ROOT/${PRODUCT_REPLICA_SAMPLE_RUN_ID}-response.json" \
        "$PRODUCT_ARTIFACT_ROOT/${PRODUCT_REPLICA_SAMPLE_RUN_ID}-response-headers.txt"

    run_loadgen \
        "$PRODUCT_WORKSPACE" \
        "$PRODUCT_STOREFRONT_URL/products/$HOT_PRODUCT_ID?cache=off&readSource=replica-east" \
        "$PRODUCT_REPLICA_RUN_ID" \
        "$PRODUCT_REQUESTS_PER_SECOND" \
        "$PRODUCT_DURATION_SECONDS" \
        "$PRODUCT_CONCURRENCY_CAP" \
        "$PRODUCT_ARTIFACT_ROOT"

    analyze_run "$PRODUCT_WORKSPACE" "$PRODUCT_ARTIFACT_ROOT" "$PRODUCT_REPLICA_RUN_ID" "product-page" "storefront"
    analyze_run "$PRODUCT_WORKSPACE" "$PRODUCT_ARTIFACT_ROOT" "$PRODUCT_REPLICA_RUN_ID" "catalog-product-detail" "catalog"

    copy_workspace_logs "$PRODUCT_WORKSPACE" "$PRODUCT_ARTIFACT_ROOT"

    if [[ -f "$PRODUCT_WORKSPACE/data/primary.db" ]]; then
        cp "$PRODUCT_WORKSPACE/data/primary.db" "$PRODUCT_ARTIFACT_ROOT/primary.db"
    fi

    if [[ -f "$PRODUCT_WORKSPACE/data/replica-east.db" ]]; then
        cp "$PRODUCT_WORKSPACE/data/replica-east.db" "$PRODUCT_ARTIFACT_ROOT/replica-east.db"
    fi

    if [[ -f "$PRODUCT_WORKSPACE/data/replica-west.db" ]]; then
        cp "$PRODUCT_WORKSPACE/data/replica-west.db" "$PRODUCT_ARTIFACT_ROOT/replica-west.db"
    fi
}

run_order_history_scenario() {
    prepare_workspace "$ORDER_HISTORY_WORKSPACE" "$ORDER_HISTORY_ARTIFACT_ROOT"

    LAB__Repository__RootPath="$ORDER_HISTORY_WORKSPACE" \
    dotnet run --project src/SeedData --no-build -- \
        --products "$ORDER_HISTORY_PRODUCT_COUNT" \
        --users "$ORDER_HISTORY_USER_COUNT" \
        --reset true > "$ORDER_HISTORY_ARTIFACT_ROOT/seed-data.txt" 2>&1

    start_order_history_services "$ORDER_HISTORY_WORKSPACE" "$ORDER_HISTORY_ARTIFACT_ROOT"
    start_order_history_worker "$ORDER_HISTORY_WORKSPACE" "$ORDER_HISTORY_ARTIFACT_ROOT"

    add_cart_item \
        "$ORDER_HISTORY_CART_URL" \
        "$ORDER_HISTORY_BASELINE_RUN_ID-cart-seed" \
        "$ORDER_HISTORY_USER_ID" \
        "$ORDER_HISTORY_PRODUCT_ID" \
        "$ORDER_HISTORY_ITEM_QUANTITY" \
        "$ORDER_HISTORY_ARTIFACT_ROOT"

    run_sync_checkout_batch \
        "$ORDER_HISTORY_STOREFRONT_URL" \
        "$ORDER_HISTORY_BASELINE_RUN_ID" \
        "$ORDER_HISTORY_BASELINE_ORDER_COUNT" \
        "$ORDER_HISTORY_USER_ID" \
        "fast_success" \
        "$ORDER_HISTORY_ARTIFACT_ROOT"

    wait_for_order_history_queue_drain "$ORDER_HISTORY_WORKSPACE" 60 > "$ORDER_HISTORY_ARTIFACT_ROOT/baseline-queue-drain.txt"
    stop_order_history_worker

    run_single_checkout \
        "$ORDER_HISTORY_STOREFRONT_URL" \
        "$ORDER_HISTORY_STALE_SEED_RUN_ID" \
        "$ORDER_HISTORY_USER_ID" \
        "fast_success" \
        "$ORDER_HISTORY_ARTIFACT_ROOT"

    query_order_history_queue_count "$ORDER_HISTORY_WORKSPACE" > "$ORDER_HISTORY_ARTIFACT_ROOT/pending-order-history-jobs.txt"

    run_loadgen \
        "$ORDER_HISTORY_WORKSPACE" \
        "$ORDER_HISTORY_STOREFRONT_URL/orders/$ORDER_HISTORY_USER_ID?readSource=primary-projection" \
        "$ORDER_HISTORY_PRIMARY_WARMUP_RUN_ID" \
        "$ORDER_HISTORY_WARMUP_RPS" \
        "$ORDER_HISTORY_WARMUP_DURATION_SECONDS" \
        "$ORDER_HISTORY_WARMUP_CONCURRENCY_CAP" \
        "$ORDER_HISTORY_ARTIFACT_ROOT"

    capture_get_json \
        "$ORDER_HISTORY_STOREFRONT_URL/orders/$ORDER_HISTORY_USER_ID?readSource=primary-projection" \
        "$ORDER_HISTORY_PRIMARY_SAMPLE_RUN_ID" \
        "$ORDER_HISTORY_PRIMARY_SAMPLE_RUN_ID" \
        "$ORDER_HISTORY_ARTIFACT_ROOT/${ORDER_HISTORY_PRIMARY_SAMPLE_RUN_ID}-response.json" \
        "$ORDER_HISTORY_ARTIFACT_ROOT/${ORDER_HISTORY_PRIMARY_SAMPLE_RUN_ID}-response-headers.txt"

    run_loadgen \
        "$ORDER_HISTORY_WORKSPACE" \
        "$ORDER_HISTORY_STOREFRONT_URL/orders/$ORDER_HISTORY_USER_ID?readSource=read-model" \
        "$ORDER_HISTORY_READ_MODEL_WARMUP_RUN_ID" \
        "$ORDER_HISTORY_WARMUP_RPS" \
        "$ORDER_HISTORY_WARMUP_DURATION_SECONDS" \
        "$ORDER_HISTORY_WARMUP_CONCURRENCY_CAP" \
        "$ORDER_HISTORY_ARTIFACT_ROOT"

    capture_get_json \
        "$ORDER_HISTORY_STOREFRONT_URL/orders/$ORDER_HISTORY_USER_ID?readSource=read-model" \
        "$ORDER_HISTORY_READ_MODEL_SAMPLE_RUN_ID" \
        "$ORDER_HISTORY_READ_MODEL_SAMPLE_RUN_ID" \
        "$ORDER_HISTORY_ARTIFACT_ROOT/${ORDER_HISTORY_READ_MODEL_SAMPLE_RUN_ID}-response.json" \
        "$ORDER_HISTORY_ARTIFACT_ROOT/${ORDER_HISTORY_READ_MODEL_SAMPLE_RUN_ID}-response-headers.txt"

    run_loadgen \
        "$ORDER_HISTORY_WORKSPACE" \
        "$ORDER_HISTORY_STOREFRONT_URL/orders/$ORDER_HISTORY_USER_ID?readSource=primary-projection" \
        "$ORDER_HISTORY_PRIMARY_RUN_ID" \
        "$ORDER_HISTORY_REQUESTS_PER_SECOND" \
        "$ORDER_HISTORY_DURATION_SECONDS" \
        "$ORDER_HISTORY_CONCURRENCY_CAP" \
        "$ORDER_HISTORY_ARTIFACT_ROOT"

    run_loadgen \
        "$ORDER_HISTORY_WORKSPACE" \
        "$ORDER_HISTORY_STOREFRONT_URL/orders/$ORDER_HISTORY_USER_ID?readSource=read-model" \
        "$ORDER_HISTORY_READ_MODEL_RUN_ID" \
        "$ORDER_HISTORY_REQUESTS_PER_SECOND" \
        "$ORDER_HISTORY_DURATION_SECONDS" \
        "$ORDER_HISTORY_CONCURRENCY_CAP" \
        "$ORDER_HISTORY_ARTIFACT_ROOT"

    analyze_run "$ORDER_HISTORY_WORKSPACE" "$ORDER_HISTORY_ARTIFACT_ROOT" "$ORDER_HISTORY_PRIMARY_RUN_ID" "order-history" "storefront"
    analyze_run "$ORDER_HISTORY_WORKSPACE" "$ORDER_HISTORY_ARTIFACT_ROOT" "$ORDER_HISTORY_READ_MODEL_RUN_ID" "order-history" "storefront"

    copy_workspace_logs "$ORDER_HISTORY_WORKSPACE" "$ORDER_HISTORY_ARTIFACT_ROOT"

    if [[ -f "$ORDER_HISTORY_WORKSPACE/data/primary.db" ]]; then
        cp "$ORDER_HISTORY_WORKSPACE/data/primary.db" "$ORDER_HISTORY_ARTIFACT_ROOT/primary.db"
    fi

    if [[ -f "$ORDER_HISTORY_WORKSPACE/data/readmodels.db" ]]; then
        cp "$ORDER_HISTORY_WORKSPACE/data/readmodels.db" "$ORDER_HISTORY_ARTIFACT_ROOT/readmodels.db"
    fi
}

write_comparison_json() {
    python3 - \
        "$PRODUCT_ARTIFACT_ROOT" \
        "$ORDER_HISTORY_ARTIFACT_ROOT" \
        "$COMPARISON_PATH" <<'PY'
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

product_root = Path(sys.argv[1])
order_root = Path(sys.argv[2])
comparison_path = Path(sys.argv[3])


def load_json(path: Path):
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def maybe_read_text(path: Path):
    return path.read_text(encoding="utf-8").strip() if path.exists() else None


def find_source(summary: dict, read_source: str):
    for source in summary.get("readFreshness", {}).get("sources", []):
        if source.get("readSource") == read_source:
            return source

    return {
        "readSource": read_source,
        "requestCount": 0,
        "staleRequestCount": 0,
        "staleRequestFraction": None,
        "averageLatencyMs": None,
        "p95LatencyMs": None,
        "averageMaxStalenessAgeMs": None,
        "maxObservedStalenessAgeMs": None,
    }


def percent_reduction(baseline: float, candidate: float):
    if baseline <= 0:
        return None

    return (baseline - candidate) / baseline * 100.0


product_primary_storefront = load_json(next(product_root.glob("*product-primary-storefront-summary.json")))
product_primary_catalog = load_json(next(product_root.glob("*product-primary-catalog-summary.json")))
product_replica_storefront = load_json(next(product_root.glob("*product-replica-east-storefront-summary.json")))
product_replica_catalog = load_json(next(product_root.glob("*product-replica-east-catalog-summary.json")))

product_primary_sample = load_json(product_root / "milestone-8-product-primary-sample-response.json")
product_replica_sample = load_json(product_root / "milestone-8-product-replica-east-sample-response.json")

order_primary_summary = load_json(next(order_root.glob("*primary-projection-storefront-summary.json")))
order_read_model_summary = load_json(next(order_root.glob("*read-model-storefront-summary.json")))
order_primary_sample = load_json(order_root / "milestone-8-order-history-primary-projection-sample-response.json")
order_read_model_sample = load_json(order_root / "milestone-8-order-history-read-model-sample-response.json")

product_primary_source = find_source(product_primary_catalog, "primary")
product_replica_source = find_source(product_replica_catalog, "replica-east")

order_primary_source = find_source(order_primary_summary, "primary-projection")
order_read_model_source = find_source(order_read_model_summary, "read-model")

payload = {
    "generatedUtc": datetime.now(timezone.utc).isoformat(),
    "productReads": {
        "workload": {
            "targetProductId": product_primary_sample.get("productId"),
            "requestsPerSecond": int(product_root.parent.parent.joinpath("dummy").exists() or 0),  # overwritten below
        },
        "primary": {
            "storefront": {
                "averageLatencyMs": product_primary_storefront["requests"]["averageLatencyMs"],
                "p95LatencyMs": product_primary_storefront["requests"]["p95LatencyMs"],
                "throughputPerSecond": product_primary_storefront["requests"]["throughputPerSecond"],
            },
            "catalog": {
                "requestCount": product_primary_catalog["requests"]["requestCount"],
                "primaryReadRequests": product_primary_source["requestCount"],
                "staleRequestFraction": product_primary_catalog["readFreshness"]["staleRequestFraction"],
                "staleResultFraction": product_primary_catalog["readFreshness"]["staleResultFraction"],
            },
            "sample": {
                "readSource": product_primary_sample["readSource"],
                "version": product_primary_sample["version"],
                "sellableQuantity": product_primary_sample["inventory"]["sellableQuantity"],
                "staleRead": product_primary_sample["freshness"]["staleRead"],
            },
        },
        "replicaEast": {
            "storefront": {
                "averageLatencyMs": product_replica_storefront["requests"]["averageLatencyMs"],
                "p95LatencyMs": product_replica_storefront["requests"]["p95LatencyMs"],
                "throughputPerSecond": product_replica_storefront["requests"]["throughputPerSecond"],
            },
            "catalog": {
                "requestCount": product_replica_catalog["requests"]["requestCount"],
                "replicaReadRequests": product_replica_source["requestCount"],
                "staleRequestFraction": product_replica_catalog["readFreshness"]["staleRequestFraction"],
                "staleResultFraction": product_replica_catalog["readFreshness"]["staleResultFraction"],
                "maxObservedStalenessAgeMs": product_replica_catalog["readFreshness"]["maxObservedStalenessAgeMs"],
            },
            "sample": {
                "readSource": product_replica_sample["readSource"],
                "version": product_replica_sample["version"],
                "sellableQuantity": product_replica_sample["inventory"]["sellableQuantity"],
                "staleRead": product_replica_sample["freshness"]["staleRead"],
            },
        },
    },
    "orderHistory": {
        "primaryProjection": {
            "averageLatencyMs": order_primary_summary["requests"]["averageLatencyMs"],
            "p95LatencyMs": order_primary_summary["requests"]["p95LatencyMs"],
            "throughputPerSecond": order_primary_summary["requests"]["throughputPerSecond"],
            "staleRequestFraction": order_primary_summary["readFreshness"]["staleRequestFraction"],
            "staleResultFraction": order_primary_summary["readFreshness"]["staleResultFraction"],
            "sourceRequestCount": order_primary_source["requestCount"],
            "sampleOrderCount": order_primary_sample["orderCount"],
        },
        "readModel": {
            "averageLatencyMs": order_read_model_summary["requests"]["averageLatencyMs"],
            "p95LatencyMs": order_read_model_summary["requests"]["p95LatencyMs"],
            "throughputPerSecond": order_read_model_summary["requests"]["throughputPerSecond"],
            "staleRequestFraction": order_read_model_summary["readFreshness"]["staleRequestFraction"],
            "staleResultFraction": order_read_model_summary["readFreshness"]["staleResultFraction"],
            "maxObservedStalenessAgeMs": order_read_model_summary["readFreshness"]["maxObservedStalenessAgeMs"],
            "sourceRequestCount": order_read_model_source["requestCount"],
            "sampleOrderCount": order_read_model_sample["orderCount"],
        },
        "pendingProjectionJobsDuringReadWindow": int(maybe_read_text(order_root / "pending-order-history-jobs.txt") or "0"),
    },
}

payload["productReads"]["workload"] = {
    "targetProductId": product_primary_sample.get("productId"),
    "requestsPerSecond": product_primary_storefront["requests"]["requestCount"] / (product_primary_storefront["requests"]["windowDurationMs"] / 1000.0),
    "durationSeconds": product_primary_storefront["requests"]["windowDurationMs"] / 1000.0,
    "requestedReadSources": ["primary", "replica-east"],
}

payload["productReads"]["primaryLoadReductionPct"] = percent_reduction(
    product_primary_source["requestCount"],
    0,
)

payload["orderHistory"]["writeTableReadReductionPct"] = percent_reduction(
    order_primary_source["requestCount"],
    0,
)

comparison_path.parent.mkdir(parents=True, exist_ok=True)
comparison_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
print(json.dumps(payload, indent=2))
PY
}

echo "Preparing milestone-8 experiment directories..."
rm -rf "$EXPERIMENT_ROOT"
mkdir -p "$ARTIFACT_ROOT"

cd "$PROJECT_ROOT"

echo "Building solution..."
dotnet build ecommerce-systems-lab.sln > "$ARTIFACT_ROOT/build.log" 2>&1

echo "Running product primary-vs-replica scenario..."
run_product_scenario
stop_services

echo "Running order-history primary-projection-vs-read-model scenario..."
run_order_history_scenario
stop_services

echo "Writing comparison artifact..."
write_comparison_json > "$ARTIFACT_ROOT/comparison-pretty.txt"

echo "Milestone-8 experiment completed."
echo "Artifacts root:    $ARTIFACT_ROOT"
echo "Comparison JSON:   $COMPARISON_PATH"
echo "Product artifacts: $PRODUCT_ARTIFACT_ROOT"
echo "Order artifacts:   $ORDER_HISTORY_ARTIFACT_ROOT"
