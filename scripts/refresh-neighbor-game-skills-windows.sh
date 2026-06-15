#!/bin/sh
# Development helper for refreshing generated uloop skill files in sibling Unity projects.
# This is not an installed agent skill or a runtime command. It exists to support local
# uloop development by resetting each target Git repository, quitting each target
# Unity Editor, regenerating Claude/Agents skill copies, committing those generated
# files locally, removing Library after Unity has stopped, relaunching each project,
# and opening the sample scene.
set -eu

skill_name="refresh-neighbor-game-skills"
expected_project_count=3
default_project_names="cli-loop-block-kuzushi cli-loop-minecraft cli-loop-tetris"
sample_scene_path="Assets/Scenes/SampleScene.unity"
dry_run=0
uloop_root="${ULOOP_ROOT:-}"
project_file="$(mktemp "${TMPDIR:-/tmp}/${skill_name}.projects.XXXXXX")"

host_os=unsupported
case "$(uname -s 2>/dev/null || printf unknown)" in
    Darwin)
        host_os=darwin
        ;;
    MINGW*|MSYS*|CYGWIN*)
        host_os=windows
        ;;
    Linux)
        if [ -n "${WSL_DISTRO_NAME:-}" ] || grep -qi microsoft /proc/version 2>/dev/null; then
            host_os=wsl
        else
            host_os=linux
        fi
        ;;
esac

case "$host_os" in
    windows|wsl)
        ;;
    *)
        printf 'ERROR: refresh-neighbor-game-skills-windows.sh is only supported on Windows Git Bash and WSL.\n' >&2
        exit 1
        ;;
esac

cleanup() {
    rm -f "$project_file"
}

on_signal() {
    cleanup
    exit "$1"
}

trap cleanup EXIT
trap 'on_signal 130' INT
trap 'on_signal 143' TERM

usage() {
    cat <<'USAGE'
Usage:
  refresh-neighbor-game-skills.sh [--dry-run] [--uloop-root PATH] [--project PATH ...]

Workflow for each target Unity project:
  0. Reset Git state to HEAD and remove untracked files
  1. Quit Unity if running
  2. Install uloop skills for Claude and Agents
  3. Commit generated skill changes without pushing
  4. Remove Library
  5. Launch Unity with the local uloop launch command
  6. Open Assets/Scenes/SampleScene.unity
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
            BEGIN {
                project_re = project
                gsub(/[][(){}.^$*+?|\\]/, "\\\\&", project_re)
                project_re = "-projectPath[[:space:]]+" project_re "/*([[:space:]]|$)"
            }
            /Unity\.app\/Contents\/MacOS\/Unity/ &&
            $0 ~ project_re &&
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

    case "$host_os" in
        windows|wsl)
            run_uloop_project "$project" launch --quit >/dev/null
            return 0
            ;;
    esac

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
    if grep -Fqx -- "$project_root" "$project_file"; then
        fail "duplicate project path: $project_root"
    fi

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

uloop_binary_for_root() {
    candidate=$1
    case "$host_os" in
        darwin)
            arch=arm64
            if [ "$(uname -m)" = "x86_64" ] || [ "$(uname -m)" = "amd64" ]; then
                arch=amd64
            fi
            printf '%s\n' "$candidate/cli/dist/darwin-$arch/uloop"
            ;;
        windows|wsl)
            printf '%s\n' "$candidate/cli/dist/windows-amd64/uloop.exe"
            ;;
        *)
            fail "unsupported host OS for local uloop binary: $host_os"
            ;;
    esac
}

has_local_uloop() {
    candidate=$1
    candidate_uloop_bin=$(uloop_binary_for_root "$candidate")
    case "$host_os" in
        windows|wsl)
            [ -f "$candidate_uloop_bin" ]
            ;;
        *)
            [ -x "$candidate_uloop_bin" ]
            ;;
    esac
}

to_uloop_project_path() {
    project=$1
    case "$host_os" in
        wsl)
            command -v wslpath >/dev/null 2>&1 || fail "wslpath is required on WSL"
            wslpath -w "$project"
            ;;
        windows)
            if command -v cygpath >/dev/null 2>&1; then
                cygpath -w "$project"
            else
                printf '%s\n' "$project"
            fi
            ;;
        *)
            printf '%s\n' "$project"
            ;;
    esac
}

run_uloop_project() {
    project=$1
    shift
    native_project=$(to_uloop_project_path "$project")

    if [ "$dry_run" -eq 1 ]; then
        printf '[dry-run] MSYS_NO_PATHCONV=1 %s --project-path %s' "$uloop_bin" "$native_project"
        for arg in "$@"; do
            printf ' %s' "$arg"
        done
        printf '\n'
        return 0
    fi

    MSYS_NO_PATHCONV=1 "$uloop_bin" --project-path "$native_project" "$@" </dev/null
}

invoke_uloop_project() {
    project=$1
    shift
    native_project=$(to_uloop_project_path "$project")
    MSYS_NO_PATHCONV=1 "$uloop_bin" --project-path "$native_project" "$@" </dev/null
}

run_for_projects_parallel() {
    worker=$1
    pids=''

    while IFS= read -r project; do
        (
            log "[$(basename "$project")] start: $worker"
            "$worker" "$project"
            log "[$(basename "$project")] done: $worker"
        ) &
        pids="$pids $!"
    done <"$project_file"

    failed=0
    for pid in $pids; do
        if ! wait "$pid"; then
            failed=1
        fi
    done

    [ "$failed" -eq 0 ] || fail "parallel phase failed: $worker"
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
    for project_name in $default_project_names; do
        candidate="$parent_dir/$project_name"
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

reset_git_state() {
    project=$1
    log "Resetting Git state: $project"
    run git -C "$project" reset --hard HEAD
    run git -C "$project" clean -fd

    if [ "$dry_run" -eq 1 ]; then
        log "[dry-run] assert Git state is clean for $project"
        return 0
    fi

    dirty=$(git -C "$project" status --porcelain)
    if [ -n "$dirty" ]; then
        printf '%s\n' "$dirty" >&2
        fail "git state is not clean after reset: $project"
    fi
}

assert_clean_skill_dirs() {
    project=$1
    if [ "$dry_run" -eq 1 ]; then
        log "[dry-run] assert generated skill directories are clean for $project"
        return 0
    fi

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

    case "$host_os" in
        windows|wsl)
            log "Quitting Unity with local uloop: $project"
            run_uloop_project "$project" launch --quit
            return 0
            ;;
    esac

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
    run_uloop_project "$project" skills install --claude --agents
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
    commit_generated_skill_changes "$project"
}

commit_generated_skill_changes() {
    project=$1
    commit_name=${GIT_AUTHOR_NAME:-}
    commit_email=${GIT_AUTHOR_EMAIL:-}

    if [ -z "$commit_name" ]; then
        commit_name=$(git -C "$project" config user.name 2>/dev/null || true)
    fi
    if [ -z "$commit_email" ]; then
        commit_email=$(git -C "$project" config user.email 2>/dev/null || true)
    fi
    if [ -z "$commit_name" ]; then
        commit_name=$(git -C "$uloop_root" config user.name 2>/dev/null || true)
    fi
    if [ -z "$commit_email" ]; then
        commit_email=$(git -C "$uloop_root" config user.email 2>/dev/null || true)
    fi
    if [ -z "$commit_name" ]; then
        commit_name="uLoop Automation"
    fi
    if [ -z "$commit_email" ]; then
        commit_email="uloop@example.invalid"
    fi

    GIT_AUTHOR_NAME="$commit_name" \
    GIT_AUTHOR_EMAIL="$commit_email" \
    GIT_COMMITTER_NAME="$commit_name" \
    GIT_COMMITTER_EMAIL="$commit_email" \
        git -C "$project" \
        -c user.name="$commit_name" \
        -c user.email="$commit_email" \
        commit -m "Update generated uloop skills"
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
    attempt=1
    max_attempts=3

    while [ "$attempt" -le "$max_attempts" ]; do
        if run_uloop_project "$project" launch; then
            return 0
        fi

        if [ "$attempt" -eq "$max_attempts" ]; then
            return 1
        fi

        log "[$(basename "$project")] Unity is not ready after launch attempt $attempt; retrying in 30s..."
        sleep 30
        attempt=$((attempt + 1))
    done
}

wait_for_unity_ready() {
    project=$1
    timeout_seconds=$2
    elapsed_seconds=0

    if [ "$dry_run" -eq 1 ]; then
        log "[dry-run] wait until Unity responds to uloop for $project"
        return 0
    fi

    while [ "$elapsed_seconds" -lt "$timeout_seconds" ]; do
        if invoke_uloop_project "$project" get-logs --max-count 1 >/dev/null 2>&1; then
            return 0
        fi

        sleep 2
        elapsed_seconds=$((elapsed_seconds + 2))
    done

    fail "Unity did not become ready after launch: $project"
}

open_sample_scene() {
    project=$1
    [ -f "$project/$sample_scene_path" ] || fail "sample scene not found: $project/$sample_scene_path"

    wait_for_unity_ready "$project" 300
    code="
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

string scenePath = \"$sample_scene_path\";
if (SceneManager.GetActiveScene().path != scenePath)
{
    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
}

return SceneManager.GetActiveScene().path;
"
    if [ "$dry_run" -eq 1 ]; then
        run_uloop_project "$project" execute-dynamic-code --code "$code"
        return 0
    fi

    response=''
    if ! response=$(invoke_uloop_project "$project" execute-dynamic-code --code "$code"); then
        [ -z "$response" ] || printf '%s\n' "$response" >&2
        fail "failed to execute sample scene open command: $project"
    fi

    if ! response_matches_scene "$response"; then
        printf '%s\n' "$response" >&2
        fail "failed to open sample scene: $project"
    fi
}

response_matches_scene() {
    response=$1
    if command -v jq >/dev/null 2>&1; then
        printf '%s\n' "$response" | jq -e --arg scene_path "$sample_scene_path" '.Success == true and .Result == $scene_path' >/dev/null
        return $?
    fi

    if command -v powershell.exe >/dev/null 2>&1; then
        scene_path_ps=$(printf '%s\n' "$sample_scene_path" | sed "s/'/''/g")
        printf '%s\n' "$response" | powershell.exe -NoProfile -Command "
\$ScenePath = '$scene_path_ps'
\$Json = [Console]::In.ReadToEnd()
try {
    \$Value = \$Json | ConvertFrom-Json
} catch {
    exit 1
}
if (\$Value.Success -eq \$true -and \$Value.Result -eq \$ScenePath) {
    exit 0
}
exit 1
" >/dev/null
        return $?
    fi

    fail "jq is not available on PATH and powershell.exe is not available for JSON validation"
}

parse_args "$@"
resolve_uloop_root
discover_projects
assert_project_count

uloop_bin=$(uloop_binary_for_root "$uloop_root")
[ -f "$uloop_bin" ] || fail "local uloop binary not found: $uloop_bin"

log "Using uloop: $uloop_bin"
log "Host OS: $host_os"
log "Target projects:"
while IFS= read -r project; do
    log "  $project"
done <"$project_file"

log "Phase 0/6: reset Git state"
run_for_projects_parallel reset_git_state

run_for_projects_parallel assert_clean_skill_dirs

log "Phase 1/6: quit Unity"
run_for_projects_parallel quit_unity

run_for_projects_parallel assert_unity_stopped

log "Phase 2/6: install skills"
run_for_projects_parallel install_skills

log "Phase 3/6: commit generated skills"
run_for_projects_parallel commit_generated_skills

log "Phase 4/6: remove Library"
run_for_projects_parallel remove_library

log "Phase 5/6: launch Unity"
run_for_projects_parallel launch_project

log "Phase 6/6: open sample scene"
run_for_projects_parallel open_sample_scene

log "Done."
