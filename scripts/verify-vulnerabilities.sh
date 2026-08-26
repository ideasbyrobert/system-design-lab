#!/usr/bin/env bash

set -euo pipefail

REPORT_PATH="$(mktemp /tmp/system-design-vulnerabilities.XXXXXX.json)"
trap 'rm -f "$REPORT_PATH"' EXIT

dotnet package list \
    --project ecommerce-systems-lab.sln \
    --vulnerable \
    --include-transitive \
    --format json \
    --no-restore > "$REPORT_PATH"

if grep -q '"vulnerabilities"' "$REPORT_PATH"
then
    cat "$REPORT_PATH"
    exit 1
fi

echo "No known vulnerable dependencies."
