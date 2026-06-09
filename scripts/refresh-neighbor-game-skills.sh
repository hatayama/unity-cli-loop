#!/bin/sh
# Development helper for refreshing generated uloop skill files in sibling Unity projects.
# This is not an installed agent skill or a runtime command. It exists to support local
# uloop development by quitting each target Unity Editor, regenerating Claude/Agents
# skill copies, committing those generated files locally, removing Library after Unity
# has stopped, and relaunching each project.
set -eu

skill_name="refresh-neighbor-game-skills"
expected_project_count=3
dry_run=0
uloop_root="${ULOOP_ROOT:-}"
project_file="$(mktemp "${TMPDIR:-/tmp}/${skill_name}.projects.XXXXXX")"

cleanup() {
    rm -f "$project_file"
}
trap cleanup EXIT INT TERM

usage() {
    cat <<'USAGE'
Usage:
  refresh-neighbor-game-skills.sh [--dry-run] [--uloop-root PATH] [--project PATH ...]

Workflow for each target Unity project:
  0. Quit Unity if running
  1. Install uloop skills for Claude and Agents
  2. Commit generated skill changes without pushing
  3. Remove Library
  4. Launch Unity with launch-unity
USAGE
}

log() {
    printf '%s\n' "$*"
}

fail() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 1
}

run() {
    if [ "$dry_run" -eq 1 ]; then
        printf '[dry-run]'
        for arg in "$@"; do
            printf ' %s' "$arg"
        done
        printf '\n'
        return 0
    fi
    "$@"
}

unity_project_pids() {
    project=$1
    ps -axo pid=,command= -ww |
        awk -v project="$project" '
            /Unity\.app\/Contents\/MacOS\/Unity/ &&
            index($0, "-projectPath " project) > 0 &&
            tolower($0) !~ /assetimportworker/ &&
            tolower($0) !~ /-batchmode/ {
                sub(/^[[:space:]]*/, "", $0)
                split($0, fields, /[[:space:]]+/)
                print fields[1]
            }
        '
}

process_alive() {
    pid=$1
    kill -0 "$pid" 2>/dev/null
}

send_graceful_quit_pid() {
    pid=$1
    if command -v osascript >/dev/null 2>&1; then
        osascript >/dev/null 2>&1 <<EOF || true
tell application "System Events"
  set frontmost of (first process whose unix id is $pid) to true
  keystroke "q" using {command down}
end tell
EOF
        return 0
    fi

    kill "$pid" 2>/dev/null || true
}

force_kill_unity() {
    project=$1
    pids=$(unity_project_pids "$project")
    [ -n "$pids" ] || return 0

    for pid in $pids; do
        if process_alive "$pid"; then
            log "Force killing Unity (PID: $pid)..."
            kill -KILL "$pid" 2>/dev/null || true
        fi
    done
}

wait_for_unity_exit_seconds() {
    project=$1
    timeout_seconds=$2
    elapsed_seconds=0

    if [ "$dry_run" -eq 1 ]; then
        log "[dry-run] wait until Unity exits for $project"
        return 0
    fi

    while [ "$elapsed_seconds" -lt "$timeout_seconds" ]; do
        pids=$(unity_project_pids "$project")
        if [ -z "$pids" ]; then
            return 0
        fi
        sleep 1
        elapsed_seconds=$((elapsed_seconds + 1))
    done

    return 1
}

assert_unity_stopped() {
    project=$1
    if [ "$dry_run" -eq 1 ]; then
        log "[dry-run] assert Unity is stopped for $project"
        return 0
    fi

    pids=$(unity_project_pids "$project")
    if [ -n "$pids" ]; then
        printf '%s\n' "$pids" >&2
        fail "refusing to continue while Unity is still running: $project"
    fi
}

append_project() {
    project_path=$1
    [ -n "$project_path" ] || fail "project path must not be empty"
    [ -d "$project_path" ] || fail "project path does not exist: $project_path"
    project_root=$(cd "$project_path" && pwd -P)

    [ -f "$project_root/ProjectSettings/ProjectVersion.txt" ] || fail "not a Unity project: $project_root"
    [ -f "$project_root/Packages/manifest.json" ] || fail "not a Unity project: $project_root"
    git -C "$project_root" rev-parse --show-toplevel >/dev/null 2>&1 || fail "not a git repository: $project_root"

    printf '%s\n' "$project_root" >>"$project_file"
}

parse_args() {
    while [ "$#" -gt 0 ]; do
        case "$1" in
            --dry-run)
                dry_run=1
                shift
                ;;
            --uloop-root)
                [ "$#" -ge 2 ] || fail "--uloop-root requires a path"
                uloop_root=$2
                shift 2
                ;;
            --project)
                [ "$#" -ge 2 ] || fail "--project requires a path"
                append_project "$2"
                shift 2
                ;;
            -h|--help)
                usage
                exit 0
                ;;
            *)
                fail "unknown argument: $1"
                ;;
        esac
    done
}

has_local_uloop() {
    candidate=$1
    [ -x "$candidate/cli/dist/darwin-arm64/uloop" ]
}

resolve_uloop_root() {
    if [ -n "$uloop_root" ]; then
        [ -d "$uloop_root" ] || fail "--uloop-root does not exist: $uloop_root"
        uloop_root=$(cd "$uloop_root" && pwd -P)
        has_local_uloop "$uloop_root" || fail "local uloop binary not found under --uloop-root"
        return 0
    fi

    current_git_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
    if [ -n "$current_git_root" ] && has_local_uloop "$current_git_root"; then
        uloop_root=$current_git_root
        return 0
    fi

    fail "run from the uloop checkout or pass --uloop-root"
}

discover_projects() {
    if [ -s "$project_file" ]; then
        return 0
    fi

    parent_dir=$(dirname "$uloop_root")
    for candidate in "$parent_dir"/cli-loop-*; do
        [ -d "$candidate" ] || continue
        [ -f "$candidate/ProjectSettings/ProjectVersion.txt" ] || continue
        [ -f "$candidate/Packages/manifest.json" ] || continue
        append_project "$candidate"
    done
}

project_count() {
    wc -l <"$project_file" | tr -d ' '
}

assert_project_count() {
    count=$(project_count)
    [ "$count" -eq "$expected_project_count" ] || fail "expected $expected_project_count sibling Unity projects, found $count"
}

assert_clean_skill_dirs() {
    project=$1
    existing=$(git -C "$project" status --porcelain -- .claude/skills .agents/skills)
    if [ -n "$existing" ]; then
        printf '%s\n' "$existing" >&2
        fail "skill directories already have uncommitted changes: $project"
    fi
}

quit_unity() {
    project=$1
    if [ "$dry_run" -eq 1 ]; then
        log "[dry-run] quit Unity processes for $project"
        wait_for_unity_exit_seconds "$project" 60
        return 0
    fi

    pids=$(unity_project_pids "$project")
    if [ -z "$pids" ]; then
        log "No running Unity process found for this project: $project"
        return 0
    fi

    for pid in $pids; do
        log "Quitting Unity (PID: $pid)..."
        send_graceful_quit_pid "$pid"
    done

    log "Sent graceful quit signal. Waiting up to 20s..."
    if wait_for_unity_exit_seconds "$project" 20; then
        log "Unity quit gracefully."
        return 0
    fi

    log "Unity did not respond to graceful quit. Force killing..."
    force_kill_unity "$project"
    if wait_for_unity_exit_seconds "$project" 10; then
        log "Unity force killed."
        return 0
    fi

    pids=$(unity_project_pids "$project")
    printf '%s\n' "$pids" >&2
    fail "Unity is still running after force kill: $project"
}

install_skills() {
    project=$1
    run "$uloop_bin" --project-path "$project" skills install --claude --agents
}

commit_generated_skills() {
    project=$1
    if [ "$dry_run" -eq 1 ]; then
        run git -C "$project" add -A -- .claude/skills .agents/skills
        run git -C "$project" diff --cached --quiet -- .claude/skills .agents/skills
        log "[dry-run] git -C $project commit -m 'Update generated uloop skills'"
        return 0
    fi

    git -C "$project" add -A -- .claude/skills .agents/skills
    if git -C "$project" diff --cached --quiet -- .claude/skills .agents/skills; then
        log "No generated skill changes to commit: $project"
        return 0
    fi
    git -C "$project" commit -m "Update generated uloop skills"
}

remove_library() {
    project=$1
    [ -n "$project" ] || fail "project path must not be empty before removing Library"
    [ "$project" != "/" ] || fail "refusing to remove /Library"
    [ -f "$project/ProjectSettings/ProjectVersion.txt" ] || fail "refusing to remove Library outside a Unity project"
    assert_unity_stopped "$project"
    run rm -rf -- "$project/Library"
}

launch_project() {
    project=$1
    run launch-unity "$project"
}

parse_args "$@"
resolve_uloop_root
discover_projects
assert_project_count

uloop_bin="$uloop_root/cli/dist/darwin-arm64/uloop"
command -v launch-unity >/dev/null 2>&1 || fail "launch-unity is not available on PATH"

log "Using uloop: $uloop_bin"
log "Target projects:"
while IFS= read -r project; do
    log "  $project"
done <"$project_file"

while IFS= read -r project; do
    assert_clean_skill_dirs "$project"
done <"$project_file"

log "Phase 0/4: quit Unity"
while IFS= read -r project; do
    quit_unity "$project"
done <"$project_file"

while IFS= read -r project; do
    assert_unity_stopped "$project"
done <"$project_file"

log "Phase 1/4: install skills"
while IFS= read -r project; do
    install_skills "$project"
done <"$project_file"

log "Phase 2/4: commit generated skills"
while IFS= read -r project; do
    commit_generated_skills "$project"
done <"$project_file"

log "Phase 3/4: remove Library"
while IFS= read -r project; do
    remove_library "$project"
done <"$project_file"

log "Phase 4/4: launch Unity"
while IFS= read -r project; do
    launch_project "$project"
done <"$project_file"

log "Done."
