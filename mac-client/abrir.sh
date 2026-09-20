#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="$HOME/Applications/U.app"
if [[ ! -x "$APP/Contents/MacOS/U" ]]; then
  echo "No existe la copia instalada. Instalando la versión actual…"
  "$ROOT/instalar.sh"
  exit 0
fi
# Always open the canonical installed bundle so macOS TCC checks the same identity.
open "$APP"
echo "Ü para Mac abierta desde: $APP"
