#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXPERIMENT_ROOT="$PROJECT_ROOT/docs/experiments/milestone-6-no-limit-vs-rate-limit-overload"
WORKSPACE_ROOT="${LAB_EXPERIMENT_ROOT:-$EXPERIMENT_ROOT/workspace}"
NO_LIMIT_WORKSPACE="$WORKSPACE_ROOT/no-limit"
RATE_LIMIT_WORKSPACE="$WORKSPACE_ROOT/rate-limit"
ARTIFACT_ROOT="$EXPERIMENT_ROOT/artifacts"
NO_LIMIT_ARTIFACT_ROOT="$ARTIFACT_ROOT/no-limit"
RATE_LIMIT_ARTIFACT_ROOT="$ARTIFACT_ROOT/rate-limit"
COMPARISON_PATH="$ARTIFACT_ROOT/comparison.json"

CART_URL="${LAB_CART_URL:-http://127.0.0.1:5085}"
PAYMENT_URL="${LAB_PAYMENT_URL:-http://127.0.0.1:5086}"
ORDER_URL="${LAB_ORDER_URL:-http://127.0.0.1:5087}"
STOREFRONT_URL="${LAB_STOREFRONT_URL:-http://127.0.0.1:5088}"

PRODUCT_COUNT="${LAB_EXPERIMENT_PRODUCT_COUNT:-4}"
USER_COUNT="${LAB_EXPERIMENT_USER_COUNT:-4}"
ITEM_QUANTITY="${LAB_EXPERIMENT_ITEM_QUANTITY:-1}"
REQUEST_COUNT="${LAB_REQUEST_COUNT:-80}"
CONCURRENCY_CAP="${LAB_CONCURRENCY_CAP:-64}"
REQUEST_SPACING_MS="${LAB_REQUEST_SPACING_MS:-10}"
PAYMENT_MODE="${LAB_PAYMENT_MODE:-fast_success}"
SLOW_LATENCY_MS="${LAB_EXPERIMENT_SLOW_LATENCY_MS:-350}"
FAST_LATENCY_MS="${LAB_EXPERIMENT_FAST_LATENCY_MS:-5}"
TIMEOUT_LATENCY_MS="${LAB_EXPERIMENT_TIMEOUT_LATENCY_MS:-700}"
DELAYED_CONFIRMATION_MS="${LAB_EXPERIMENT_DELAYED_CONFIRMATION_MS:-300}"
DUPLICATE_CALLBACK_SPACING_MS="${LAB_EXPERIMENT_DUPLICATE_CALLBACK_SPACING_MS:-50}"
DISPATCHER_POLL_MS="${LAB_EXPERIMENT_DISPATCHER_POLL_MS:-20}"

LIMITED_BUCKET_CAPACITY="${LAB_LIMITED_BUCKET_CAPACITY:-1}"
LIMITED_TOKENS_PER_SECOND="${LAB_LIMITED_TOKENS_PER_SECOND:-5}"

WARMUP_USER_ID="${LAB_WARMUP_USER_ID:-user-0001}"
WARMUP_PRODUCT_ID="${LAB_WARMUP_PRODUCT_ID:-sku-0001}"
MEASURED_USER_ID="${LAB_MEASURED_USER_ID:-user-0002}"
MEASURED_PRODUCT_ID="${LAB_MEASURED_PRODUCT_ID:-sku-0002}"

NO_LIMIT_RUN_ID="${LAB_NO_LIMIT_RUN_ID:-milestone-6-storefront-sync-no-limit}"
RATE_LIMIT_RUN_ID="${LAB_RATE_LIMIT_RUN_ID:-milestone-6-storefront-sync-rate-limit}"
NO_LIMIT_WARMUP_RUN_ID="${LAB_NO_LIMIT_WARMUP_RUN_ID:-milestone-6-storefront-sync-no-limit-warmup}"
RATE_LIMIT_WARMUP_RUN_ID="${LAB_RATE_LIMIT_WARMUP_RUN_ID:-milestone-6-storefront-sync-rate-limit-warmup}"

CART_PID=""
PAYMENT_PID=""
ORDER_PID=""
STOREFRONT_PID=""

reset_service_pids() {
    CART_PID=""
    PAYMENT_PID=""
    ORDER_PID=""
    STOREFRONT_PID=""
}

stop_services() {
    local pid

    for pid in "$STOREFRONT_PID" "$ORDER_PID" "$PAYMENT_PID" "$CART_PID"; do
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

    LAB__Repository__RootPath="$workspace" \
    dotnet run --project src/SeedData --no-build -- \
        --products "$PRODUCT_COUNT" \
        --users "$USER_COUNT" \
        --reset true > "$artifact_dir/seed-data.txt" 2>&1
}

start_services() {
    local workspace="$1"
    local artifact_dir="$2"
    local rate_limit_enabled="$3"
    local bucket_capacity="$4"
    local tokens_per_second="$5"

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
    LAB__RateLimiter__Checkout__Enabled="$rate_limit_enabled" \
    LAB__RateLimiter__Checkout__TokenBucketCapacity="$bucket_capacity" \
    LAB__RateLimiter__Checkout__TokensPerSecond="$tokens_per_second" \
    dotnet run --project src/Storefront.Api --no-build --urls "$STOREFRONT_URL" > "$artifact_dir/storefront.log" 2>&1 &
    STOREFRONT_PID=$!

    wait_for_http "$CART_URL/" "$CART_PID" "$artifact_dir/cart.log" "Cart.Api"
    wait_for_http "$PAYMENT_URL/" "$PAYMENT_PID" "$artifact_dir/payment-simulator.log" "PaymentSimulator.Api"
    wait_for_http "$ORDER_URL/" "$ORDER_PID" "$artifact_dir/order.log" "Order.Api"
    wait_for_http "$STOREFRONT_URL/health" "$STOREFRONT_PID" "$artifact_dir/storefront.log" "Storefront.Api"
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

warm_up_checkout_path() {
    local run_id="$1"
    local user_id="$2"
    local product_id="$3"
    local quantity="$4"
    local payment_mode="$5"
    local artifact_dir="$6"
    local response_body_path="$artifact_dir/${run_id}-warmup-response.json"
    local response_headers_path="$artifact_dir/${run_id}-warmup-response-headers.txt"
    local status_code

    ensure_cart_contains_item "${run_id}-setup" "$user_id" "$product_id" "$quantity" "$artifact_dir"

    status_code="$(curl -sS -D "$response_headers_path" -o "$response_body_path" -w '%{http_code}' \
        -X POST "$STOREFRONT_URL/checkout?mode=sync" \
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
    local user_id="$2"
    local payment_mode="$3"
    local artifact_dir="$4"

    python3 - "$STOREFRONT_URL" "$run_id" "$REQUEST_COUNT" "$CONCURRENCY_CAP" "$REQUEST_SPACING_MS" "$user_id" "$payment_mode" "$artifact_dir" <<'PY'
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
user_id = sys.argv[6]
payment_mode = sys.argv[7]
artifact_dir = Path(sys.argv[8])
artifact_dir.mkdir(parents=True, exist_ok=True)

results_path = artifact_dir / f"{run_id}-client-results.json"
summary_path = artifact_dir / f"{run_id}-client-summary.json"

start_monotonic = time.perf_counter()
results = []

def send_request(sequence: int):
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
            "X-Debug-Telemetry": "true",
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
        "correlationId": correlation_id,
        "idempotencyKey": idempotency_key,
        "statusCode": response_status,
        "elapsedMs": elapsed_ms,
        "transportError": transport_error,
        "retryAfter": response_headers.get("Retry-After"),
        "error": parsed.get("error") if isinstance(parsed, dict) else None,
        "detail": parsed.get("detail") if isinstance(parsed, dict) else None,
        "contractSatisfied": parsed.get("contractSatisfied") if isinstance(parsed, dict) else None,
        "source": parsed.get("source") if isinstance(parsed, dict) else None,
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
    "userId": user_id,
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

    cp "$workspace/logs/requests.jsonl" "$artifact_dir/requests.jsonl"

    if [[ -f "$workspace/logs/jobs.jsonl" ]]; then
        cp "$workspace/logs/jobs.jsonl" "$artifact_dir/jobs.jsonl"
    fi
}

write_comparison_json() {
    python3 - "$NO_LIMIT_ARTIFACT_ROOT" "$RATE_LIMIT_ARTIFACT_ROOT" "$COMPARISON_PATH" <<'PY'
import json
import sys
from pathlib import Path

no_limit_root = Path(sys.argv[1])
rate_limit_root = Path(sys.argv[2])
output_path = Path(sys.argv[3])

def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))

def load_side(root: Path):
    storefront = load_json(next(root.glob("*-storefront-summary.json")))
    payment = load_json(next(root.glob("*-payment-summary.json")))
    client = load_json(next(root.glob("*-client-summary.json")))

    return {
        "storefront": storefront,
        "payment": payment,
        "client": client,
        "derived": {
            "admittedThroughputPerSecond": storefront["overload"]["admittedRequests"]["throughputPerSecond"],
            "admittedP95LatencyMs": storefront["overload"]["admittedRequests"]["p95LatencyMs"],
            "rejectFraction": storefront["overload"]["rejectFraction"],
            "timeoutFraction": storefront["overload"]["timeoutFraction"],
            "rejectedRequestCount": storefront["overload"]["rejectedRequestCount"],
            "admittedRequestCount": storefront["overload"]["admittedRequestCount"],
            "downstreamPaymentRequestCount": payment["requests"]["requestCount"],
            "downstreamPaymentErrorCounts": payment["requests"]["errorCounts"],
        },
    }

comparison = {
    "noLimit": load_side(no_limit_root),
    "rateLimit": load_side(rate_limit_root),
}

output_path.write_text(json.dumps(comparison, indent=2), encoding="utf-8")
print(json.dumps(comparison, indent=2))
PY
}

run_arm() {
    local workspace="$1"
    local artifact_dir="$2"
    local warmup_run_id="$3"
    local measured_run_id="$4"
    local rate_limit_enabled="$5"
    local bucket_capacity="$6"
    local tokens_per_second="$7"

    prepare_workspace "$workspace" "$artifact_dir"
    seed_workspace "$workspace" "$artifact_dir"
    start_services "$workspace" "$artifact_dir" "$rate_limit_enabled" "$bucket_capacity" "$tokens_per_second"

    ensure_cart_contains_item "${warmup_run_id}-cart" "$WARMUP_USER_ID" "$WARMUP_PRODUCT_ID" "$ITEM_QUANTITY" "$artifact_dir"
    ensure_cart_contains_item "${measured_run_id}-cart" "$MEASURED_USER_ID" "$MEASURED_PRODUCT_ID" "$ITEM_QUANTITY" "$artifact_dir"

    warm_up_checkout_path "$warmup_run_id" "$WARMUP_USER_ID" "$WARMUP_PRODUCT_ID" "$ITEM_QUANTITY" "$PAYMENT_MODE" "$artifact_dir"
    run_checkout_load "$measured_run_id" "$MEASURED_USER_ID" "$PAYMENT_MODE" "$artifact_dir"

    stop_services

    analyze_run "$workspace" "$artifact_dir" "$measured_run_id" "storefront-checkout-sync" "storefront"
    analyze_run "$workspace" "$artifact_dir" "$measured_run_id" "payment-authorize" "payment"
    copy_workspace_logs "$workspace" "$artifact_dir"
}

echo "Preparing clean milestone-6 experiment workspace at: $WORKSPACE_ROOT"
rm -rf "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"
mkdir -p "$ARTIFACT_ROOT"

cd "$PROJECT_ROOT"

echo "Building solution..."
dotnet build ecommerce-systems-lab.sln > "$ARTIFACT_ROOT/build.log" 2>&1

echo "Running no-limit arm..."
run_arm \
    "$NO_LIMIT_WORKSPACE" \
    "$NO_LIMIT_ARTIFACT_ROOT" \
    "$NO_LIMIT_WARMUP_RUN_ID" \
    "$NO_LIMIT_RUN_ID" \
    "false" \
    "$LIMITED_BUCKET_CAPACITY" \
    "$LIMITED_TOKENS_PER_SECOND"

echo "Running rate-limit arm..."
run_arm \
    "$RATE_LIMIT_WORKSPACE" \
    "$RATE_LIMIT_ARTIFACT_ROOT" \
    "$RATE_LIMIT_WARMUP_RUN_ID" \
    "$RATE_LIMIT_RUN_ID" \
    "true" \
    "$LIMITED_BUCKET_CAPACITY" \
    "$LIMITED_TOKENS_PER_SECOND"

echo "Writing comparison artifact..."
write_comparison_json > "$ARTIFACT_ROOT/comparison-pretty.txt"

echo "Milestone-6 no-limit vs rate-limit overload experiment completed."
echo "No-limit artifacts:   $NO_LIMIT_ARTIFACT_ROOT"
echo "Rate-limit artifacts: $RATE_LIMIT_ARTIFACT_ROOT"
echo "Comparison JSON:      $COMPARISON_PATH"
