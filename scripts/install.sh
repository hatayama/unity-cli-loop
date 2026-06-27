#!/bin/sh
set -eu

REPOSITORY="hatayama/unity-cli-loop"
INSTALL_DIR="${ULOOP_INSTALL_DIR:-$HOME/.local/bin}"
VERSION="${ULOOP_VERSION:-latest}"
LATEST_VERSION="latest"
LATEST_BETA_VERSION="latest-beta"

report_path_shadowing() {
  resolved_uloop=$(command -v uloop 2>/dev/null || true)
  expected_uloop="$INSTALL_DIR/$installed_command_name"

  if [ -z "$resolved_uloop" ] || [ "$resolved_uloop" = "$expected_uloop" ] || [ "$resolved_uloop.exe" = "$expected_uloop" ]; then
    return
  fi

  echo "Installed uloop to $expected_uloop, but PATH resolves uloop to:"
  echo "  $resolved_uloop"
  echo "Move $INSTALL_DIR earlier in PATH, or remove the legacy installation if it owns that command."
}

detect_asset_name() {
  os=$(uname -s)
  arch=$(uname -m)

  case "$os" in
    Darwin) os_name="darwin" ;;
    MINGW*|MSYS*) os_name="windows" ;;
    *)
      echo "Unsupported OS: $os" >&2
      exit 1
      ;;
  esac

  case "$arch" in
    arm64|aarch64) arch_name="arm64" ;;
    x86_64|amd64) arch_name="amd64" ;;
    *)
      echo "Unsupported architecture: $arch" >&2
      exit 1
    ;;
  esac

  if [ "$os_name" = "windows" ]; then
    if [ "$arch_name" != "amd64" ]; then
      echo "Unsupported Windows architecture: $arch" >&2
      exit 1
    fi
    echo "uloop-dispatcher-windows-amd64.zip"
    return
  fi

  echo "uloop-dispatcher-$os_name-$arch_name.tar.gz"
}

detect_installed_command_name() {
  case "$asset_name" in
    *.zip) echo "uloop.exe" ;;
    *) echo "uloop" ;;
  esac
}

infer_npm_prefix_from_uloop_path() {
  command_path=$1

  case "$command_path" in
    */bin/uloop|*/bin/uloop.exe)
      bin_dir=${command_path%/*}
      echo "${bin_dir%/bin}"
      ;;
    *)
      echo ""
      ;;
  esac
}

is_legacy_npm_uloop_path() {
  command_path=$1

  if [ -z "$command_path" ]; then
    return 1
  fi

  if [ -L "$command_path" ]; then
    link_target=$(readlink "$command_path" 2>/dev/null || true)
    case "$link_target" in
      *node_modules/uloop-cli*|*node_modules\\uloop-cli*) return 0 ;;
    esac
  fi

  if [ -f "$command_path" ] && grep -F "node_modules/uloop-cli" "$command_path" >/dev/null 2>&1; then
    return 0
  fi

  return 1
}

print_legacy_npm_manual_removal() {
  legacy_uloop=$1
  legacy_prefix=$2

  echo "Could not remove the legacy npm package automatically."
  if [ -n "$legacy_uloop" ]; then
    echo "Legacy uloop command: $legacy_uloop"
  fi

  if [ -n "$legacy_prefix" ]; then
    echo "Run this manually if that command still shadows the native CLI:"
    echo "  npm uninstall -g --prefix \"$legacy_prefix\" uloop-cli"
    return
  fi

  echo "Run this manually if the old npm command still shadows the native CLI:"
  echo "  npm uninstall -g uloop-cli"
}

quote_for_single_quoted_shell() {
  printf "%s" "$1" | sed "s/'/'\\\\''/g"
}

print_append_command() {
  append_line=$1
  profile_path=$2
  quoted_line=$(quote_for_single_quoted_shell "$append_line")
  quoted_profile=$(quote_for_single_quoted_shell "$profile_path")

  printf "%s\n" "  printf '\n%s\n' '$quoted_line' >> '$quoted_profile'"
}

detect_user_shell_name() {
  shell_path=${SHELL:-}

  if [ -z "$shell_path" ]; then
    echo ""
    return
  fi

  echo "${shell_path##*/}"
}

detect_bash_profile_path() {
  if [ -f "$HOME/.bash_profile" ]; then
    echo "$HOME/.bash_profile"
    return
  fi

  if [ -f "$HOME/.bash_login" ]; then
    echo "$HOME/.bash_login"
    return
  fi

  if [ -f "$HOME/.profile" ]; then
    echo "$HOME/.profile"
    return
  fi

  echo "$HOME/.bash_profile"
}

detect_zsh_profile_path() {
  if [ -n "${ZDOTDIR:-}" ]; then
    echo "$ZDOTDIR/.zshrc"
    return
  fi

  echo "$HOME/.zshrc"
}

detect_fish_profile_path() {
  if [ -n "${XDG_CONFIG_HOME:-}" ]; then
    echo "$XDG_CONFIG_HOME/fish/config.fish"
    return
  fi

  echo "$HOME/.config/fish/config.fish"
}

print_fish_append_command() {
  append_line=$1
  profile_path=$2
  profile_dir=${profile_path%/*}
  quoted_line=$(quote_for_single_quoted_shell "$append_line")
  quoted_profile=$(quote_for_single_quoted_shell "$profile_path")
  quoted_profile_dir=$(quote_for_single_quoted_shell "$profile_dir")

  printf "%s\n" "  mkdir -p '$quoted_profile_dir' && printf '\n%s\n' '$quoted_line' >> '$quoted_profile'"
}

print_path_setup_guidance() {
  shell_name=$(detect_user_shell_name)

  echo "Installed uloop to $INSTALL_DIR, but that directory is not in PATH."
  case "$shell_name" in
    zsh)
      profile_path=$(detect_zsh_profile_path)
      echo "Detected shell: zsh"
      echo "Add this line to $profile_path:"
      echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
      echo "Or run:"
      print_append_command "export PATH=\"$INSTALL_DIR:\$PATH\"" "$profile_path"
      ;;
    bash)
      profile_path=$(detect_bash_profile_path)
      echo "Detected shell: bash"
      echo "Add this line to $profile_path:"
      echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
      echo "Or run:"
      print_append_command "export PATH=\"$INSTALL_DIR:\$PATH\"" "$profile_path"
      ;;
    fish)
      profile_path=$(detect_fish_profile_path)
      echo "Detected shell: fish"
      echo "Add this line to $profile_path:"
      echo "  fish_add_path --move \"$INSTALL_DIR\""
      echo "Or run:"
      print_fish_append_command "fish_add_path --move \"$INSTALL_DIR\"" "$profile_path"
      ;;
    *)
      echo "Add this directory to PATH in your shell profile:"
      echo "  $INSTALL_DIR"
      ;;
  esac

  echo "Open a new terminal after updating the profile."
}

try_remove_legacy_npm_package() {
  legacy_uloop=$1
  expected_uloop=$2

  if [ -n "$expected_uloop" ] && { [ "$legacy_uloop" = "$expected_uloop" ] || [ "$legacy_uloop.exe" = "$expected_uloop" ]; }; then
    return
  fi

  legacy_prefix=""
  if [ -n "$legacy_uloop" ] && is_legacy_npm_uloop_path "$legacy_uloop"; then
    legacy_prefix=$(infer_npm_prefix_from_uloop_path "$legacy_uloop")
  fi

  if [ -z "$legacy_prefix" ]; then
    print_legacy_npm_manual_removal "$legacy_uloop" ""
    return
  fi

  if ! command -v npm >/dev/null 2>&1; then
    print_legacy_npm_manual_removal "$legacy_uloop" "$legacy_prefix"
    return
  fi

  if [ -n "$legacy_prefix" ]; then
    if npm uninstall -g --prefix "$legacy_prefix" uloop-cli; then
      if [ -e "$legacy_uloop" ] || [ -L "$legacy_uloop" ]; then
        print_legacy_npm_manual_removal "$legacy_uloop" "$legacy_prefix"
      else
        echo "Removed legacy npm package: uloop-cli"
      fi
      return
    fi

    print_legacy_npm_manual_removal "$legacy_uloop" "$legacy_prefix"
    return
  fi
}

find_latest_asset_url() {
  release_channel=$1
  page=1

  while :; do
    releases_json=$(curl -fsSL "https://api.github.com/repos/$REPOSITORY/releases?per_page=100&page=$page")
    asset_url=$(printf '%s\n' "$releases_json" | awk -v asset_name="$asset_name" -v release_channel="$release_channel" '
      /"tag_name":/ {
        tag_name = $0
        sub(/^[[:space:]]*"tag_name": "/, "", tag_name)
        sub(/",?[[:space:]]*$/, "", tag_name)
      }
      /"draft":/ {
        draft = ($0 ~ /true/)
      }
      /"prerelease":/ {
        prerelease = ($0 ~ /true/)
      }
      /"browser_download_url":/ {
        if (draft) {
          next
        }
        if (release_channel == "stable" && prerelease) {
          next
        }
        if (release_channel == "beta" && (!prerelease || index(tolower(tag_name), "-beta.") == 0)) {
          next
        }

        line = $0
        sub(/^[[:space:]]*"browser_download_url": "/, "", line)
        sub(/",?[[:space:]]*$/, "", line)
        count = split(line, parts, "/")
        if (parts[count] == asset_name && found == "") {
          found = line
        }
      }
      END {
        if (found != "") {
          print found
        }
      }
    ')

    if [ -n "$asset_url" ]; then
      echo "$asset_url"
      return
    fi

    release_count=$(printf '%s\n' "$releases_json" | awk '/"tag_name":/ { count++ } END { print count + 0 }')
    if [ "$release_count" -lt 100 ]; then
      return
    fi

    page=$((page + 1))
  done
}

set_download_urls() {
  if [ "$VERSION" != "$LATEST_VERSION" ] && [ "$VERSION" != "$LATEST_BETA_VERSION" ]; then
    download_url="https://github.com/$REPOSITORY/releases/download/$VERSION/$asset_name"
    checksum_url="$download_url.sha256"
    return
  fi

  if [ "$VERSION" = "$LATEST_BETA_VERSION" ]; then
    download_url=$(find_latest_asset_url "beta")
  else
    download_url=$(find_latest_asset_url "stable")
  fi
  if [ -z "$download_url" ]; then
    echo "Could not find a $VERSION release asset named $asset_name." >&2
    echo "Set ULOOP_VERSION to a release tag that provides this asset." >&2
    exit 1
  fi
  checksum_url="$download_url.sha256"
}

extract_asset() {
  case "$asset_name" in
    *.zip)
      if ! command -v unzip >/dev/null 2>&1; then
        echo "unzip is required to extract $asset_name" >&2
        exit 1
      fi
      unzip -q "$tmp_dir/$asset_name" -d "$tmp_dir"
      if [ ! -f "$tmp_dir/$installed_command_name" ]; then
        echo "Expected $installed_command_name at archive root after extracting $asset_name." >&2
        exit 1
      fi
      return
      ;;
    *)
      tar -xzf "$tmp_dir/$asset_name" -C "$tmp_dir"
      ;;
  esac
}

test_uloop_native_install_supported() {
  uloop_path=$1

  help_output=$("$uloop_path" install --help 2>/dev/null) || return 1
  case "$(uname -s)" in
    Darwin)
      printf '%s\n' "$help_output" | grep -F "On macOS," >/dev/null
      ;;
    MINGW*|MSYS*)
      printf '%s\n' "$help_output" | grep -F "On Windows," >/dev/null
      ;;
    *)
      return 1
      ;;
  esac
}

invoke_uloop_native_install() {
  uloop_path=$1

  echo "Configuring global uloop launcher..."
  "$uloop_path" install --dir "$INSTALL_DIR"
}

prepend_current_path() {
  case ":${PATH:-}:" in
    *":$INSTALL_DIR:"*) ;;
    *)
      PATH="$INSTALL_DIR:${PATH:-}"
      export PATH
      ;;
  esac
}

cleanup_preinstall_legacy_npm_if_needed() {
  if [ "$legacy_npm_removed_before_install" -ne 0 ] || [ "$legacy_npm_uloop_detected_before_install" -ne 1 ]; then
    return
  fi
  if [ -z "$legacy_uloop_before_install" ]; then
    return
  fi
  if [ ! -e "$legacy_uloop_before_install" ] && [ ! -L "$legacy_uloop_before_install" ]; then
    return
  fi

  try_remove_legacy_npm_package "$legacy_uloop_before_install" "$final_uloop_path"
}

run_compatibility_install_setup() {
  cleanup_preinstall_legacy_npm_if_needed

  case ":${PATH:-}:" in
    *":$INSTALL_DIR:"*) ;;
    *)
      print_path_setup_guidance
      ;;
  esac
}

asset_name=$(detect_asset_name)
installed_command_name=$(detect_installed_command_name)
legacy_uloop_before_install=$(command -v uloop 2>/dev/null || true)
legacy_npm_uloop_detected_before_install=0
if is_legacy_npm_uloop_path "$legacy_uloop_before_install"; then
  legacy_npm_uloop_detected_before_install=1
fi
download_url=""
checksum_url=""
set_download_urls

tmp_dir=$(mktemp -d)
staged_uloop_path=""
trap 'rm -rf "$tmp_dir"; if [ -n "$staged_uloop_path" ]; then rm -f "$staged_uloop_path"; fi' EXIT

verify_checksum() {
  if command -v sha256sum >/dev/null 2>&1; then
    (
      cd "$tmp_dir"
      sha256sum -c "$asset_name.sha256"
    )
    return
  fi

  if command -v shasum >/dev/null 2>&1; then
    expected_hash=$(awk '{print $1}' "$tmp_dir/$asset_name.sha256")
    actual_hash=$(shasum -a 256 "$tmp_dir/$asset_name" | awk '{print $1}')
    if [ "$expected_hash" != "$actual_hash" ]; then
      echo "Checksum mismatch for $asset_name" >&2
      exit 1
    fi
    return
  fi

  echo "sha256sum or shasum is required to verify $asset_name" >&2
  exit 1
}

mkdir -p "$INSTALL_DIR"
curl -fsSL "$download_url" -o "$tmp_dir/$asset_name"
curl -fsSL "$checksum_url" -o "$tmp_dir/$asset_name.sha256"
verify_checksum
extract_asset
staged_uloop_path="$INSTALL_DIR/.uloop-install-$$"
if [ "$installed_command_name" = "uloop.exe" ]; then
  staged_uloop_path="$staged_uloop_path.exe"
fi
install -m 0755 "$tmp_dir/$installed_command_name" "$staged_uloop_path"
"$staged_uloop_path" --version >/dev/null
native_install_supported=0
if test_uloop_native_install_supported "$staged_uloop_path"; then
  native_install_supported=1
fi
final_uloop_path="$INSTALL_DIR/$installed_command_name"
legacy_npm_removed_before_install=0
if [ "$legacy_uloop_before_install" = "$final_uloop_path" ] || [ "$legacy_uloop_before_install.exe" = "$final_uloop_path" ]; then
  if [ "$legacy_npm_uloop_detected_before_install" -eq 1 ]; then
    try_remove_legacy_npm_package "$legacy_uloop_before_install" ""
    legacy_npm_removed_before_install=1
  fi
  legacy_uloop_before_install=""
fi
mv -f "$staged_uloop_path" "$final_uloop_path"
staged_uloop_path=""
if [ "$native_install_supported" -eq 1 ]; then
  if invoke_uloop_native_install "$final_uloop_path"; then
    cleanup_preinstall_legacy_npm_if_needed
    prepend_current_path
  else
    echo "Native install setup failed. The uloop binary was installed, but PATH setup may need manual repair." >&2
    run_compatibility_install_setup
  fi
else
  run_compatibility_install_setup
fi

"$INSTALL_DIR/$installed_command_name" --version
report_path_shadowing
