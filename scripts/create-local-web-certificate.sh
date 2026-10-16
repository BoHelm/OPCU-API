#!/usr/bin/env bash
# Ausschließlich lokale Entwicklungsbereitstellung: kein öffentlich vertrauenswürdiges Produktivzertifikat.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p .keys certificates
chmod 700 .keys
if [ -e .keys/admin-tls-password ] || [ -e certificates/admin.pfx ]; then
    echo "Bestehende TLS-Dateien werden nicht überschrieben." >&2
    exit 1
fi
python3 - <<'PY'
import secrets
from pathlib import Path
p=Path('.keys/admin-tls-password')
p.write_text(secrets.token_urlsafe(48))
p.chmod(0o444)
PY
temporary=$(mktemp -d .keys/web-cert.XXXXXX)
trap 'rm -rf -- "$temporary"' EXIT
openssl req -x509 -newkey rsa:3072 -noenc -days 30 \
    -subj '/CN=localhost' -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' \
    -addext 'extendedKeyUsage=serverAuth' \
    -keyout "$temporary/key.pem" -out certificates/admin.crt
openssl pkcs12 -export -inkey "$temporary/key.pem" -in certificates/admin.crt \
    -out certificates/admin.pfx -passout file:.keys/admin-tls-password
# Der verschlüsselte PFX ist lesbar; sein Passwort liegt getrennt unter dem nicht durchsuchbaren .keys.
# Docker-Secrets-Bindmounts müssen für den nicht privilegierten Containerbenutzer lesbar sein.
chmod 755 certificates
chmod 444 certificates/admin.pfx certificates/admin.crt
echo "Lokales TLS-Zertifikat erstellt. admin.crt gezielt im lokalen Client vertrauen; für Produktion eigene CA verwenden."
