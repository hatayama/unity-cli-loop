#!/bin/sh
set -eu

REPOSITORY="hatayama/unity-cli-loop"
INSTALL_DIR="${ULOOP_INSTALL_DIR:-$HOME/.local/bin}"
VERSION="${ULOOP_VERSION:-latest}"
LATEST_VERSION="latest"
LATEST_BETA_VERSION="latest-beta"
DEFAULT_PIN_REF="main"
PIN_REF="${ULOOP_REF:-$DEFAULT_PIN_REF}"

# Why: install.sh interpolates $VERSION into
# "https://github.com/$REPOSITORY/releases/download/$VERSION/..." without
# curl normalizing dot segments, so a value like
# "../../evil/repo/releases/download/v1" would break out of the intended
# release path even though the ambient attacker who can already set env
# usually has bigger wins. Fail-close early on any value that is neither
# the two well-known channel selectors nor a semantic-version-shaped tag,
# so downstream users of $VERSION (asset URL, ULOOP_VERSION passthrough,
# release tag prefixing) never see a value they weren't designed for.
validate_uloop_version() {
  candidate=$1
  if [ "$candidate" = "$LATEST_VERSION" ] || [ "$candidate" = "$LATEST_BETA_VERSION" ]; then
    return
  fi
  # Why: `grep -Eq` matches per line, so a value like
  # printf '../evil\n3.0.0' would pass the ERE below on its second line
  # while leaking the embedded newline into the URL builder downstream.
  # Reject anything outside the semver tag alphabet at the whole-string
  # level first — `case` sees the value as one string and cannot be
  # smuggled past with embedded newlines or NULs.
  case $candidate in
    ''|*[!0-9A-Za-z.+-]*)
      echo "Invalid ULOOP_VERSION: $candidate" >&2
      echo "Expected 'latest', 'latest-beta', or a semver tag such as '3.0.0-beta.5' / 'dispatcher-v3.0.0-beta.5'." >&2
      exit 1
      ;;
  esac
  # POSIX ERE: optional dispatcher-v / uloop-project-runner-v / v prefix,
  # then MAJOR.MINOR.PATCH, then optional -prerelease.identifiers or
  # +build.metadata.
  if printf '%s' "$candidate" | grep -Eq '^(dispatcher-v|uloop-project-runner-v|v)?[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$'; then
    return
  fi
  echo "Invalid ULOOP_VERSION: $candidate" >&2
  echo "Expected 'latest', 'latest-beta', or a semver tag such as '3.0.0-beta.5' / 'dispatcher-v3.0.0-beta.5'." >&2
  exit 1
}

validate_uloop_version "$VERSION"

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
    return
  fi

  if ! command -v npm >/dev/null 2>&1; then
    return
  fi

  if [ -n "$legacy_prefix" ]; then
    if npm uninstall -g --prefix "$legacy_prefix" uloop-cli; then
      if [ -e "$legacy_uloop" ] || [ -L "$legacy_uloop" ]; then
        return
      else
        echo "Removed legacy npm package: uloop-cli"
      fi
      return
    fi

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

# Why: ULOOP_REF is interpolated into a raw.githubusercontent.com URL. Reject
# empty values, path traversal (`..`), and any character outside the ref
# alphabet so a hostile env cannot break out of the intended pin path.
validate_uloop_ref() {
  candidate=$1
  case $candidate in
    ''|*..*|*[!0-9A-Za-z._/-]*)
      echo "Invalid ULOOP_REF: $candidate" >&2
      echo "Expected a git ref such as 'main' or 'v3-beta'." >&2
      exit 1
      ;;
  esac
}

fetch_pin_json() {
  curl -fsSL "https://raw.githubusercontent.com/$REPOSITORY/$PIN_REF/Packages/src/project-runner-pin.json"
}

# Why: the pin is pretty-printed with one key per physical line and the
# manifest value keeps JSON `\n` escapes. Extract the raw (still-escaped)
# string so unescape_pin_manifest can fail closed on unexpected escapes.
extract_pin_string_field() {
  printf '%s\n' "$pin_json" | awk -v key="$1" '
    {
      prefix = "\"" key "\": \""
      i = index($0, prefix)
      if (i == 0) next
      value = substr($0, i + length(prefix))
      if (value !~ /",?[[:space:]]*$/) exit 1
      sub(/",?[[:space:]]*$/, "", value)
      print value
      found = 1
      exit
    }
    END { if (!found) exit 1 }
  '
}

# Why: only `\n` is a legitimate escape in dispatcherArchiveManifest. Any
# other backslash residue means the pin shape drifted or was tampered with.
unescape_pin_manifest() {
  printf '%s\n' "$1" | awk '
    {
      residue = $0
      gsub(/\\n/, "", residue)
      if (residue ~ /\\/) exit 1
      line = $0
      gsub(/\\n/, "\n", line)
      print line
    }
  '
}

# Why: keep the installer symmetric with CliPinReaderService — 64 hex digits
# (A-F accepted), exactly two spaces, no whitespace in the filename, no CR,
# no duplicates, and at least one line.
validate_pin_manifest() {
  printf '%s\n' "$1" | awk '
    BEGIN { count = 0 }
    {
      if ($0 ~ /\r/) exit 1
      if ($0 == "") next
      if ($0 !~ /^[0-9a-fA-F]{64}  [^ \t]+$/) exit 1
      name = $2
      if (seen[name]++) exit 1
      count++
    }
    END { if (count == 0) exit 1 }
  '
}

resolve_manifest_from_pin_if_needed() {
  if [ -n "${ULOOP_ARCHIVE_MANIFEST:-}" ]; then
    return 0
  fi

  validate_uloop_ref "$PIN_REF"

  pin_json=$(fetch_pin_json) || {
    echo "Could not fetch project-runner pin from https://raw.githubusercontent.com/$REPOSITORY/$PIN_REF/Packages/src/project-runner-pin.json" >&2
    echo "Set ULOOP_ARCHIVE_MANIFEST from a verified attestation (see README), or fix ULOOP_REF." >&2
    exit 1
  }

  pin_tag=$(extract_pin_string_field dispatcherReleaseTag) || {
    echo "project-runner pin is missing dispatcherReleaseTag" >&2
    exit 1
  }
  raw=$(extract_pin_string_field dispatcherArchiveManifest) || {
    echo "project-runner pin is missing dispatcherArchiveManifest" >&2
    exit 1
  }
  pin_manifest=$(unescape_pin_manifest "$raw") || {
    echo "project-runner pin dispatcherArchiveManifest has an unexpected escape sequence" >&2
    exit 1
  }
  validate_pin_manifest "$pin_manifest" || {
    echo "project-runner pin dispatcherArchiveManifest failed format validation" >&2
    exit 1
  }
  validate_uloop_version "$pin_tag"

  if [ "$VERSION" = "$LATEST_VERSION" ] || [ "$VERSION" = "$LATEST_BETA_VERSION" ]; then
    VERSION=$pin_tag
    echo "Using dispatcher release $VERSION pinned at $PIN_REF"
  elif [ "$VERSION" = "$pin_tag" ]; then
    :
  else
    echo "ULOOP_VERSION ($VERSION) does not match the pin dispatcherReleaseTag ($pin_tag) at $PIN_REF." >&2
    echo "Unset ULOOP_VERSION, or supply ULOOP_ARCHIVE_MANIFEST from a verified attestation (see README)." >&2
    exit 1
  fi

  ULOOP_ARCHIVE_MANIFEST=$pin_manifest
}

zip_extracted_uloop_present() {
  [ -f "$tmp_dir/$installed_command_name" ]
}

try_extract_zip_with_unzip() {
  command -v unzip >/dev/null 2>&1 || return 1
  unzip -q "$tmp_dir/$asset_name" -d "$tmp_dir" || return 1
  zip_extracted_uloop_present
}

try_extract_zip_with_tar() {
  command -v tar >/dev/null 2>&1 || return 1
  # Why: omit -z. A zip is not gzip, and a failed extract must fall
  # through so GNU tar (which may reject zip) can yield to the next tool.
  tar -xf "$tmp_dir/$asset_name" -C "$tmp_dir" || return 1
  zip_extracted_uloop_present
}

try_extract_zip_with_powershell() {
  case "$(uname -s)" in
    MINGW*|MSYS*) ;;
    *) return 1 ;;
  esac
  command -v powershell.exe >/dev/null 2>&1 || return 1

  zip_win=$tmp_dir/$asset_name
  dest_win=$tmp_dir
  if command -v cygpath >/dev/null 2>&1; then
    zip_win=$(cygpath -w "$tmp_dir/$asset_name")
    dest_win=$(cygpath -w "$tmp_dir")
  fi
  ULOOP_ZIP_PATH=$zip_win
  ULOOP_ZIP_DEST=$dest_win
  export ULOOP_ZIP_PATH ULOOP_ZIP_DEST
  powershell.exe -NoProfile -Command 'Expand-Archive -LiteralPath $env:ULOOP_ZIP_PATH -DestinationPath $env:ULOOP_ZIP_DEST -Force' || return 1
  zip_extracted_uloop_present
}

extract_zip_asset() {
  # Why: Git Bash and some Windows hosts have no unzip. Prefer unzip, then
  # tar (macOS/Windows 10+ bsdtar extracts zip; GNU tar often does not),
  # then PowerShell Expand-Archive on Windows. Try the next tool when the
  # current one is missing or the extract does not produce uloop.exe.
  if try_extract_zip_with_unzip; then
    return
  fi
  if try_extract_zip_with_tar; then
    return
  fi
  if try_extract_zip_with_powershell; then
    return
  fi
  echo "Unable to extract $asset_name: need unzip, a tar that can read zip, or powershell.exe Expand-Archive." >&2
  exit 1
}

extract_asset() {
  case "$asset_name" in
    *.zip)
      extract_zip_asset
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

  echo "Configuring global uloop dispatcher..."
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
resolve_manifest_from_pin_if_needed
set_download_urls

tmp_dir=$(mktemp -d)
staged_uloop_path=""
replaced_uloop_backup_path=""
final_uloop_path=""
install_completed=0

# Why: if the install failed after the old binary was moved aside, put it
# back so a failed update never leaves the user without a working uloop.
# That includes post-place failures such as --version: the new binary is
# already at the destination, so remove it first and only then restore.
# If the new binary cannot be removed, leave the backup in place.
cleanup_install() {
  if [ "$install_completed" -eq 0 ] && [ -n "$replaced_uloop_backup_path" ] && [ -f "$replaced_uloop_backup_path" ]; then
    if [ -n "$final_uloop_path" ]; then
      rm -f "$final_uloop_path" 2>/dev/null || true
    fi
    if [ -n "$final_uloop_path" ] && [ ! -e "$final_uloop_path" ]; then
      mv "$replaced_uloop_backup_path" "$final_uloop_path"
    fi
  fi
  if [ -n "$staged_uloop_path" ]; then
    rm -f "$staged_uloop_path"
  fi
  rm -rf "$tmp_dir"
}
trap cleanup_install EXIT
# Why: POSIX does not run the EXIT trap on INT/HUP/TERM unless the handler
# exits. Between move-aside and place that would leave uloop missing.
trap "exit 129" INT HUP TERM

compute_asset_sha256() {
  # Why: install.sh runs on macOS (shasum) and MINGW/MSYS (sha256sum). We do the
  # computation once here so both the same-origin .sha256 check and the trusted
  # manifest check share one hex string.
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$tmp_dir/$asset_name" | awk '{print $1}'
    return
  fi
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$tmp_dir/$asset_name" | awk '{print $1}'
    return
  fi
  echo "sha256sum or shasum is required to verify $asset_name" >&2
  exit 1
}

verify_checksum() {
  actual_hash=$(compute_asset_sha256)
  expected_hash=$(awk '{print $1}' "$tmp_dir/$asset_name.sha256")
  if [ -z "$expected_hash" ]; then
    echo "Missing checksum entry for $asset_name" >&2
    exit 1
  fi
  if [ "$expected_hash" != "$actual_hash" ]; then
    echo "Checksum mismatch for $asset_name" >&2
    exit 1
  fi
}

verify_archive_attestation_manifest() {
  # Why: when the dispatcher self-update path invokes this script it passes an
  # ULOOP_ARCHIVE_MANIFEST env carrying "<digest>  <filename>" lines that were
  # extracted from a Sigstore attestation bundle already verified against the
  # release commit SHA. Enforce that the digest we just computed matches the
  # entry for our asset_name so a compromised same-origin .sha256 file alone
  # cannot bless a swapped archive. A missing manifest must fail before
  # extraction because same-origin checksums alone do not authenticate a first
  # installation.
  if [ -z "${ULOOP_ARCHIVE_MANIFEST:-}" ]; then
    echo "Attestation manifest is required" >&2
    exit 1
  fi
  manifest_hash=$(
    printf '%s\n' "$ULOOP_ARCHIVE_MANIFEST" | awk -v name="$asset_name" '
      $2 == name { print $1; found = 1; exit }
      END { if (!found) exit 1 }
    '
  ) || {
    echo "Attestation manifest has no entry for $asset_name" >&2
    exit 1
  }
  if [ -z "$manifest_hash" ]; then
    echo "Attestation manifest entry for $asset_name is empty" >&2
    exit 1
  fi
  if [ "$manifest_hash" != "$actual_hash" ]; then
    echo "Attestation manifest hash mismatch for $asset_name" >&2
    exit 1
  fi
}

mkdir -p "$INSTALL_DIR"
echo "Downloading uloop dispatcher archive..."
curl -fsSL "$download_url" -o "$tmp_dir/$asset_name"
curl -fsSL "$checksum_url" -o "$tmp_dir/$asset_name.sha256"
echo "Verifying uloop dispatcher archive..."
verify_checksum
verify_archive_attestation_manifest
echo "Extracting uloop dispatcher archive..."
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

# Why: earlier installs rename the then-running binary aside; those
# processes have exited by now, so reclaim the leftovers. A backup whose
# process is still running stays locked and is skipped silently until a
# later install can remove it.
for leftover in "$INSTALL_DIR/$installed_command_name".old-*; do
  if [ ! -e "$leftover" ]; then
    continue
  fi
  rm -f "$leftover" 2>/dev/null || true
done

# Why: Windows locks the image file of a running executable against
# overwrite and delete but still allows rename. `uloop update` runs this
# script as a child of the running uloop.exe, so overwriting the target
# in place can never succeed there. Move the existing binary aside first;
# the EXIT trap restores it if the new binary was not placed.
if [ -e "$final_uloop_path" ]; then
  replaced_uloop_backup_path="$final_uloop_path.old-$$-$(date +%s)"
  mv "$final_uloop_path" "$replaced_uloop_backup_path"
fi
mv "$staged_uloop_path" "$final_uloop_path"
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
install_completed=1
