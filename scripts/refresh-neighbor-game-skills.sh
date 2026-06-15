#!/bin/sh
# Dispatches to the platform-specific refresh-neighbor-game-skills implementation.
set -eu

root_dir=$(CDPATH= cd "$(dirname "$0")/.." && pwd)

case "$(uname -s 2>/dev/null || printf unknown)" in
    Darwin)
        exec "$root_dir/scripts/refresh-neighbor-game-skills-macos.sh" "$@"
        ;;
    MINGW*|MSYS*|CYGWIN*)
        exec "$root_dir/scripts/refresh-neighbor-game-skills-windows.sh" "$@"
        ;;
    Linux)
        if [ -n "${WSL_DISTRO_NAME:-}" ] || grep -qi microsoft /proc/version 2>/dev/null; then
            exec "$root_dir/scripts/refresh-neighbor-game-skills-windows.sh" "$@"
        fi
        ;;
esac

printf 'ERROR: refresh-neighbor-game-skills is only supported on macOS, Windows Git Bash, and WSL.\n' >&2
exit 1
