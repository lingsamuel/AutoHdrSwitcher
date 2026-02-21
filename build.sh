#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_PATH="$ROOT_DIR/AutoHdrSwitcher.sln"
PROJECT_PATH="$ROOT_DIR/src/AutoHdrSwitcher/AutoHdrSwitcher.csproj"
ARTIFACTS_DIR="$ROOT_DIR/artifacts"
PUBLISH_DIR="$ARTIFACTS_DIR/publish"
CONFIGURATION="${CONFIGURATION:-Release}"
ACTION="${1:-publish}"

if command -v dotnet.exe >/dev/null 2>&1; then
  DOTNET_CMD="dotnet.exe"
elif command -v dotnet >/dev/null 2>&1; then
  DOTNET_CMD="dotnet"
else
  echo "Error: dotnet/dotnet.exe not found in PATH." >&2
  exit 127
fi

to_dotnet_path() {
  local p="$1"
  if [[ "$DOTNET_CMD" == "dotnet.exe" ]]; then
    wslpath -w "$p"
  else
    echo "$p"
  fi
}

SOLUTION_ARG="$(to_dotnet_path "$SOLUTION_PATH")"
PROJECT_ARG="$(to_dotnet_path "$PROJECT_PATH")"
PUBLISH_ARG="$(to_dotnet_path "$PUBLISH_DIR")"

echo "Using: $DOTNET_CMD"
echo "Action: $ACTION"
echo "Configuration: $CONFIGURATION"

case "$ACTION" in
  restore)
    "$DOTNET_CMD" restore "$SOLUTION_ARG"
    ;;
  build)
    "$DOTNET_CMD" build "$SOLUTION_ARG" -c "$CONFIGURATION" --nologo
    ;;
  publish)
    mkdir -p "$PUBLISH_DIR"
    "$DOTNET_CMD" publish "$PROJECT_ARG" -c "$CONFIGURATION" -o "$PUBLISH_ARG" --nologo
    echo "Publish output: $PUBLISH_DIR"
    ;;
  clean)
    "$DOTNET_CMD" clean "$SOLUTION_ARG" -c "$CONFIGURATION" --nologo
    ;;
  *)
    echo "Usage: ./build.sh [restore|build|publish|clean]" >&2
    exit 2
    ;;
esac
