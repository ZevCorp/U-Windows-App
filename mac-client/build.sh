#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
configuration="${1:-release}"
if [[ "$configuration" != release && "$configuration" != debug ]]; then
  echo 'Uso: ./build.sh [release|debug]' >&2
  exit 2
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
</dict></plist>
PLIST
  /usr/bin/plutil -lint "$bundle/Contents/Info.plist"
  if [[ -n "${CODE_SIGN_IDENTITY:-}" ]]; then
    /usr/bin/codesign --force --options runtime --timestamp --entitlements entitlements.plist --sign "$CODE_SIGN_IDENTITY" "$bundle"
  else
    # Keep the designated requirement tied to the bundle identifier. The default
    # ad-hoc requirement is the executable cdhash, which changes on every build
    # and makes TCC treat the rebuilt app as a new application.
    requirement="designated => identifier \"$identifier\""
    /usr/bin/codesign --force --entitlements entitlements.plist --requirements "=$requirement" --sign - "$bundle"
  fi
  /usr/bin/codesign --verify --deep --strict "$bundle"
}
make_bundle U com.zevcorp.u 'Ü para Mac'
make_bundle UFixture com.zevcorp.u.mac.fixture 'Ü Prueba local'
/usr/bin/ditto -c -k --keepParent "$output_dir/U.app" "$output_dir/U-Mac.zip"
echo "App lista: $output_dir/U.app"
echo "Abre con: open \"$output_dir/U.app\""
