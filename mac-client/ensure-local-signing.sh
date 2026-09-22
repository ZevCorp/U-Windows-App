#!/bin/bash
# Creates a machine-local, persistent signing identity for development builds.
# It is intentionally separate from public distribution: public builds must use
# an Apple Developer ID identity and notarization.
set -euo pipefail

ROOT_DIR="${U_LOCAL_SIGNING_DIRECTORY:-$HOME/Library/Application Support/U Mac/Signing}"
KEYCHAIN="$ROOT_DIR/U Local Stable Signing.keychain-db"
SECRET_FILE="$ROOT_DIR/keychain-password"
IDENTITY_NAME="U Local Stable Signing"

metadata() {
  local identity
  # A self-signed development certificate does not appear in
  # `find-identity -p codesigning` unless the user changes global trust policy.
  # codesign can use its SHA-1 fingerprint directly once the keychain is in its
  # search list, keeping this setup private to Ü's development keychain.
  identity="$(security find-certificate -Z -c "$IDENTITY_NAME" "$KEYCHAIN" 2>/dev/null | awk '/SHA-1 hash:/ { print $3; exit }')"
  [[ -n "$identity" ]] || return 1
  printf '%s\t%s\t%s\n' "$identity" "$KEYCHAIN" "local-stable"
}

if metadata >/dev/null 2>&1; then
  if [[ "${1:-}" == "--metadata" ]]; then metadata; fi
  exit 0
fi

mkdir -p "$ROOT_DIR"
umask 077
if [[ ! -f "$SECRET_FILE" ]]; then /usr/bin/uuidgen > "$SECRET_FILE"; fi

TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/u-local-signing.XXXXXX")"
trap 'rm -rf "$TEMP_DIR"' EXIT
CERT="$TEMP_DIR/certificate.pem"
KEY="$TEMP_DIR/private-key.pem"
P12="$TEMP_DIR/identity.p12"

if [[ ! -f "$KEYCHAIN" ]]; then
  security create-keychain -p "$(<"$SECRET_FILE")" "$KEYCHAIN"
fi
security unlock-keychain -p "$(<"$SECRET_FILE")" "$KEYCHAIN"
security set-keychain-settings -lut 7200 "$KEYCHAIN"

/usr/bin/openssl req -x509 -newkey rsa:2048 -sha256 -nodes \
  -keyout "$KEY" -out "$CERT" -days 3650 \
  -subj "/CN=$IDENTITY_NAME/O=U Mac Local Development" \
  -addext 'keyUsage=digitalSignature' \
  -addext 'extendedKeyUsage=codeSigning' >/dev/null 2>&1
/usr/bin/openssl pkcs12 -export -out "$P12" -inkey "$KEY" -in "$CERT" \
  -passout pass:temporary-u-local-signing >/dev/null 2>&1
security import "$P12" -k "$KEYCHAIN" -P temporary-u-local-signing -T /usr/bin/codesign >/dev/null

if [[ "${1:-}" == "--metadata" ]]; then metadata; fi
