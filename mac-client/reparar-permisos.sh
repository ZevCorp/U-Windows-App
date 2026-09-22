#!/bin/bash
# One-time recovery for a previous ad-hoc installation. This script never runs
# from the app and must not be used after ordinary updates.
set -euo pipefail

APP="$HOME/Applications/U.app"
IDENTIFIER="com.zevcorp.u.mac"
if [[ ! -x "$APP/Contents/MacOS/U" ]]; then
  echo "Instala primero la app actual con ./mac-client/instalar.sh" >&2
  exit 1
fi

while IFS= read -r pid; do
  [[ "$pid" == "$$" ]] && continue
  kill -TERM "$pid" 2>/dev/null || true
done < <(/usr/bin/pgrep -f -- "$APP/Contents/MacOS/U" 2>/dev/null || true)
sleep 1
/usr/bin/tccutil reset Accessibility "$IDENTIFIER"
/usr/bin/tccutil reset ScreenCapture "$IDENTIFIER"
echo "Se limpiaron únicamente los registros TCC de $IDENTIFIER."
echo "Abriendo la app instalada: concede Accesibilidad y Grabación de pantalla una sola vez."
open "$APP"
