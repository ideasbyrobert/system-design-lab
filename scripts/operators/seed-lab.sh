#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

WORKSPACE_ROOT="${1:-$(operator_default_workspace)}"
PRODUCT_COUNT="${LAB_PRODUCT_COUNT:-25}"
USER_COUNT="${LAB_USER_COUNT:-8}"
RESET="${LAB_RESET:-true}"
REBUILD_PRODUCT_PAGE_PROJECTION="${LAB_REBUILD_PRODUCT_PAGE_PROJECTION:-true}"
SYNC_REPLICAS="${LAB_SYNC_REPLICAS:-true}"
REPLICA_EAST_LAG_MS="${LAB_REPLICA_EAST_LAG_MS:-0}"
REPLICA_WEST_LAG_MS="${LAB_REPLICA_WEST_LAG_MS:-0}"
LOG_PATH="$(operator_log_path "$WORKSPACE_ROOT" "seed-data")"

operator_ensure_runtime_dirs "$WORKSPACE_ROOT"

COMMAND=(
    dotnet run --project src/SeedData --
    --products "$PRODUCT_COUNT"
    --users "$USER_COUNT"
    --reset "$RESET"
)

if [[ "$REBUILD_PRODUCT_PAGE_PROJECTION" == "true" ]]; then
    COMMAND+=(--rebuild-product-page-projection true)
fi

if [[ "$SYNC_REPLICAS" == "true" ]]; then
    COMMAND+=(--sync-replicas true --replica-east-lag-ms "$REPLICA_EAST_LAG_MS" --replica-west-lag-ms "$REPLICA_WEST_LAG_MS")
fi

(
    cd "$OPERATOR_PROJECT_ROOT"
    LAB__Repository__RootPath="$WORKSPACE_ROOT" "${COMMAND[@]}"
) > "$LOG_PATH" 2>&1

cat <<EOF
Seed complete.

Workspace root:
  $WORKSPACE_ROOT

Seed log:
  $LOG_PATH

Databases:
  $WORKSPACE_ROOT/data/primary.db
  $WORKSPACE_ROOT/data/readmodels.db
  $WORKSPACE_ROOT/data/replica-east.db
  $WORKSPACE_ROOT/data/replica-west.db
EOF
