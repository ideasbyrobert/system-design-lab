#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKSPACE_ROOT="${LAB_EXPERIMENT_ROOT:-$PROJECT_ROOT/docs/experiments/milestone-4-synchronous-checkout/workspace}"
ARTIFACT_ROOT="$PROJECT_ROOT/docs/experiments/milestone-4-synchronous-checkout/artifacts"
CART_URL="${LAB_CART_URL:-http://127.0.0.1:5085}"
PAYMENT_URL="${LAB_PAYMENT_URL:-http://127.0.0.1:5086}"
ORDER_URL="${LAB_ORDER_URL:-http://127.0.0.1:5087}"
PRODUCT_COUNT="${LAB_EXPERIMENT_PRODUCT_COUNT:-5}"
USER_COUNT="${LAB_EXPERIMENT_USER_COUNT:-5}"
ITEM_QUANTITY="${LAB_EXPERIMENT_ITEM_QUANTITY:-1}"
WARMUP_RUN_ID="${LAB_WARMUP_RUN_ID:-milestone-4-sync-warmup}"
FAST_RUN_ID="${LAB_FAST_RUN_ID:-milestone-4-sync-fast-success}"
SLOW_RUN_ID="${LAB_SLOW_RUN_ID:-milestone-4-sync-slow-success}"
TIMEOUT_RUN_ID="${LAB_TIMEOUT_RUN_ID:-milestone-4-sync-timeout}"
TRANSIENT_RUN_ID="${LAB_TRANSIENT_RUN_ID:-milestone-4-sync-transient-failure}"
CART_LOG_PATH="$ARTIFACT_ROOT/cart.log"
PAYMENT_LOG_PATH="$ARTIFACT_ROOT/payment-simulator.log"
ORDER_LOG_PATH="$ARTIFACT_ROOT/order.log"

cleanup() {
    if [[ -n "${ORDER_PID:-}" ]] && kill -0 "$ORDER_PID" 2>/dev/null; then
        kill "$ORDER_PID" 2>/dev/null || true
        wait "$ORDER_PID" 2>/dev/null || true
    fi

    if [[ -n "${PAYMENT_PID:-}" ]] && kill -0 "$PAYMENT_PID" 2>/dev/null; then
        kill "$PAYMENT_PID" 2>/dev/null || true
        wait "$PAYMENT_PID" 2>/dev/null || true
    fi

    if [[ -n "${CART_PID:-}" ]] && kill -0 "$CART_PID" 2>/dev/null; then
        kill "$CART_PID" 2>/dev/null || true
        wait "$CART_PID" 2>/dev/null || true
    fi
}

trap cleanup EXIT

wait_for_http() {
    local url="$1"
    local process_id="$2"
    local log_path="$3"
    local service_name="$4"

    for attempt in {1..60}; do
        if curl -fsS "$url" > /dev/null 2>&1; then
            return 0
        fi

        if ! kill -0 "$process_id" 2>/dev/null; then
            echo "$service_name exited before becoming healthy. See $log_path" >&2
            exit 1
        fi

        sleep 0.2

        if [[ "$attempt" -eq 60 ]]; then
            echo "Timed out waiting for $service_name to become healthy. See $log_path" >&2
            exit 1
        fi
    done
}

add_cart_item() {
    local run_id="$1"
    local user_id="$2"
    local product_id="$3"
    local quantity="$4"
    local body_path="$ARTIFACT_ROOT/${run_id}-cart-response.json"
    local headers_path="$ARTIFACT_ROOT/${run_id}-cart-response-headers.txt"
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

analyze_run() {
    local run_id="$1"
    local operation="$2"
    local suffix="$3"

    LAB__Repository__RootPath="$WORKSPACE_ROOT" \
    dotnet run --project src/Analyze --no-build -- \
        --run-id "$run_id" \
        --operation "$operation" > "$ARTIFACT_ROOT/${run_id}-${suffix}-analyze.txt" 2>&1

    cp "$WORKSPACE_ROOT/logs/runs/$run_id/summary.json" "$ARTIFACT_ROOT/${run_id}-${suffix}-summary.json"
    cp "$WORKSPACE_ROOT/analysis/$run_id/report.md" "$ARTIFACT_ROOT/${run_id}-${suffix}-analysis.md"
}

run_checkout_case() {
    local run_id="$1"
    local user_id="$2"
    local product_id="$3"
    local quantity="$4"
    local payment_mode="$5"
    local idempotency_key="$6"
    local setup_run_id="${run_id}-setup"
    local response_body_path="$ARTIFACT_ROOT/${run_id}-response.json"
    local response_headers_path="$ARTIFACT_ROOT/${run_id}-response-headers.txt"
    local status_code

    echo "Preparing cart for $run_id ($user_id / $product_id / qty=$quantity)"
    add_cart_item "$setup_run_id" "$user_id" "$product_id" "$quantity"

    echo "Running checkout for $run_id with payment mode '$payment_mode'"
    status_code="$(curl -sS -D "$response_headers_path" -o "$response_body_path" -w '%{http_code}' \
        -X POST "$ORDER_URL/orders/checkout" \
        -H 'Content-Type: application/json' \
        -H 'X-Debug-Telemetry: true' \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $run_id-order" \
        -H "Idempotency-Key: $idempotency_key" \
        -d "{\"userId\":\"$user_id\",\"paymentMode\":\"$payment_mode\"}")"

    if [[ "$status_code" != "200" ]]; then
        echo "Checkout experiment '$run_id' failed with HTTP $status_code. See $response_body_path" >&2
        exit 1
    fi

    echo "Analyzing checkout boundary for $run_id"
    analyze_run "$run_id" "checkout-sync" "checkout"

    echo "Analyzing payment boundary for $run_id"
    analyze_run "$run_id" "payment-authorize" "payment"
}

warm_up_checkout_path() {
    local run_id="$1"
    local user_id="$2"
    local product_id="$3"
    local quantity="$4"
    local payment_mode="$5"

    echo "Warming up checkout path with $run_id"
    add_cart_item "${run_id}-setup" "$user_id" "$product_id" "$quantity"

    local status_code
    status_code="$(curl -sS -o /dev/null -w '%{http_code}' \
        -X POST "$ORDER_URL/orders/checkout" \
        -H 'Content-Type: application/json' \
        -H "X-Run-Id: $run_id" \
        -H "X-Correlation-Id: $run_id-order" \
        -H "Idempotency-Key: idem-$run_id" \
        -d "{\"userId\":\"$user_id\",\"paymentMode\":\"$payment_mode\"}")"

    if [[ "$status_code" != "200" ]]; then
        echo "Warm-up checkout failed with HTTP $status_code." >&2
        exit 1
    fi
}

echo "Preparing clean milestone-4 experiment workspace at: $WORKSPACE_ROOT"
rm -rf "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"
mkdir -p "$WORKSPACE_ROOT" "$ARTIFACT_ROOT"

cd "$PROJECT_ROOT"

echo "Building solution..."
dotnet build ecommerce-systems-lab.sln > "$ARTIFACT_ROOT/build.log" 2>&1

echo "Seeding primary data..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/SeedData --no-build -- \
    --products "$PRODUCT_COUNT" \
    --users "$USER_COUNT" \
    --reset true > "$ARTIFACT_ROOT/seed-data.txt" 2>&1

echo "Starting Cart.Api at $CART_URL ..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/Cart.Api --no-build --urls "$CART_URL" > "$CART_LOG_PATH" 2>&1 &
CART_PID=$!

echo "Starting PaymentSimulator.Api at $PAYMENT_URL ..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
dotnet run --project src/PaymentSimulator.Api --no-build --urls "$PAYMENT_URL" > "$PAYMENT_LOG_PATH" 2>&1 &
PAYMENT_PID=$!

echo "Starting Order.Api at $ORDER_URL ..."
LAB__Repository__RootPath="$WORKSPACE_ROOT" \
LAB__ServiceEndpoints__PaymentSimulatorBaseUrl="$PAYMENT_URL" \
dotnet run --project src/Order.Api --no-build --urls "$ORDER_URL" > "$ORDER_LOG_PATH" 2>&1 &
ORDER_PID=$!

echo "Waiting for Cart.Api, PaymentSimulator.Api, and Order.Api to become healthy..."
wait_for_http "$CART_URL/" "$CART_PID" "$CART_LOG_PATH" "Cart.Api"
wait_for_http "$PAYMENT_URL/" "$PAYMENT_PID" "$PAYMENT_LOG_PATH" "PaymentSimulator.Api"
wait_for_http "$ORDER_URL/" "$ORDER_PID" "$ORDER_LOG_PATH" "Order.Api"

warm_up_checkout_path "$WARMUP_RUN_ID" "user-0005" "sku-0005" "$ITEM_QUANTITY" "fast_success"

run_checkout_case "$FAST_RUN_ID" "user-0001" "sku-0001" "$ITEM_QUANTITY" "fast_success" "idem-$FAST_RUN_ID"
run_checkout_case "$SLOW_RUN_ID" "user-0002" "sku-0002" "$ITEM_QUANTITY" "slow_success" "idem-$SLOW_RUN_ID"
run_checkout_case "$TIMEOUT_RUN_ID" "user-0003" "sku-0003" "$ITEM_QUANTITY" "timeout" "idem-$TIMEOUT_RUN_ID"
run_checkout_case "$TRANSIENT_RUN_ID" "user-0004" "sku-0004" "$ITEM_QUANTITY" "transient_failure" "idem-$TRANSIENT_RUN_ID"

cp "$WORKSPACE_ROOT/logs/requests.jsonl" "$ARTIFACT_ROOT/requests.jsonl"

echo "Milestone-4 synchronous checkout experiment completed."
echo "Workspace:              $WORKSPACE_ROOT"
echo "Artifacts:              $ARTIFACT_ROOT"
echo "Fast-success summary:   $ARTIFACT_ROOT/$FAST_RUN_ID-checkout-summary.json"
echo "Slow-success summary:   $ARTIFACT_ROOT/$SLOW_RUN_ID-checkout-summary.json"
echo "Timeout summary:        $ARTIFACT_ROOT/$TIMEOUT_RUN_ID-checkout-summary.json"
echo "Transient summary:      $ARTIFACT_ROOT/$TRANSIENT_RUN_ID-checkout-summary.json"
