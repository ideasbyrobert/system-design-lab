#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

WORKSPACE_ROOT="${1:-$(operator_default_workspace)}"
REGION="${LAB_REGION:-us-east}"
PRIMARY_REGION="${LAB_PRIMARY_REGION:-us-east}"
EAST_REGION="${LAB_EAST_REGION:-us-east}"
WEST_REGION="${LAB_WEST_REGION:-us-west}"
SAME_REGION_LATENCY_MS="${LAB_SAME_REGION_LATENCY_MS:-2}"
CROSS_REGION_LATENCY_MS="${LAB_CROSS_REGION_LATENCY_MS:-17}"
QUEUE_POLL_INTERVAL_MS="${LAB_QUEUE_POLL_INTERVAL_MS:-50}"

STOREFRONT_URL="${LAB_STOREFRONT_URL:-http://127.0.0.1:5202}"
CATALOG_URL="${LAB_CATALOG_URL:-http://127.0.0.1:5203}"
CART_URL="${LAB_CART_URL:-http://127.0.0.1:5204}"
ORDER_URL="${LAB_ORDER_URL:-http://127.0.0.1:5205}"
PAYMENT_URL="${LAB_PAYMENT_URL:-http://127.0.0.1:5206}"

MANIFEST_PATH="$(operator_manifest_path "$WORKSPACE_ROOT" "core-topology")"

operator_stop_all "$WORKSPACE_ROOT"
operator_build "$WORKSPACE_ROOT"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "catalog" \
    "src/Catalog.Api" \
    "$CATALOG_URL" \
    "/" \
    "LAB__Regions__CurrentRegion=$REGION" \
    "LAB__Regions__PrimaryRegion=$PRIMARY_REGION" \
    "LAB__Regions__EastReplicaRegion=$EAST_REGION" \
    "LAB__Regions__WestReplicaRegion=$WEST_REGION"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "cart" \
    "src/Cart.Api" \
    "$CART_URL" \
    "/" \
    "LAB__Regions__CurrentRegion=$REGION"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "payment-simulator" \
    "src/PaymentSimulator.Api" \
    "$PAYMENT_URL" \
    "/" \
    "LAB__Regions__CurrentRegion=$REGION"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "order" \
    "src/Order.Api" \
    "$ORDER_URL" \
    "/" \
    "LAB__Regions__CurrentRegion=$REGION" \
    "LAB__ServiceEndpoints__PaymentSimulatorBaseUrl=$PAYMENT_URL" \
    "LAB__ServiceEndpoints__PaymentSimulatorRegion=$REGION"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "storefront" \
    "src/Storefront.Api" \
    "$STOREFRONT_URL" \
    "/health" \
    "LAB__Regions__CurrentRegion=$REGION" \
    "LAB__Regions__PrimaryRegion=$PRIMARY_REGION" \
    "LAB__Regions__EastReplicaRegion=$EAST_REGION" \
    "LAB__Regions__WestReplicaRegion=$WEST_REGION" \
    "LAB__Regions__SameRegionLatencyMs=$SAME_REGION_LATENCY_MS" \
    "LAB__Regions__CrossRegionLatencyMs=$CROSS_REGION_LATENCY_MS" \
    "LAB__ServiceEndpoints__CatalogBaseUrl=$CATALOG_URL" \
    "LAB__ServiceEndpoints__CatalogRegion=$REGION" \
    "LAB__ServiceEndpoints__CartBaseUrl=$CART_URL" \
    "LAB__ServiceEndpoints__CartRegion=$REGION" \
    "LAB__ServiceEndpoints__OrderBaseUrl=$ORDER_URL" \
    "LAB__ServiceEndpoints__OrderRegion=$REGION"

operator_start_background_service \
    "$WORKSPACE_ROOT" \
    "worker" \
    "src/Worker" \
    "LAB__Regions__CurrentRegion=$REGION" \
    "LAB__Queue__PollIntervalMilliseconds=$QUEUE_POLL_INTERVAL_MS" \
    "LAB__ServiceEndpoints__PaymentSimulatorBaseUrl=$PAYMENT_URL" \
    "LAB__ServiceEndpoints__PaymentSimulatorRegion=$REGION"

cat > "$MANIFEST_PATH" <<EOF
WORKSPACE_ROOT=$WORKSPACE_ROOT
STOREFRONT_URL=$STOREFRONT_URL
CATALOG_URL=$CATALOG_URL
CART_URL=$CART_URL
ORDER_URL=$ORDER_URL
PAYMENT_URL=$PAYMENT_URL
REGION=$REGION
EOF

cat <<EOF
Core topology is running.

Workspace root:
  $WORKSPACE_ROOT

User-visible boundary:
  Storefront.Api -> $STOREFRONT_URL

Supporting services:
  Catalog.Api -> $CATALOG_URL
  Cart.Api -> $CART_URL
  Order.Api -> $ORDER_URL
  PaymentSimulator.Api -> $PAYMENT_URL
  Worker -> background only

Operator files:
  Build log: $(operator_build_log_path "$WORKSPACE_ROOT")
  Manifest:  $MANIFEST_PATH
  PID/log dir: $(operator_runtime_dir "$WORKSPACE_ROOT")

Next useful commands:
  curl $STOREFRONT_URL/health
  curl '$STOREFRONT_URL/products/sku-0001?cache=off&readSource=primary'
  ./scripts/operators/stop-topology.sh '$WORKSPACE_ROOT'
EOF
