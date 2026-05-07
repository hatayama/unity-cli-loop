#!/bin/sh
set -eu

cd "$(dirname "$0")/.."

dotnet run --project tools/UnityCliLoop.DeadCodeScanner/UnityCliLoop.DeadCodeScanner.csproj -- "$@"
