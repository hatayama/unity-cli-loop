#!/bin/sh
# Verifies the README bootstrap manifest derivation preserves every attested subject.
set -eu

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT INT HUP TERM

payload='{"subject":[{"name":"install.sh","digest":{"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}},{"name":"uloop-dispatcher-darwin-arm64.tar.gz","digest":{"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}]}'
encoded_payload=$(printf '%s' "$payload" | base64)
printf '{"dsseEnvelope":{"payload":"%s"}}\n' "$encoded_payload" > "$TMP_DIR/bundle.json"

manifest=$(jq -r '.dsseEnvelope.payload | @base64d | fromjson | .subject[] | "\(.digest.sha256)  \(.name)"' "$TMP_DIR/bundle.json" | LC_ALL=C sort)
expected='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh
bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  uloop-dispatcher-darwin-arm64.tar.gz'

if [ "$manifest" != "$expected" ]; then
  echo "bootstrap manifest derivation mismatch" >&2
  exit 1
fi
