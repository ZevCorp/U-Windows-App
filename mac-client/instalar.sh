#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
SOURCE="$ROOT/.artifacts/U.app"
DESTINATION="$HOME/Applications/U.app"
EXPECTED_IDENTIFIER="com.zevcorp.u.mac"
LEGACY_DESKTOP_APP="$HOME/Desktop/U/U-Mac/U.app"
TRASH="$HOME/.Trash"

stop_bundle() {
  local executable="$1"
  local signal="$2"
  local pid
  while IFS= read -r pid; do
    [[ "$pid" == "$$" ]] && continue
    kill "-$signal" "$pid" 2>/dev/null || true
  done < <(/usr/bin/pgrep -f -- "$executable" 2>/dev/null || true)
}

if [[ ! -x "$SOURCE/Contents/MacOS/U" ]]; then
  "$ROOT/build.sh" release
fi
# Stop only the known U copies before replacing the canonical installed bundle.
stop_bundle "$DESTINATION/Contents/MacOS/U" TERM
stop_bundle "$LEGACY_DESKTOP_APP/Contents/MacOS/U" TERM
sleep 1
stop_bundle "$DESTINATION/Contents/MacOS/U" KILL
stop_bundle "$LEGACY_DESKTOP_APP/Contents/MacOS/U" KILL
mkdir -p "$HOME/Applications"
mkdir -p "$TRASH"

# The old Desktop bundle has the historical ad-hoc TCC identity. Move it to the
# Trash so System Settings cannot keep offering its stale record as another U.
if [[ -d "$LEGACY_DESKTOP_APP" ]]; then
  mv "$LEGACY_DESKTOP_APP" "$TRASH/U.app.legacy-$(date +%Y%m%d-%H%M%S)"
fi

STAGING="$HOME/Applications/.U.installing-$(/usr/bin/uuidgen).app"
/usr/bin/ditto "$SOURCE" "$STAGING"
actual_identifier="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$STAGING/Contents/Info.plist")"
if [[ "$actual_identifier" != "$EXPECTED_IDENTIFIER" ]]; then
  echo "La app preparada tiene un identificador inesperado: $actual_identifier" >&2
  exit 1
fi
if [[ -d "$DESTINATION" ]]; then
  mv "$DESTINATION" "$TRASH/U.app.previous-$(date +%Y%m%d-%H%M%S)"
fi
mv "$STAGING" "$DESTINATION"
/usr/bin/codesign --verify --deep --strict "$DESTINATION"
actual_requirement="$(/usr/bin/codesign -d -r- "$DESTINATION" 2>&1)"
if [[ "$actual_requirement" == *"cdhash"* ]]; then
  echo "La app instalada tiene una firma ad hoc inestable; no se instaló." >&2
  exit 1
fi
echo "App instalada en: $DESTINATION"
echo "Identidad TCC: $EXPECTED_IDENTIFIER"
echo "Abre siempre esta copia para conservar el registro de permisos de macOS."
open "$DESTINATION"
