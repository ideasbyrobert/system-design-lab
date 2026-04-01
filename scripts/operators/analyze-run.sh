#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

if [[ $# -lt 1 ]]; then
    echo "Usage: $0 <run-id> [operation] [workspace-root]" >&2
    exit 1
fi

RUN_ID="$1"
OPERATION="${2:-}"
WORKSPACE_ROOT="${3:-$(operator_default_workspace)}"
LOG_PATH="$(operator_log_path "$WORKSPACE_ROOT" "analyze-$RUN_ID")"

operator_ensure_runtime_dirs "$WORKSPACE_ROOT"

COMMAND=(
    dotnet run --project src/Analyze --no-build --
    --run-id "$RUN_ID"
)

if [[ -n "$OPERATION" ]]; then
    COMMAND+=(--operation "$OPERATION")
fi

(
    cd "$OPERATOR_PROJECT_ROOT"
    LAB__Repository__RootPath="$WORKSPACE_ROOT" "${COMMAND[@]}"
) > "$LOG_PATH" 2>&1

SUMMARY_PATH="$WORKSPACE_ROOT/logs/runs/$RUN_ID/summary.json"
REPORT_PATH="$WORKSPACE_ROOT/analysis/$RUN_ID/report.md"

if [[ ! -f "$SUMMARY_PATH" || ! -f "$REPORT_PATH" ]]; then
    echo "Analyze completed, but the expected output files were not materialized. See $LOG_PATH" >&2
    exit 1
fi

cat <<EOF
Analysis complete.

Run id:
  $RUN_ID

Summary:
  $SUMMARY_PATH

Report:
  $REPORT_PATH

Analyze log:
  $LOG_PATH
EOF
