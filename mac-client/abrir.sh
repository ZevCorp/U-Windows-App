#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="$ROOT/.artifacts/U.app"
if [[ ! -x "$APP/Contents/MacOS/U" ]]; then
  echo "No existe la app compilada. Ejecutando la compilación de desarrollo…"
  "$ROOT/build.sh" debug
fi
# open is intentional: macOS TCC permissions belong to the app bundle, not a loose executable.
open -n "$APP"
echo "Ü para Mac abierta desde: $APP"
