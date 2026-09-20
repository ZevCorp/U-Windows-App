#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
SOURCE="$ROOT/.artifacts/U.app"
DESTINATION="$HOME/Applications/U.app"

if [[ ! -x "$SOURCE/Contents/MacOS/U" ]]; then
  "$ROOT/build.sh" release
fi
# Stop every previous U bundle before replacing or launching the installed copy.
# This also removes the legacy Desktop copy that could keep a stale TCC identity alive.
/usr/bin/pkill -TERM -f '/U\.app/Contents/MacOS/U$' 2>/dev/null || true
sleep 1
/usr/bin/pkill -KILL -f '/U\.app/Contents/MacOS/U$' 2>/dev/null || true
mkdir -p "$HOME/Applications"
rm -rf "$DESTINATION"
/usr/bin/ditto "$SOURCE" "$DESTINATION"
/usr/bin/codesign --verify --deep --strict "$DESTINATION"
echo "App instalada en: $DESTINATION"
echo "Abre siempre esta copia para conservar el registro de permisos de macOS."
open "$DESTINATION"
