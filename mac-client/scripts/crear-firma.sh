#!/bin/bash
# Crea la identidad de firma "U Dev Signing" en el llavero de ESTA máquina, una sola vez.
#
# El problema que resuelve, medido el 2026-08-21: `codesign --sign -` (ad-hoc) genera una firma
# NUEVA en cada build, y macOS ata los permisos de Grabación de pantalla / Accesibilidad a esa
# firma exacta — no al nombre de la app. Cada recompilación era, para TCC, otra app, y había que
# volver a conceder el permiso una y otra vez, sin parar.
#
# El arreglo: un certificado autofirmado, hecho una vez, que `hacer-app.sh` reutiliza en cada
# build. Mientras exista, la firma no cambia y el permiso que concedas sobrevive a recompilar.
#
# Se corre UNA VEZ por máquina — no por sesión, no por rama. Si ya existe la identidad, no hace
# nada y lo dice.
set -euo pipefail

NOMBRE="U Dev Signing"

if security find-identity -v -p codesigning 2>/dev/null | grep -q "$NOMBRE"; then
  echo "▸ ya existe «$NOMBRE» en el llavero — nada que hacer."
  exit 0
fi

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

cat > "$TMP/codesign.cnf" <<EOF
[req]
distinguished_name = req_distinguished_name
x509_extensions = v3_req
prompt = no

[req_distinguished_name]
CN = ${NOMBRE}

[v3_req]
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
basicConstraints = critical, CA:false
EOF

echo "▸ generando el certificado…"
openssl req -x509 -newkey rsa:2048 -keyout "$TMP/key.pem" -out "$TMP/cert.pem" \
  -days 3650 -nodes -config "$TMP/codesign.cnf"

# Contraseña de un solo uso: protege el .p12 mientras existe en disco (unos segundos, en /tmp),
# no la identidad ya importada al llavero.
CLAVE=$(openssl rand -hex 16)
openssl pkcs12 -export -out "$TMP/cert.p12" -inkey "$TMP/key.pem" -in "$TMP/cert.pem" -passout "pass:$CLAVE"

echo "▸ importando al llavero de inicio de sesión…"
security import "$TMP/cert.p12" -k ~/Library/Keychains/login.keychain-db -P "$CLAVE" \
  -T /usr/bin/codesign -T /usr/bin/security

echo "▸ dándole confianza para firmar código…"
security add-trusted-cert -d -r trustRoot -p codeSign -k ~/Library/Keychains/login.keychain-db "$TMP/cert.pem"

if security find-identity -v -p codesigning 2>/dev/null | grep -q "$NOMBRE"; then
  echo "▸ listo: «$NOMBRE» es una identidad de firma válida en este Mac."
  echo "  hacer-app.sh la va a usar sola a partir de ahora."
else
  echo "▸ ✘ algo falló — «security find-identity -v -p codesigning» no la ve como válida."
  exit 1
fi
