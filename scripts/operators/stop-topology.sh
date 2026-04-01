#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

WORKSPACE_ROOT="${1:-$(operator_default_workspace)}"

operator_stop_all "$WORKSPACE_ROOT"

cat <<EOF
Stopped any operator-managed services for:
  $WORKSPACE_ROOT

PID files and stdout logs remain in:
  $(operator_runtime_dir "$WORKSPACE_ROOT")
EOF
