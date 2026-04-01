#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

WORKSPACE_ROOT="${1:-$(operator_default_workspace)}"
PRIMARY_REGION="${LAB_PRIMARY_REGION:-us-east}"
EAST_REGION="${LAB_EAST_REGION:-us-east}"
WEST_REGION="${LAB_WEST_REGION:-us-west}"
SAME_REGION_LATENCY_MS="${LAB_SAME_REGION_LATENCY_MS:-2}"
CROSS_REGION_LATENCY_MS="${LAB_CROSS_REGION_LATENCY_MS:-17}"
ROUTING_MODE="${LAB_PROXY_ROUTING_MODE:-round_robin}"

PROXY_URL="${LAB_PROXY_URL:-http://127.0.0.1:5300}"
EAST_STOREFRONT_URL="${LAB_EAST_STOREFRONT_URL:-http://127.0.0.1:5301}"
WEST_STOREFRONT_URL="${LAB_WEST_STOREFRONT_URL:-http://127.0.0.1:5302}"
EAST_CATALOG_URL="${LAB_EAST_CATALOG_URL:-http://127.0.0.1:5303}"
WEST_CATALOG_URL="${LAB_WEST_CATALOG_URL:-http://127.0.0.1:5304}"

MANIFEST_PATH="$(operator_manifest_path "$WORKSPACE_ROOT" "east-west-topology")"

operator_stop_all "$WORKSPACE_ROOT"
operator_build "$WORKSPACE_ROOT"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "catalog-east" \
    "src/Catalog.Api" \
    "$EAST_CATALOG_URL" \
    "/" \
    "LAB__Regions__CurrentRegion=$EAST_REGION" \
    "LAB__Regions__PrimaryRegion=$PRIMARY_REGION" \
    "LAB__Regions__EastReplicaRegion=$EAST_REGION" \
    "LAB__Regions__WestReplicaRegion=$WEST_REGION"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "catalog-west" \
    "src/Catalog.Api" \
    "$WEST_CATALOG_URL" \
    "/" \
    "LAB__Regions__CurrentRegion=$WEST_REGION" \
    "LAB__Regions__PrimaryRegion=$PRIMARY_REGION" \
    "LAB__Regions__EastReplicaRegion=$EAST_REGION" \
    "LAB__Regions__WestReplicaRegion=$WEST_REGION"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "storefront-east" \
    "src/Storefront.Api" \
    "$EAST_STOREFRONT_URL" \
    "/health" \
    "LAB__Regions__CurrentRegion=$EAST_REGION" \
    "LAB__Regions__PrimaryRegion=$PRIMARY_REGION" \
    "LAB__Regions__EastReplicaRegion=$EAST_REGION" \
    "LAB__Regions__WestReplicaRegion=$WEST_REGION" \
    "LAB__Regions__SameRegionLatencyMs=$SAME_REGION_LATENCY_MS" \
    "LAB__Regions__CrossRegionLatencyMs=$CROSS_REGION_LATENCY_MS" \
    "LAB__ServiceEndpoints__CatalogBaseUrl=$EAST_CATALOG_URL" \
    "LAB__ServiceEndpoints__CatalogRegion=$EAST_REGION" \
    "LAB__ServiceEndpoints__CatalogFailoverBaseUrl=$WEST_CATALOG_URL" \
    "LAB__ServiceEndpoints__CatalogFailoverRegion=$WEST_REGION"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "storefront-west" \
    "src/Storefront.Api" \
    "$WEST_STOREFRONT_URL" \
    "/health" \
    "LAB__Regions__CurrentRegion=$WEST_REGION" \
    "LAB__Regions__PrimaryRegion=$PRIMARY_REGION" \
    "LAB__Regions__EastReplicaRegion=$EAST_REGION" \
    "LAB__Regions__WestReplicaRegion=$WEST_REGION" \
    "LAB__Regions__SameRegionLatencyMs=$SAME_REGION_LATENCY_MS" \
    "LAB__Regions__CrossRegionLatencyMs=$CROSS_REGION_LATENCY_MS" \
    "LAB__ServiceEndpoints__CatalogBaseUrl=$WEST_CATALOG_URL" \
    "LAB__ServiceEndpoints__CatalogRegion=$WEST_REGION" \
    "LAB__ServiceEndpoints__CatalogFailoverBaseUrl=$EAST_CATALOG_URL" \
    "LAB__ServiceEndpoints__CatalogFailoverRegion=$EAST_REGION"

operator_start_http_service \
    "$WORKSPACE_ROOT" \
    "proxy" \
    "src/Proxy" \
    "$PROXY_URL" \
    "/proxy/status" \
    "LAB__Proxy__RoutingMode=$ROUTING_MODE" \
    "LAB__Proxy__Catalog__Enabled=false" \
    "LAB__Proxy__Storefront__Backends__0=$EAST_STOREFRONT_URL" \
    "LAB__Proxy__Storefront__Backends__1=$WEST_STOREFRONT_URL"

cat > "$MANIFEST_PATH" <<EOF
WORKSPACE_ROOT=$WORKSPACE_ROOT
PROXY_URL=$PROXY_URL
EAST_STOREFRONT_URL=$EAST_STOREFRONT_URL
WEST_STOREFRONT_URL=$WEST_STOREFRONT_URL
EAST_CATALOG_URL=$EAST_CATALOG_URL
WEST_CATALOG_URL=$WEST_CATALOG_URL
ROUTING_MODE=$ROUTING_MODE
EOF

cat <<EOF
East/west topology is running.

Workspace root:
  $WORKSPACE_ROOT

Proxy:
  Proxy -> $PROXY_URL

Regional services:
  East Storefront -> $EAST_STOREFRONT_URL
  West Storefront -> $WEST_STOREFRONT_URL
  East Catalog -> $EAST_CATALOG_URL
  West Catalog -> $WEST_CATALOG_URL

Operator files:
  Build log: $(operator_build_log_path "$WORKSPACE_ROOT")
  Manifest:  $MANIFEST_PATH
  PID/log dir: $(operator_runtime_dir "$WORKSPACE_ROOT")

Next useful commands:
  curl $PROXY_URL/proxy/status
  curl -H 'X-Session-Key: sess-demo' '$PROXY_URL/health'
  curl '$WEST_STOREFRONT_URL/products/sku-0001?cache=off&readSource=local'
  ./scripts/operators/stop-topology.sh '$WORKSPACE_ROOT'
EOF
