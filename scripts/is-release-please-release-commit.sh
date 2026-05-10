#!/bin/sh
set -eu

COMMIT_SUBJECT=${1:?commit subject is required}

case "$COMMIT_SUBJECT" in
  "chore: release "*|chore\(*\):\ release\ *)
    exit 0
    ;;
  *)
    exit 1
    ;;
esac
