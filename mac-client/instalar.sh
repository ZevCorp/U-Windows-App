#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
SOURCE="$ROOT/.artifacts/U.app"
DESTINATION="$HOME/Applications/U.app"

if [[ ! -x "$SOURCE/Contents/MacOS/U" ]]; then
  "$ROOT/build.sh" release
fi
mkdir -p "$HOME/Applications"
rm -rf "$DESTINATION"
/usr/bin/ditto "$SOURCE" "$DESTINATION"
/usr/bin/codesign --verify --deep --strict "$DESTINATION"
echo "App instalada en: $DESTINATION"
echo "Abre siempre esta copia para conservar el registro de permisos de macOS."
open -n "$DESTINATION"
