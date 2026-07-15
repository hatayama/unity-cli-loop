# Dispatcher pin release order

The Unity package uses a provenance-pinned dispatcher Release for first
installation. Package release preparation must follow this order:

1. Publish the dispatcher Release and its Sigstore attestations.
2. Run `stamp-dispatcher-pin` against that immutable Release.
3. Verify the resulting pin with `check-dispatcher-pin` and publish the Unity
   package.

Changing `scripts/install.sh` or `scripts/install.ps1` does not immediately
change the Unity first-install path. Unity downloads the script from the
Release named in its package pin. The change becomes active only after a new
dispatcher Release is published and a later package release stamps that
Release. The pin guard reports source-script drift for review but does not
block the dispatcher Release that is required to resolve it.
