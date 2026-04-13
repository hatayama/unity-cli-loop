#!/bin/sh
# Stress test: repeatedly run uloop compile and uloop get-logs in sequence.
# Designed for long-running Unity server restart/recovery verification.

set -eu

PROJECT_PATH="${ULOOP_PROJECT_PATH:-}"
INTERVAL_SECONDS="${ULOOP_STRESS_INTERVAL_SECONDS:-2}"
LOG_DIR="${ULOOP_STRESS_LOG_DIR:-.uloop/stress-tests}"
MAX_ROUNDS="${ULOOP_STRESS_MAX_ROUNDS:-0}"
WAIT_FOR_READY_SECONDS="${ULOOP_STRESS_WAIT_FOR_READY_SECONDS:-15}"
CURRENT_CHILD_PID=''
CURRENT_TIMEOUT_PID=''
mkdir -p "$LOG_DIR"

timestamp() {
    date +"%Y-%m-%dT%H:%M:%S%z"
}

uloop_cmd() {
    if [ -n "$PROJECT_PATH" ]; then
        uloop "$@" --project-path "$PROJECT_PATH"
    else
        uloop "$@"
    fi
}

wait_for_ready() {
    round="$1"
    deadline=$(( $(date +%s) + WAIT_FOR_READY_SECONDS ))
    while :; do
        remaining_seconds=$(( deadline - $(date +%s) ))
        if [ "$remaining_seconds" -le 0 ]; then
            printf '%s [%s] ready timeout after %ss\n' "$(timestamp)" "$round" "$WAIT_FOR_READY_SECONDS"
            return 1
        fi

        if run_quiet_with_timeout "$remaining_seconds" uloop_cmd get-logs --max-count 1; then
            printf '%s [%s] ready\n' "$(timestamp)" "$round"
            return 0
        fi

        now="$(date +%s)"
        if [ "$now" -ge "$deadline" ]; then
            printf '%s [%s] ready timeout after %ss\n' "$(timestamp)" "$round" "$WAIT_FOR_READY_SECONDS"
            return 1
        fi

        sleep_interruptible 2
    done
}

cleanup() {
    if [ -n "$CURRENT_TIMEOUT_PID" ]; then
        kill -TERM "$CURRENT_TIMEOUT_PID" 2>/dev/null || :
        wait "$CURRENT_TIMEOUT_PID" 2>/dev/null || :
        CURRENT_TIMEOUT_PID=''
    fi

    if [ -n "$CURRENT_CHILD_PID" ]; then
        kill -TERM "$CURRENT_CHILD_PID" 2>/dev/null || :
        sleep 1
        kill -KILL "$CURRENT_CHILD_PID" 2>/dev/null || :
        wait "$CURRENT_CHILD_PID" 2>/dev/null || :
        CURRENT_CHILD_PID=''
    fi

    printf '%s cleanup\n' "$(timestamp)"
}

run_quiet() {
    "$@" >/dev/null 2>&1 &
    CURRENT_CHILD_PID="$!"
    wait "$CURRENT_CHILD_PID"
    status="$?"
    CURRENT_CHILD_PID=''
    return "$status"
}

run_quiet_with_timeout() {
    timeout_seconds="$1"
    shift

    "$@" >/dev/null 2>&1 &
    CURRENT_CHILD_PID="$!"

    timeout_marker="${LOG_DIR}/.timeout-${CURRENT_CHILD_PID}"
    rm -f "$timeout_marker"

    (
        sleep "$timeout_seconds"
        if kill -0 "$CURRENT_CHILD_PID" 2>/dev/null; then
            : > "$timeout_marker"
            kill -TERM "$CURRENT_CHILD_PID" 2>/dev/null || :
            sleep 1
            kill -KILL "$CURRENT_CHILD_PID" 2>/dev/null || :
        fi
    ) &
    CURRENT_TIMEOUT_PID="$!"

    if wait "$CURRENT_CHILD_PID"; then
        status=0
    else
        status="$?"
    fi

    kill -TERM "$CURRENT_TIMEOUT_PID" 2>/dev/null || :
    wait "$CURRENT_TIMEOUT_PID" 2>/dev/null || :
    CURRENT_TIMEOUT_PID=''
    CURRENT_CHILD_PID=''

    if [ -e "$timeout_marker" ]; then
        rm -f "$timeout_marker"
        return 124
    fi

    rm -f "$timeout_marker"
    return "$status"
}

run_with_logs() {
    stdout_path="$1"
    stderr_path="$2"
    shift 2

    "$@" >"$stdout_path" 2>"$stderr_path" &
    CURRENT_CHILD_PID="$!"
    wait "$CURRENT_CHILD_PID"
    status="$?"
    CURRENT_CHILD_PID=''
    return "$status"
}

sleep_interruptible() {
    duration_seconds="$1"

    sleep "$duration_seconds" &
    CURRENT_CHILD_PID="$!"
    wait "$CURRENT_CHILD_PID"
    status="$?"
    CURRENT_CHILD_PID=''
    return "$status"
}

handle_interrupt() {
    cleanup
    exit 130
}

trap handle_interrupt INT TERM

echo "=== uloop compile/get-logs stress test ==="
echo "log_dir=$LOG_DIR"
echo "interval_seconds=$INTERVAL_SECONDS"
echo "max_rounds=$MAX_ROUNDS"
echo "wait_for_ready_seconds=$WAIT_FOR_READY_SECONDS"

if ! wait_for_ready "bootstrap"; then
    echo "bootstrap failed"
    exit 1
fi

round=1
while :; do
    if [ "$MAX_ROUNDS" -gt 0 ] && [ "$round" -gt "$MAX_ROUNDS" ]; then
        echo "reached max rounds: $MAX_ROUNDS"
        exit 0
    fi

    if ! run_with_logs "$LOG_DIR/${round}_compile.out" "$LOG_DIR/${round}_compile.err" \
        uloop_cmd compile --wait-for-domain-reload true; then
        echo "compile failed at round $round"
        exit 1
    fi

    if ! wait_for_ready "$round"; then
        echo "server did not become ready after compile at round $round"
        exit 1
    fi

    if ! run_with_logs "$LOG_DIR/${round}_get-logs.out" "$LOG_DIR/${round}_get-logs.err" \
        uloop_cmd get-logs --max-count 1; then
        echo "get-logs failed at round $round"
        exit 1
    fi

    echo "$(timestamp) [$round] round complete"
    round=$((round + 1))
    sleep_interruptible "$INTERVAL_SECONDS"
done
