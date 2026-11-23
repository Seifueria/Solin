#!/bin/bash
# Makes a local code-signing certificate, "Solin Local Signing", in your login keychain, once. mac/build-app.sh signs
# Solin.app with it when it is there, so macOS keeps the permissions you grant her (Screen Recording) across rebuilds:
# an ad-hoc signature changes her identity every build, and macOS asks again each time. Self-signed and only meaningful
# on this Mac; no admin rights, no trust settings changed. Only codesign may use its key. The key files made on the way
# are deleted; the key lives on only in the keychain.
#   mac/make-signing-cert.sh
# To remove it later: Keychain Access › login › My Certificates › "Solin Local Signing" › Delete.
set -euo pipefail
name="Solin Local Signing"
if security find-identity -p codesigning 2>/dev/null | grep -q "\"$name\""; then
  echo "already there: $name"
  exit 0
fi
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cat > "$work/cert.cnf" <<CNF
[ req ]
distinguished_name = dn
x509_extensions = ext
prompt = no
[ dn ]
CN = $name
[ ext ]
basicConstraints = critical, CA:false
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
CNF
/usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -keyout "$work/key.pem" -out "$work/cert.pem" -config "$work/cert.cnf" 2>/dev/null
pass="$(/usr/bin/openssl rand -hex 16)"
/usr/bin/openssl pkcs12 -export -inkey "$work/key.pem" -in "$work/cert.pem" -name "$name" -out "$work/solin.p12" -passout "pass:$pass"
security import "$work/solin.p12" -k "$HOME/Library/Keychains/login.keychain-db" -P "$pass" -T /usr/bin/codesign >/dev/null
echo "made: $name"
