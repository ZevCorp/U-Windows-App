#!/bin/bash
# Empaqueta Ü como una app de verdad: U.app.
#
# Hace falta que sea una app (y no el binario suelto) por dos razones: para poder abrirla con doble
# clic, y porque los permisos de macOS —Accesibilidad, Grabación de pantalla, Micrófono— se conceden
# a una app identificada, no a un ejecutable anónimo. Sin esto no aparece en la lista de Ajustes.
set -euo pipefail
cd "$(dirname "$0")"

CONFIG="${1:-release}"
APP="U.app"

echo "▸ compilando ($CONFIG)…"
swift build -c "$CONFIG"
BIN=$(swift build -c "$CONFIG" --show-bin-path)/U

echo "▸ armando $APP…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/U"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>              <string>Ü</string>
  <key>CFBundleDisplayName</key>       <string>Ü</string>
  <key>CFBundleExecutable</key>        <string>U</string>
  <key>CFBundleIdentifier</key>        <string>com.zevcorp.u</string>
  <key>CFBundlePackageType</key>       <string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.1</string>
  <key>CFBundleVersion</key>           <string>1</string>
  <key>LSMinimumSystemVersion</key>    <string>13.0</string>

  <!-- Ü no es una app que se «abre»: está ahí. Sin icono en el Dock ni barra de menús propia. -->
  <key>LSUIElement</key>               <true/>

  <!-- Los textos que verá el usuario cuando macOS le pida cada permiso. Se explican por lo que Ü
       hace con ellos, no por la API: quien lee ese diálogo está decidiendo si confía. -->
  <key>NSMicrophoneUsageDescription</key>
  <string>Para poder hablar contigo mientras trabaja.</string>
  <key>NSSpeechRecognitionUsageDescription</key>
  <string>Para entender lo que le pides en voz alta.</string>
  <key>NSAppleEventsUsageDescription</key>
  <string>Para poder usar las apps de tu Mac por ti.</string>
</dict>
</plist>
PLIST

# Firma local (ad-hoc). No sirve para distribuir, pero sí para que macOS le dé una identidad estable
# y los permisos concedidos no se pierdan en cada recompilación.
codesign --force --deep --sign - "$APP" 2>/dev/null || echo "  (aviso: no se pudo firmar; los permisos se pedirán de nuevo en cada build)"

echo "▸ listo: $(pwd)/$APP"

# EL CONTRATO, EN CADA BUILD. Esto es lo que lo convierte en compuerta y no en un informe que nadie
# corre: si algo que ya funcionaba se rompió, se sabe AHORA y no tres días después hablándole a la
# carita. Va el modo rápido —las siete promesas que no necesitan micrófono, unos dos minutos—;
# las de micrófono se corren aparte y con la habitación en silencio:
#
#     swift run -c release Contrato            todas
#     swift run -c release Contrato 8 9 10     solo las del micrófono
#
# Y se puede saltar cuando solo se está tanteando algo:  SIN_CONTRATO=1 ./hacer-app.sh
if [ "${SIN_CONTRATO:-0}" = "1" ]; then
  echo "▸ contrato: saltado (SIN_CONTRATO=1)"
  exit 0
fi

echo "▸ corriendo el contrato (modo rápido)…"
if swift run -c "$CONFIG" Contrato --rapido; then
  exit 0
else
  echo ""
  echo "  ⚠︎  El binario quedó compilado en $APP, pero el contrato NO está intacto."
  echo "      Míralo antes de dar nada por bueno."
  exit 1
fi
