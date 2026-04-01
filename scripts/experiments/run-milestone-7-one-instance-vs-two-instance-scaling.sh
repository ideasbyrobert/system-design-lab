#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXPERIMENT_ROOT="$PROJECT_ROOT/docs/experiments/milestone-7-one-instance-vs-two-instance-scaling"
WORKSPACE_ROOT="${LAB_EXPERIMENT_ROOT:-$EXPERIMENT_ROOT/workspace}"
ARTIFACT_ROOT="$EXPERIMENT_ROOT/artifacts"
COMPARISON_PATH="$ARTIFACT_ROOT/comparison.json"

CPU_ONE_WORKSPACE="$WORKSPACE_ROOT/frontend-cpu/one-instance"
CPU_TWO_WORKSPACE="$WORKSPACE_ROOT/frontend-cpu/two-instance"
CHECKOUT_ONE_WORKSPACE="$WORKSPACE_ROOT/shared-checkout/one-instance"
CHECKOUT_TWO_WORKSPACE="$WORKSPACE_ROOT/shared-checkout/two-instance"

CPU_ONE_ARTIFACT_ROOT="$ARTIFACT_ROOT/frontend-cpu/one-instance"
CPU_TWO_ARTIFACT_ROOT="$ARTIFACT_ROOT/frontend-cpu/two-instance"
CHECKOUT_ONE_ARTIFACT_ROOT="$ARTIFACT_ROOT/shared-checkout/one-instance"
CHECKOUT_TWO_ARTIFACT_ROOT="$ARTIFACT_ROOT/shared-checkout/two-instance"

PROXY_URL="${LAB_PROXY_URL:-http://127.0.0.1:5090}"
CATALOG_URL="${LAB_CATALOG_URL:-http://127.0.0.1:5084}"
CART_URL="${LAB_CART_URL:-http://127.0.0.1:5085}"
PAYMENT_URL="${LAB_PAYMENT_URL:-http://127.0.0.1:5086}"
ORDER_URL="${LAB_ORDER_URL:-http://127.0.0.1:5087}"
STOREFRONT_A_URL="${LAB_STOREFRONT_A_URL:-http://127.0.0.1:5088}"
STOREFRONT_B_URL="${LAB_STOREFRONT_B_URL:-http://127.0.0.1:5089}"

CPU_WORK_FACTOR="${LAB_CPU_WORK_FACTOR:-90}"
CPU_ITERATIONS="${LAB_CPU_ITERATIONS:-1000}"
CPU_RPS="${LAB_CPU_RPS:-8}"
CPU_DURATION_SECONDS="${LAB_CPU_DURATION_SECONDS:-6}"
CPU_CONCURRENCY_CAP="${LAB_CPU_CONCURRENCY_CAP:-8}"

CHECKOUT_REQUEST_COUNT="${LAB_CHECKOUT_REQUEST_COUNT:-12}"
CHECKOUT_CONCURRENCY_CAP="${LAB_CHECKOUT_CONCURRENCY_CAP:-12}"
CHECKOUT_REQUEST_SPACING_MS="${LAB_CHECKOUT_REQUEST_SPACING_MS:-15}"
CHECKOUT_ITEM_QUANTITY="${LAB_CHECKOUT_ITEM_QUANTITY:-1}"
CHECKOUT_PRODUCT_COUNT="${LAB_CHECKOUT_PRODUCT_COUNT:-13}"
CHECKOUT_USER_COUNT="${LAB_CHECKOUT_USER_COUNT:-13}"
CHECKOUT_PAYMENT_MODE="${LAB_CHECKOUT_PAYMENT_MODE:-slow_success}"
CHECKOUT_SLOW_LATENCY_MS="${LAB_CHECKOUT_SLOW_LATENCY_MS:-700}"
CHECKOUT_FAST_LATENCY_MS="${LAB_CHECKOUT_FAST_LATENCY_MS:-5}"
CHECKOUT_TIMEOUT_LATENCY_MS="${LAB_CHECKOUT_TIMEOUT_LATENCY_MS:-900}"
CHECKOUT_DELAYED_CONFIRMATION_MS="${LAB_CHECKOUT_DELAYED_CONFIRMATION_MS:-300}"
CHECKOUT_DUPLICATE_CALLBACK_SPACING_MS="${LAB_CHECKOUT_DUPLICATE_CALLBACK_SPACING_MS:-50}"
CHECKOUT_DISPATCHER_POLL_MS="${LAB_CHECKOUT_DISPATCHER_POLL_MS:-20}"

CPU_ONE_RUN_ID="${LAB_CPU_ONE_RUN_ID:-milestone-7-frontend-cpu-one-instance}"
CPU_TWO_RUN_ID="${LAB_CPU_TWO_RUN_ID:-milestone-7-frontend-cpu-two-instance}"
CPU_ONE_WARMUP_RUN_ID="${LAB_CPU_ONE_WARMUP_RUN_ID:-milestone-7-frontend-cpu-one-instance-warmup}"
CPU_TWO_WARMUP_RUN_ID="${LAB_CPU_TWO_WARMUP_RUN_ID:-milestone-7-frontend-cpu-two-instance-warmup}"

CHECKOUT_ONE_RUN_ID="${LAB_CHECKOUT_ONE_RUN_ID:-milestone-7-shared-checkout-one-instance}"
CHECKOUT_TWO_RUN_ID="${LAB_CHECKOUT_TWO_RUN_ID:-milestone-7-shared-checkout-two-instance}"
CHECKOUT_ONE_WARMUP_RUN_ID="${LAB_CHECKOUT_ONE_WARMUP_RUN_ID:-milestone-7-shared-checkout-one-instance-warmup}"
CHECKOUT_TWO_WARMUP_RUN_ID="${LAB_CHECKOUT_TWO_WARMUP_RUN_ID:-milestone-7-shared-checkout-two-instance-warmup}"

STOREFRONT_A_PID=""
STOREFRONT_B_PID=""
PROXY_PID=""
CART_PID=""
PAYMENT_PID=""
ORDER_PID=""

reset_service_pids() {
    STOREFRONT_A_PID=""
    STOREFRONT_B_PID=""
    PROXY_PID=""
    CART_PID=""
    PAYMENT_PID=""
    ORDER_PID=""
}

stop_services() {
    local pid

    for pid in "$PROXY_PID" "$STOREFRONT_B_PID" "$STOREFRONT_A_PID" "$ORDER_PID" "$PAYMENT_PID" "$CART_PID"; do
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
    mkdir -p "$workspace" "$artifact_dir"
}

seed_workspace() {
    local workspace="$1"
    local artifact_dir="$2"
    local product_count="$3"
    local user_count="$4"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/SeedData --no-build -- \
        --products "$product_count" \
        --users "$user_count" \
        --reset true > "$artifact_dir/seed-data.txt" 2>&1
}

start_storefront_instances() {
    local workspace="$1"
    local artifact_dir="$2"
    local instance_count="$3"
    local order_base_url="$4"

    LAB__Repository__RootPath="$workspace" \
    LAB__ServiceEndpoints__OrderBaseUrl="$order_base_url" \
    LAB__RateLimiter__Checkout__Enabled="false" \
    DOTNET_PROCESSOR_COUNT=1 \
    dotnet run --project src/Storefront.Api --no-build --urls "$STOREFRONT_A_URL" > "$artifact_dir/storefront-a.stdout.log" 2>&1 &
    STOREFRONT_A_PID=$!

    wait_for_http "$STOREFRONT_A_URL/health" "$STOREFRONT_A_PID" "$artifact_dir/storefront-a.stdout.log" "Storefront.Api (A)"

    if [[ "$instance_count" -gt 1 ]]; then
        LAB__Repository__RootPath="$workspace" \
        LAB__ServiceEndpoints__OrderBaseUrl="$order_base_url" \
        LAB__RateLimiter__Checkout__Enabled="false" \
        DOTNET_PROCESSOR_COUNT=1 \
        dotnet run --project src/Storefront.Api --no-build --urls "$STOREFRONT_B_URL" > "$artifact_dir/storefront-b.stdout.log" 2>&1 &
        STOREFRONT_B_PID=$!

        wait_for_http "$STOREFRONT_B_URL/health" "$STOREFRONT_B_PID" "$artifact_dir/storefront-b.stdout.log" "Storefront.Api (B)"
    fi
}

start_proxy() {
    local workspace="$1"
    local artifact_dir="$2"
    local storefront_count="$3"

    if [[ "$storefront_count" -gt 1 ]]; then
        LAB__Repository__RootPath="$workspace" \
        LAB__Proxy__RoutingMode="round_robin" \
        LAB__Proxy__Storefront__Backends__0="$STOREFRONT_A_URL" \
        LAB__Proxy__Storefront__Backends__1="$STOREFRONT_B_URL" \
        LAB__Proxy__Catalog__Backends__0="$CATALOG_URL" \
        dotnet run --project src/Proxy --no-build --urls "$PROXY_URL" > "$artifact_dir/proxy.stdout.log" 2>&1 &
    else
        LAB__Repository__RootPath="$workspace" \
        LAB__Proxy__RoutingMode="round_robin" \
        LAB__Proxy__Storefront__Backends__0="$STOREFRONT_A_URL" \
        LAB__Proxy__Catalog__Backends__0="$CATALOG_URL" \
        dotnet run --project src/Proxy --no-build --urls "$PROXY_URL" > "$artifact_dir/proxy.stdout.log" 2>&1 &
    fi

    PROXY_PID=$!
    wait_for_http "$PROXY_URL/proxy/status" "$PROXY_PID" "$artifact_dir/proxy.stdout.log" "Proxy"
    curl -fsS "$PROXY_URL/proxy/status" > "$artifact_dir/proxy-status.json"
}

start_checkout_dependencies() {
    local workspace="$1"
    local artifact_dir="$2"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/Cart.Api --no-build --urls "$CART_URL" > "$artifact_dir/cart.stdout.log" 2>&1 &
    CART_PID=$!

    LAB__Repository__RootPath="$workspace" \
    LAB__PaymentSimulator__FastLatencyMilliseconds="$CHECKOUT_FAST_LATENCY_MS" \
    LAB__PaymentSimulator__SlowLatencyMilliseconds="$CHECKOUT_SLOW_LATENCY_MS" \
    LAB__PaymentSimulator__TimeoutLatencyMilliseconds="$CHECKOUT_TIMEOUT_LATENCY_MS" \
    LAB__PaymentSimulator__DelayedConfirmationMilliseconds="$CHECKOUT_DELAYED_CONFIRMATION_MS" \
    LAB__PaymentSimulator__DuplicateCallbackSpacingMilliseconds="$CHECKOUT_DUPLICATE_CALLBACK_SPACING_MS" \
    LAB__PaymentSimulator__DispatcherPollMilliseconds="$CHECKOUT_DISPATCHER_POLL_MS" \
    dotnet run --project src/PaymentSimulator.Api --no-build --urls "$PAYMENT_URL" > "$artifact_dir/payment-simulator.stdout.log" 2>&1 &
    PAYMENT_PID=$!

    LAB__Repository__RootPath="$workspace" \
    LAB__ServiceEndpoints__PaymentSimulatorBaseUrl="$PAYMENT_URL" \
    dotnet run --project src/Order.Api --no-build --urls "$ORDER_URL" > "$artifact_dir/order.stdout.log" 2>&1 &
    ORDER_PID=$!

    wait_for_http "$CART_URL/" "$CART_PID" "$artifact_dir/cart.stdout.log" "Cart.Api"
    wait_for_http "$PAYMENT_URL/" "$PAYMENT_PID" "$artifact_dir/payment-simulator.stdout.log" "PaymentSimulator.Api"
    wait_for_http "$ORDER_URL/" "$ORDER_PID" "$artifact_dir/order.stdout.log" "Order.Api"
}

ensure_cart_contains_item() {
    local run_id="$1"
    local user_id="$2"
    local product_id="$3"
    local quantity="$4"
    local artifact_dir="$5"
    local body_path="$artifact_dir/${run_id}-cart-response.json"
    local headers_path="$artifact_dir/${run_id}-cart-response-headers.txt"
    local status_code

    status_code="$(curl -sS -D "$headers_path" -o "$body_path" -w '%{http_code}' \
        -X POST "$CART_URL/cart/items" \
        -H 'Content-Type: application/json' \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $run_id-cart" \
        -d "{\"userId\":\"$user_id\",\"productId\":\"$product_id\",\"quantity\":$quantity}")"

    if [[ "$status_code" != "200" ]]; then
        echo "Cart setup failed for $user_id/$product_id with HTTP $status_code. See $body_path" >&2
        exit 1
    fi
}

warm_up_cpu_path() {
    local run_id="$1"
    local artifact_dir="$2"
    local body_path="$artifact_dir/${run_id}-response.json"
    local headers_path="$artifact_dir/${run_id}-response-headers.txt"
    local status_code

    status_code="$(curl -sS -D "$headers_path" -o "$body_path" -w '%{http_code}' \
        "$PROXY_URL/cpu?workFactor=$CPU_WORK_FACTOR&iterations=$CPU_ITERATIONS" \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $run_id-cpu")"

    if [[ "$status_code" != "200" ]]; then
        echo "CPU warm-up failed with HTTP $status_code. See $body_path" >&2
        exit 1
    fi
}

run_cpu_load() {
    local workspace="$1"
    local artifact_dir="$2"
    local run_id="$3"

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/LoadGen --no-build -- \
        --target-url "$PROXY_URL/cpu?workFactor=$CPU_WORK_FACTOR&iterations=$CPU_ITERATIONS" \
        --method GET \
        --rps "$CPU_RPS" \
        --duration-seconds "$CPU_DURATION_SECONDS" \
        --concurrency-cap "$CPU_CONCURRENCY_CAP" \
        --run-id "$run_id" > "$artifact_dir/${run_id}-loadgen.txt" 2>&1
}

warm_up_checkout_path() {
    local run_id="$1"
    local user_id="$2"
    local payment_mode="$3"
    local artifact_dir="$4"
    local response_body_path="$artifact_dir/${run_id}-warmup-response.json"
    local response_headers_path="$artifact_dir/${run_id}-warmup-response-headers.txt"
    local status_code

    status_code="$(curl -sS -D "$response_headers_path" -o "$response_body_path" -w '%{http_code}' \
        -X POST "$PROXY_URL/checkout?mode=sync" \
        -H 'Content-Type: application/json' \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $run_id-checkout" \
        -H "Idempotency-Key: idem-$run_id" \
        -d "{\"userId\":\"$user_id\",\"paymentMode\":\"$payment_mode\"}")"

    if [[ "$status_code" != "200" ]]; then
        echo "Warm-up checkout failed with HTTP $status_code. See $response_body_path" >&2
        exit 1
    fi
}

run_checkout_load() {
    local run_id="$1"
    local artifact_dir="$2"

    python3 - "$PROXY_URL" "$run_id" "$CHECKOUT_REQUEST_COUNT" "$CHECKOUT_CONCURRENCY_CAP" "$CHECKOUT_REQUEST_SPACING_MS" "$CHECKOUT_PAYMENT_MODE" "$artifact_dir" <<'PY'
import concurrent.futures
import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

target_url = sys.argv[1].rstrip("/") + "/checkout?mode=sync"
run_id = sys.argv[2]
request_count = int(sys.argv[3])
concurrency_cap = int(sys.argv[4])
request_spacing_ms = int(sys.argv[5])
payment_mode = sys.argv[6]
artifact_dir = Path(sys.argv[7])
artifact_dir.mkdir(parents=True, exist_ok=True)

results_path = artifact_dir / f"{run_id}-client-results.json"
summary_path = artifact_dir / f"{run_id}-client-summary.json"

start_monotonic = time.perf_counter()
results = []

def send_request(sequence: int):
    correlation_id = f"{run_id}-{sequence:06d}"
    idempotency_key = f"idem-{run_id}-{sequence:06d}"
    user_id = f"user-{sequence:04d}"
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

    started_monotonic = time.perf_counter()
    response_status = None
    response_headers = {}
    response_body = None
    transport_error = None

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            response_status = response.getcode()
            response_headers = dict(response.info().items())
            response_body = response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        response_status = error.code
        response_headers = dict(error.headers.items())
        response_body = error.read().decode("utf-8")
    except Exception as error:
        transport_error = type(error).__name__

    elapsed_ms = (time.perf_counter() - started_monotonic) * 1000.0

    parsed = None
    if response_body:
        try:
            parsed = json.loads(response_body)
        except Exception:
            parsed = None

    result = {
        "sequence": sequence,
        "userId": user_id,
        "correlationId": correlation_id,
        "idempotencyKey": idempotency_key,
        "statusCode": response_status,
        "elapsedMs": elapsed_ms,
        "transportError": transport_error,
        "retryAfter": response_headers.get("Retry-After"),
        "error": parsed.get("error") if isinstance(parsed, dict) else None,
        "source": parsed.get("source") if isinstance(parsed, dict) else None,
        "contractSatisfied": parsed.get("contractSatisfied") if isinstance(parsed, dict) else None,
        "orderStatus": (parsed.get("order") or {}).get("status") if isinstance(parsed, dict) else None,
        "paymentStatus": (parsed.get("order") or {}).get("paymentStatus") if isinstance(parsed, dict) else None,
        "paymentErrorCode": (parsed.get("order") or {}).get("paymentErrorCode") if isinstance(parsed, dict) else None,
    }

    return result

with concurrent.futures.ThreadPoolExecutor(max_workers=concurrency_cap) as executor:
    pending = []

    for sequence in range(1, request_count + 1):
        target_elapsed = ((sequence - 1) * request_spacing_ms) / 1000.0
        remaining = target_elapsed - (time.perf_counter() - start_monotonic)
        if remaining > 0:
            time.sleep(remaining)

        pending.append(executor.submit(send_request, sequence))

        if len(pending) >= concurrency_cap:
            done, not_done = concurrent.futures.wait(
                pending,
                return_when=concurrent.futures.FIRST_COMPLETED,
            )
            for future in done:
                results.append(future.result())
            pending = list(not_done)

    for future in concurrent.futures.as_completed(pending):
        results.append(future.result())

results.sort(key=lambda item: item["sequence"])

status_counts = {}
error_counts = {}
business_status_counts = {}
payment_status_counts = {}
transport_error_counts = {}

for item in results:
    status_key = str(item["statusCode"]) if item["statusCode"] is not None else "none"
    status_counts[status_key] = status_counts.get(status_key, 0) + 1

    if item["error"]:
        error_counts[item["error"]] = error_counts.get(item["error"], 0) + 1

    if item["transportError"]:
        transport_error_counts[item["transportError"]] = transport_error_counts.get(item["transportError"], 0) + 1

    if item["orderStatus"]:
        business_status_counts[item["orderStatus"]] = business_status_counts.get(item["orderStatus"], 0) + 1

    if item["paymentStatus"]:
        payment_status_counts[item["paymentStatus"]] = payment_status_counts.get(item["paymentStatus"], 0) + 1

latencies = sorted(item["elapsedMs"] for item in results)

def percentile(values, p):
    if not values:
        return None
    index = max(0, min(len(values) - 1, int((p * len(values) + 0.9999999999)) - 1))
    return values[index]

summary = {
    "runId": run_id,
    "targetUrl": target_url,
    "requestCount": request_count,
    "concurrencyCap": concurrency_cap,
    "requestSpacingMs": request_spacing_ms,
    "paymentMode": payment_mode,
    "averageLatencyMs": (sum(latencies) / len(latencies)) if latencies else None,
    "p95LatencyMs": percentile(latencies, 0.95),
    "statusCounts": dict(sorted(status_counts.items())),
    "errorCounts": dict(sorted(error_counts.items())),
    "transportErrorCounts": dict(sorted(transport_error_counts.items())),
    "businessStatusCounts": dict(sorted(business_status_counts.items())),
    "paymentStatusCounts": dict(sorted(payment_status_counts.items())),
}

results_path.write_text(json.dumps(results, indent=2), encoding="utf-8")
summary_path.write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps(summary, indent=2))
PY
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

write_proxy_backend_counts() {
    local workspace="$1"
    local artifact_dir="$2"
    local run_id="$3"

    python3 - "$workspace/logs/proxy.log" "$run_id" "$artifact_dir/proxy-backend-counts.json" <<'PY'
import json
import re
import sys
from pathlib import Path

log_path = Path(sys.argv[1])
run_id = sys.argv[2]
output_path = Path(sys.argv[3])

counts = {}
pattern = re.compile(r"backend (?P<backend>\S+) with status")

if log_path.exists():
    for line in log_path.read_text(encoding="utf-8").splitlines():
        if f"RunId={run_id}" not in line:
            continue

        match = pattern.search(line)
        if not match:
            continue

        backend = match.group("backend")
        counts[backend] = counts.get(backend, 0) + 1

payload = {
    "runId": run_id,
    "backendCounts": dict(sorted(counts.items())),
}

output_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
print(json.dumps(payload, indent=2))
PY
}

run_cpu_arm() {
    local instance_count="$1"
    local workspace="$2"
    local artifact_dir="$3"
    local warmup_run_id="$4"
    local run_id="$5"

    prepare_workspace "$workspace" "$artifact_dir"
    stop_services

    start_storefront_instances "$workspace" "$artifact_dir" "$instance_count" "$ORDER_URL"
    start_proxy "$workspace" "$artifact_dir" "$instance_count"

    warm_up_cpu_path "$warmup_run_id" "$artifact_dir"
    run_cpu_load "$workspace" "$artifact_dir" "$run_id"

    stop_services

    analyze_run "$workspace" "$artifact_dir" "$run_id" "cpu-bound-lab" "cpu"
    copy_workspace_logs "$workspace" "$artifact_dir"
    write_proxy_backend_counts "$workspace" "$artifact_dir" "$run_id" > "$artifact_dir/${run_id}-proxy-backends.txt"
}

run_checkout_arm() {
    local instance_count="$1"
    local workspace="$2"
    local artifact_dir="$3"
    local warmup_run_id="$4"
    local run_id="$5"

    prepare_workspace "$workspace" "$artifact_dir"
    seed_workspace "$workspace" "$artifact_dir" "$CHECKOUT_PRODUCT_COUNT" "$CHECKOUT_USER_COUNT"
    stop_services

    start_checkout_dependencies "$workspace" "$artifact_dir"
    start_storefront_instances "$workspace" "$artifact_dir" "$instance_count" "$ORDER_URL"
    start_proxy "$workspace" "$artifact_dir" "$instance_count"

    local sequence
    local user_id
    local product_id
    for sequence in $(seq 1 "$CHECKOUT_REQUEST_COUNT"); do
        user_id="$(printf "user-%04d" "$sequence")"
        product_id="$(printf "sku-%04d" "$sequence")"
        ensure_cart_contains_item "${run_id}-cart-$sequence" "$user_id" "$product_id" "$CHECKOUT_ITEM_QUANTITY" "$artifact_dir"
    done

    local warmup_sequence
    warmup_sequence=$((CHECKOUT_REQUEST_COUNT + 1))
    ensure_cart_contains_item \
        "${warmup_run_id}-cart" \
        "$(printf "user-%04d" "$warmup_sequence")" \
        "$(printf "sku-%04d" "$warmup_sequence")" \
        "$CHECKOUT_ITEM_QUANTITY" \
        "$artifact_dir"

    warm_up_checkout_path "$warmup_run_id" "$(printf "user-%04d" "$warmup_sequence")" "$CHECKOUT_PAYMENT_MODE" "$artifact_dir"
    run_checkout_load "$run_id" "$artifact_dir"

    stop_services

    analyze_run "$workspace" "$artifact_dir" "$run_id" "storefront-checkout-sync" "storefront"
    analyze_run "$workspace" "$artifact_dir" "$run_id" "checkout-sync" "order"
    analyze_run "$workspace" "$artifact_dir" "$run_id" "payment-authorize" "payment"
    copy_workspace_logs "$workspace" "$artifact_dir"
    write_proxy_backend_counts "$workspace" "$artifact_dir" "$run_id" > "$artifact_dir/${run_id}-proxy-backends.txt"
}

write_comparison_json() {
    python3 - \
        "$CPU_ONE_ARTIFACT_ROOT" \
        "$CPU_TWO_ARTIFACT_ROOT" \
        "$CHECKOUT_ONE_ARTIFACT_ROOT" \
        "$CHECKOUT_TWO_ARTIFACT_ROOT" \
        "$COMPARISON_PATH" <<'PY'
import json
import sys
from pathlib import Path

cpu_one_root = Path(sys.argv[1])
cpu_two_root = Path(sys.argv[2])
checkout_one_root = Path(sys.argv[3])
checkout_two_root = Path(sys.argv[4])
output_path = Path(sys.argv[5])

def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))

def percent_change(one, two):
    if one in (None, 0) or two is None:
        return None
    return ((two - one) / one) * 100.0

def load_backend_counts(root: Path):
    return load_json(root / "proxy-backend-counts.json")

def load_cpu_side(root: Path):
    summary = load_json(next(root.glob("*-cpu-summary.json")))
    return {
        "summary": summary,
        "proxy": load_backend_counts(root),
    }

def load_checkout_side(root: Path):
    storefront = load_json(next(root.glob("*-storefront-summary.json")))
    order = load_json(next(root.glob("*-order-summary.json")))
    payment = load_json(next(root.glob("*-payment-summary.json")))
    client = load_json(next(root.glob("*-client-summary.json")))
    return {
        "storefront": storefront,
        "order": order,
        "payment": payment,
        "client": client,
        "proxy": load_backend_counts(root),
    }

cpu_one = load_cpu_side(cpu_one_root)
cpu_two = load_cpu_side(cpu_two_root)
checkout_one = load_checkout_side(checkout_one_root)
checkout_two = load_checkout_side(checkout_two_root)

comparison = {
    "generatedUtc": __import__("datetime").datetime.now(__import__("datetime").timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
    "frontendCpu": {
        "oneInstance": cpu_one,
        "twoInstance": cpu_two,
        "derived": {
            "throughputGainPercent": percent_change(
                cpu_one["summary"]["requests"]["throughputPerSecond"],
                cpu_two["summary"]["requests"]["throughputPerSecond"],
            ),
            "averageLatencyChangePercent": percent_change(
                cpu_one["summary"]["requests"]["averageLatencyMs"],
                cpu_two["summary"]["requests"]["averageLatencyMs"],
            ),
            "p95LatencyChangePercent": percent_change(
                cpu_one["summary"]["requests"]["p95LatencyMs"],
                cpu_two["summary"]["requests"]["p95LatencyMs"],
            ),
        },
    },
    "sharedCheckout": {
        "oneInstance": checkout_one,
        "twoInstance": checkout_two,
        "derived": {
            "storefrontThroughputGainPercent": percent_change(
                checkout_one["storefront"]["requests"]["throughputPerSecond"],
                checkout_two["storefront"]["requests"]["throughputPerSecond"],
            ),
            "storefrontAverageLatencyChangePercent": percent_change(
                checkout_one["storefront"]["requests"]["averageLatencyMs"],
                checkout_two["storefront"]["requests"]["averageLatencyMs"],
            ),
            "paymentAverageLatencyChangePercent": percent_change(
                checkout_one["payment"]["requests"]["averageLatencyMs"],
                checkout_two["payment"]["requests"]["averageLatencyMs"],
            ),
            "paymentThroughputGainPercent": percent_change(
                checkout_one["payment"]["requests"]["throughputPerSecond"],
                checkout_two["payment"]["requests"]["throughputPerSecond"],
            ),
        },
    },
}

output_path.write_text(json.dumps(comparison, indent=2), encoding="utf-8")
print(json.dumps(comparison, indent=2))
PY
}

echo "Preparing clean milestone-7 experiment workspace at: $WORKSPACE_ROOT"
rm -rf "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"
mkdir -p "$ARTIFACT_ROOT"

cd "$PROJECT_ROOT"

echo "Building solution..."
dotnet build ecommerce-systems-lab.sln > "$ARTIFACT_ROOT/build.log" 2>&1

echo "Running frontend CPU scenario with one Storefront instance..."
run_cpu_arm 1 "$CPU_ONE_WORKSPACE" "$CPU_ONE_ARTIFACT_ROOT" "$CPU_ONE_WARMUP_RUN_ID" "$CPU_ONE_RUN_ID"

echo "Running frontend CPU scenario with two Storefront instances..."
run_cpu_arm 2 "$CPU_TWO_WORKSPACE" "$CPU_TWO_ARTIFACT_ROOT" "$CPU_TWO_WARMUP_RUN_ID" "$CPU_TWO_RUN_ID"

echo "Running shared checkout scenario with one Storefront instance..."
run_checkout_arm 1 "$CHECKOUT_ONE_WORKSPACE" "$CHECKOUT_ONE_ARTIFACT_ROOT" "$CHECKOUT_ONE_WARMUP_RUN_ID" "$CHECKOUT_ONE_RUN_ID"

echo "Running shared checkout scenario with two Storefront instances..."
run_checkout_arm 2 "$CHECKOUT_TWO_WORKSPACE" "$CHECKOUT_TWO_ARTIFACT_ROOT" "$CHECKOUT_TWO_WARMUP_RUN_ID" "$CHECKOUT_TWO_RUN_ID"

echo "Writing comparison artifact..."
write_comparison_json > "$ARTIFACT_ROOT/comparison-pretty.txt"

echo "Milestone-7 one-instance vs two-instance scaling experiment completed."
echo "Artifacts root:     $ARTIFACT_ROOT"
echo "Comparison summary: $COMPARISON_PATH"
