#!/bin/sh
set -eu

: "${EVENT_NAME:?EVENT_NAME is required}"

EVENT_REF_NAME=${EVENT_REF_NAME:-}
BEFORE_SHA=${BEFORE_SHA:-}
INPUT_RELEASE_TAG=${INPUT_RELEASE_TAG:-}
INPUT_DRY_RUN=${INPUT_DRY_RUN:-false}

release_is_published() {
  release_tag=$1
  is_draft=$(gh release view "$release_tag" --json isDraft --jq '.isDraft' 2>/dev/null || true)

  [ "$is_draft" = "false" ]
}

VERSION=$(jq -r '.["Packages/src"]' .release-please-manifest.json)
if [ -z "$VERSION" ] || [ "$VERSION" = "null" ]; then
  echo "Could not resolve Packages/src version from .release-please-manifest.json." >&2
  exit 1
fi

RELEASE_TAG="${INPUT_RELEASE_TAG:-v$VERSION}"
case "$RELEASE_TAG" in
  v[0-9]*)
    ;;
  *)
    echo "Invalid release tag: $RELEASE_TAG" >&2
    exit 1
    ;;
esac

case "$RELEASE_TAG" in
  *[!A-Za-z0-9._-]*)
    echo "Invalid release tag: $RELEASE_TAG" >&2
    exit 1
    ;;
esac

IS_PRERELEASE=false
case "$VERSION" in
  *-*)
    IS_PRERELEASE=true
    ;;
esac

SHOULD_PUBLISH=true
if [ "$EVENT_NAME" = "push" ]; then
  case "$EVENT_REF_NAME" in
    main)
      if [ "$IS_PRERELEASE" = "true" ]; then
        echo "Refusing to publish prerelease version $VERSION from main." >&2
        exit 1
      fi
      ;;
    v3-beta)
      if [ "$IS_PRERELEASE" != "true" ]; then
        echo "Refusing to publish stable version $VERSION from v3-beta." >&2
        exit 1
      fi
      ;;
    *)
      echo "Skipping native CLI publish for unsupported branch $EVENT_REF_NAME." >&2
      SHOULD_PUBLISH=false
      ;;
  esac

  if [ "$SHOULD_PUBLISH" = "true" ]; then
    case "$BEFORE_SHA" in
      ""|0000000000000000000000000000000000000000)
        echo "Cannot compare release manifest from push before SHA: $BEFORE_SHA" >&2
        exit 1
        ;;
    esac

    PREVIOUS_MANIFEST=$(git show "$BEFORE_SHA:.release-please-manifest.json") || {
      echo "Could not read release manifest from push before SHA: $BEFORE_SHA" >&2
      exit 1
    }
    PREVIOUS_VERSION=$(printf '%s\n' "$PREVIOUS_MANIFEST" | jq -r '.["Packages/src"]')
    if [ "$PREVIOUS_VERSION" = "$VERSION" ]; then
      if release_is_published "$RELEASE_TAG"; then
        SHOULD_PUBLISH=false
      else
        echo "Release $RELEASE_TAG is not published; retrying native CLI publish." >&2
      fi
    fi
  fi
fi

TARGET_SHA=$(git rev-parse HEAD)
DRY_RUN=false
if [ "$INPUT_DRY_RUN" = "true" ]; then
  DRY_RUN=true
fi

printf 'publish=%s\n' "$SHOULD_PUBLISH"
printf 'tag=%s\n' "$RELEASE_TAG"
printf 'version=%s\n' "$VERSION"
printf 'sha=%s\n' "$TARGET_SHA"
printf 'dry_run=%s\n' "$DRY_RUN"

echo "Publish: $SHOULD_PUBLISH" >&2
echo "Release tag: $RELEASE_TAG" >&2
echo "Target SHA: $TARGET_SHA" >&2
