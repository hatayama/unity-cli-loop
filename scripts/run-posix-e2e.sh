#!/bin/sh
# Runs the terminal-driven POSIX E2E coverage through one entrypoint.

set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
PROJECT_PATH="$ROOT_DIR"
ULOOP_PATH="${ULOOP_BIN:-}"
TIMEOUT_SECONDS=120
LAUNCH_TIMEOUT_SECONDS=240
SKIP_RECOVERY_READINESS=false
SKIP_SIMULATE_MOUSE=false

fail() {
    printf 'ERROR: %s\n' "$1" >&2
    exit 1
}

usage() {
    cat <<EOF
Usage: sh scripts/run-posix-e2e.sh [options]

Options:
  --project-path <path>              Unity project to test. Defaults to this repository.
  --uloop-path <path>                uloop binary to execute. Defaults to the built native binary.
  --timeout <seconds>                Per-command smoke timeout. Default: 120.
  --launch-timeout <seconds>         Launch/reuse smoke timeout. Default: 240.
  --skip-recovery-readiness          Skip recovery/readiness smoke.
  --skip-simulate-mouse              Skip simulate-mouse UI E2E.
  -h, --help                         Show this help.
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --project-path)
            [ "$#" -ge 2 ] || fail "--project-path requires a value"
            PROJECT_PATH=$2
            shift 2
            ;;
        --uloop-path)
            [ "$#" -ge 2 ] || fail "--uloop-path requires a value"
            ULOOP_PATH=$2
            shift 2
            ;;
        --timeout)
            [ "$#" -ge 2 ] || fail "--timeout requires a value"
            TIMEOUT_SECONDS=$2
            shift 2
            ;;
        --launch-timeout)
            [ "$#" -ge 2 ] || fail "--launch-timeout requires a value"
            LAUNCH_TIMEOUT_SECONDS=$2
            shift 2
            ;;
        --skip-recovery-readiness)
            SKIP_RECOVERY_READINESS=true
            shift
            ;;
        --skip-simulate-mouse)
            SKIP_SIMULATE_MOUSE=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            fail "unknown option: $1"
            ;;
    esac
done

resolve_path() {
    path=$1
    if [ -d "$path" ]; then
        (cd "$path" && pwd)
        return
    fi
    parent=$(dirname -- "$path")
    leaf=$(basename -- "$path")
    (cd "$parent" && printf '%s/%s\n' "$(pwd)" "$leaf")
}

default_uloop_path() {
    case "$(uname -s)" in
        Darwin)
            machine=$(uname -m)
            if [ "$machine" = "arm64" ] || [ "$machine" = "aarch64" ]; then
                printf '%s\n' "$ROOT_DIR/dist/darwin-arm64/uloop"
            else
                printf '%s\n' "$ROOT_DIR/dist/darwin-amd64/uloop"
            fi
            ;;
        MINGW*|MSYS*|CYGWIN*)
            printf '%s\n' "$ROOT_DIR/dist/windows-amd64/uloop.exe"
            ;;
        *)
            printf '%s\n' ""
            ;;
    esac
}

run_step() {
    name=$1
    shift
    printf '\n=== %s ===\n' "$name"
    "$@"
}

PROJECT_PATH=$(resolve_path "$PROJECT_PATH")
if [ -z "$ULOOP_PATH" ]; then
    ULOOP_PATH=$(default_uloop_path)
fi
[ -n "$ULOOP_PATH" ] || fail "no checked-in uloop binary for this platform; pass --uloop-path"
ULOOP_PATH=$(resolve_path "$ULOOP_PATH")
[ -x "$ULOOP_PATH" ] || fail "uloop binary is not executable: $ULOOP_PATH"
[ -d "$PROJECT_PATH/Assets" ] || fail "--project-path does not contain Assets: $PROJECT_PATH"
[ -d "$PROJECT_PATH/ProjectSettings" ] || fail "--project-path does not contain ProjectSettings: $PROJECT_PATH"

export ULOOP_BIN="$ULOOP_PATH"
PATH="$(dirname -- "$ULOOP_PATH"):$PATH"
export PATH

printf '=== POSIX terminal-driven E2E ===\n'
printf 'project_path=%s\n' "$PROJECT_PATH"
printf 'uloop_path=%s\n' "$ULOOP_PATH"

if [ "$SKIP_RECOVERY_READINESS" = false ]; then
    run_step "CLI recovery/readiness" \
        go run "$ROOT_DIR/scripts/smoke-cli-recovery-readiness.go" \
            --project-path "$PROJECT_PATH" \
            --uloop-path "$ULOOP_PATH" \
            --timeout "$TIMEOUT_SECONDS" \
            --launch-timeout "$LAUNCH_TIMEOUT_SECONDS"
fi

if [ "$SKIP_SIMULATE_MOUSE" = false ]; then
    run_step "Simulate mouse UI" \
        sh "$ROOT_DIR/scripts/test-simulate-mouse-demo.sh" \
            --project-path "$PROJECT_PATH"
fi

printf '\nAll POSIX terminal-driven E2E checks passed.\n'
