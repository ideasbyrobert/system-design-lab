#!/usr/bin/env bash

operator_project_root() {
    cd "$(dirname "${BASH_SOURCE[0]}")/../.." >/dev/null 2>&1 && pwd
}

OPERATOR_PROJECT_ROOT="$(operator_project_root)"

operator_default_workspace() {
    printf '%s\n' "${LAB_OPERATOR_ROOT:-/tmp/ecommerce-systems-lab-operator}"
}

operator_runtime_dir() {
    printf '%s\n' "$1/.operator"
}

operator_pid_path() {
    printf '%s\n' "$(operator_runtime_dir "$1")/$2.pid"
}

operator_log_path() {
    printf '%s\n' "$(operator_runtime_dir "$1")/$2.stdout.log"
}

operator_build_log_path() {
    printf '%s\n' "$(operator_runtime_dir "$1")/build.log"
}

operator_manifest_path() {
    printf '%s\n' "$(operator_runtime_dir "$1")/$2.manifest"
}

operator_ensure_runtime_dirs() {
    mkdir -p "$1" "$(operator_runtime_dir "$1")"
}

operator_wait_for_http() {
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

        sleep 0.25

        if [[ "$attempt" -eq 80 ]]; then
            echo "Timed out waiting for $service_name to become healthy. See $log_path" >&2
            exit 1
        fi
    done
}

operator_stop_pid_file() {
    local pid_path="$1"

    if [[ ! -f "$pid_path" ]]; then
        return 0
    fi

    local pid
    pid="$(cat "$pid_path")"

    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
        kill "$pid" 2>/dev/null || true
        wait "$pid" 2>/dev/null || true
    fi

    rm -f "$pid_path"
}

operator_stop_all() {
    local workspace="$1"
    local runtime_dir
    runtime_dir="$(operator_runtime_dir "$workspace")"

    if [[ ! -d "$runtime_dir" ]]; then
        return 0
    fi

    local pid_path
    for pid_path in "$runtime_dir"/*.pid; do
        if [[ -f "$pid_path" ]]; then
            operator_stop_pid_file "$pid_path"
        fi
    done
}

operator_build() {
    local workspace="$1"

    operator_ensure_runtime_dirs "$workspace"

    (
        cd "$OPERATOR_PROJECT_ROOT"
        dotnet build "$OPERATOR_PROJECT_ROOT/ecommerce-systems-lab.sln"
    ) > "$(operator_build_log_path "$workspace")" 2>&1
}

operator_start_http_service() {
    local workspace="$1"
    local name="$2"
    local project_path="$3"
    local url="$4"
    local health_path="$5"
    shift 5

    operator_ensure_runtime_dirs "$workspace"

    local pid_path
    local log_path
    pid_path="$(operator_pid_path "$workspace" "$name")"
    log_path="$(operator_log_path "$workspace" "$name")"

    operator_stop_pid_file "$pid_path"

    (
        cd "$OPERATOR_PROJECT_ROOT"
        env LAB__Repository__RootPath="$workspace" "$@" \
            dotnet run --project "$project_path" --no-build --urls "$url"
    ) > "$log_path" 2>&1 &

    local process_id="$!"
    printf '%s\n' "$process_id" > "$pid_path"
    operator_wait_for_http "$url$health_path" "$process_id" "$log_path" "$name"
}

operator_start_background_service() {
    local workspace="$1"
    local name="$2"
    local project_path="$3"
    shift 3

    operator_ensure_runtime_dirs "$workspace"

    local pid_path
    local log_path
    pid_path="$(operator_pid_path "$workspace" "$name")"
    log_path="$(operator_log_path "$workspace" "$name")"

    operator_stop_pid_file "$pid_path"

    (
        cd "$OPERATOR_PROJECT_ROOT"
        env LAB__Repository__RootPath="$workspace" "$@" \
            dotnet run --project "$project_path" --no-build
    ) > "$log_path" 2>&1 &

    local process_id="$!"
    printf '%s\n' "$process_id" > "$pid_path"
    sleep 2

    if ! kill -0 "$process_id" 2>/dev/null; then
        echo "$name exited during startup. See $log_path" >&2
        exit 1
    fi
}
