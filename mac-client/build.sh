#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
configuration="${1:-release}"
if [[ "$configuration" != release && "$configuration" != debug ]]; then
  echo 'Uso: ./build.sh [release|debug]' >&2
  exit 2
fi
APP_IDENTIFIER="com.zevcorp.u.mac"
signing_identity="${CODE_SIGN_IDENTITY:-}"
signing_keychain=""
signing_mode="developer-id"
restore_keychain_search_list() { :; }
if [[ -z "$signing_identity" ]]; then
  IFS=$'\t' read -r signing_identity signing_keychain signing_mode < <("$PWD/ensure-local-signing.sh" --metadata)
  saved_keychains=()
  while IFS= read -r keychain; do saved_keychains+=("$keychain"); done < <(security list-keychains -d user | sed -E 's/^[[:space:]]*"//; s/"[[:space:]]*$//')
  security list-keychains -d user -s "$signing_keychain" "${saved_keychains[@]}"
  restore_keychain_search_list() { security list-keychains -d user -s "${saved_keychains[@]}"; }
  trap restore_keychain_search_list EXIT
fi
swift run -c "$configuration" NativeContract
swift build -c "$configuration" --product U
swift build -c "$configuration" --product UFixture
binary_dir="$(swift build -c "$configuration" --show-bin-path)"
output_dir="$PWD/.artifacts"
mkdir -p "$output_dir"
make_bundle() {
  local executable="$1" identifier="$2" display_name="$3"
  local bundle="$output_dir/$executable.app"
  mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
  cp "$binary_dir/$executable" "$bundle/Contents/MacOS/$executable"
  cat > "$bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>$executable</string>
<key>CFBundleIdentifier</key><string>$identifier</string>
<key>CFBundleName</key><string>$display_name</string>
<key>CFBundleDisplayName</key><string>$display_name</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>0.1.0</string>
<key>CFBundleVersion</key><string>1</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
<key>NSMicrophoneUsageDescription</key><string>Ü usa el micrófono para escuchar lo que le pides. Puedes silenciarlo en cualquier momento.</string>
<key>NSSpeechRecognitionUsageDescription</key><string>Ü convierte tu voz en texto para entender tus peticiones cuando utilizas el dictado nativo.</string>
<key>NSAppleEventsUsageDescription</key><string>Ü puede abrir y utilizar aplicaciones cuando se lo pides.</string>
<key>NSScreenCaptureUsageDescription</key><string>Ü necesita ver la pantalla para comprobar el resultado de las acciones que realiza.</string>
<key>USigningMode</key><string>$signing_mode</string>
</dict></plist>
PLIST
  /usr/bin/plutil -lint "$bundle/Contents/Info.plist"
  if [[ "$signing_mode" == "developer-id" ]]; then
    /usr/bin/codesign --force --options runtime --timestamp --entitlements entitlements.plist --sign "$CODE_SIGN_IDENTITY" "$bundle"
  else
    /usr/bin/codesign --force --keychain "$signing_keychain" --entitlements entitlements.plist --sign "$signing_identity" "$bundle"
  fi
  /usr/bin/codesign --verify --deep --strict "$bundle"
}
make_bundle U "$APP_IDENTIFIER" 'Ü para Mac'
make_bundle UFixture com.zevcorp.u.mac.fixture 'Ü Prueba local'
/usr/bin/ditto -c -k --keepParent "$output_dir/U.app" "$output_dir/U-Mac.zip"
echo "App lista: $output_dir/U.app"
echo "Firma: $signing_mode ($signing_identity)"
echo "Abre con: open \"$output_dir/U.app\""
