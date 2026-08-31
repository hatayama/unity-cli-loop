#!/bin/sh
TargetPath='{{TARGET_PATH}}'
PathBlockStart="# >>> uloop PATH >>>"
PathBlockEnd="# <<< uloop PATH <<<"

resolve_profile_write_path() {
    profile_path=$1
    if [ ! -L "$profile_path" ]; then
        echo "$profile_path"
        return
    fi

    link_target=$(readlink "$profile_path" 2>/dev/null || true)
    if [ -z "$link_target" ]; then
        echo "$profile_path"
        return
    fi

    case "$link_target" in
        /*)
            echo "$link_target"
            ;;
        *)
            profile_dir=${profile_path%/*}
            if [ "$profile_dir" = "$profile_path" ]; then
                echo "$link_target"
                return
            fi
            echo "$profile_dir/$link_target"
            ;;
    esac
}

remove_path_block() {
    profile_path=$1
    profile_write_path=$(resolve_profile_write_path "$profile_path")
    if [ ! -f "$profile_write_path" ]; then
        return 0
    fi

    case "$profile_write_path" in
        */*)
            profile_dir=${profile_write_path%/*}
            [ -n "$profile_dir" ] || profile_dir=/
            ;;
        *)
            profile_dir=.
            ;;
    esac

    tmp_path=$(mktemp "$profile_dir/.uloop_path.XXXXXX" 2>/dev/null || true)
    if [ -z "$tmp_path" ]; then
        echo "Could not create a temporary file for PATH cleanup." >&2
        return 1
    fi

    awk -v start="$PathBlockStart" -v end="$PathBlockEnd" '
        $0 == start { skipping = 1; changed = 1; next }
        $0 == end { skipping = 0; next }
        !skipping { print }
        END { if (changed) exit 0; exit 2 }
    ' "$profile_write_path" > "$tmp_path"
    status=$?
    if [ "$status" -ne 0 ]; then
        rm -f "$tmp_path"
        if [ "$status" -eq 2 ]; then
            return 0
        fi
        echo "Could not read shell profile for PATH cleanup: $profile_path" >&2
        return 1
    fi

    if mv "$tmp_path" "$profile_write_path"; then
        echo "Removed uloop PATH block from $profile_path."
        return 0
    fi

    echo "Could not update shell profile for PATH cleanup: $profile_path" >&2
    rm -f "$tmp_path"
    return 1
}

cleanup_shell_path_blocks() {
    if [ -z "${HOME:-}" ]; then
        return 0
    fi

    remove_path_block "$HOME/.bash_profile" || return 1
    remove_path_block "$HOME/.bash_login" || return 1
    remove_path_block "$HOME/.profile" || return 1

    if [ -n "${ZDOTDIR:-}" ]; then
        remove_path_block "$ZDOTDIR/.zshrc" || return 1
    fi
    remove_path_block "$HOME/.zshrc" || return 1

    if [ -n "${XDG_CONFIG_HOME:-}" ]; then
        remove_path_block "$XDG_CONFIG_HOME/fish/config.fish" || return 1
    fi
    remove_path_block "$HOME/.config/fish/config.fish" || return 1
}

rm -f "$TargetPath"
cleanup_shell_path_blocks
