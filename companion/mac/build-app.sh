#!/bin/bash
# Builds companion/dist/Solin.app: a self-contained background app for this Mac, signed with the local certificate from
# mac/make-signing-cert.sh when it is there (so macOS remembers the permissions you give her), ad-hoc otherwise.
#   mac/build-app.sh            build only
#   mac/build-app.sh --install  build, copy to ~/Applications/Solin.app and start it
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
repo="$(cd "$here/.." && pwd)"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$here/Solin.Companion.csproj" | head -1)"
app="$here/dist/Solin.app"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

echo "publishing Solin $version"
dotnet publish "$here/Solin.Companion.csproj" -c Release -r osx-arm64 --self-contained true -p:UseAppHost=true -o "$stage/publish" -nologo -v q

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$stage/publish/." "$app/Contents/MacOS/"
sed "s/@VERSION@/$version/g" "$here/mac/Info.plist" > "$app/Contents/Info.plist"
cp "$here/mac/Solin.icns" "$app/Contents/Resources/Solin.icns"

# Where her backend lives and what to run it with: an app started at login gets a bare PATH without Homebrew.
python="$(command -v python3)"
cat > "$app/Contents/Resources/solin-home.json" <<JSON
{ "repo": "$repo", "python": "$python", "path": "$PATH:/opt/homebrew/bin:/usr/local/bin" }
JSON

identity="Solin Local Signing"
if security find-identity -p codesigning 2>/dev/null | grep -q "\"$identity\""; then
  codesign --force --deep -s "$identity" "$app" 2>/dev/null
  echo "signed with $identity"
else
  codesign --force --deep -s - "$app" >/dev/null 2>&1
  echo "signed ad-hoc (run mac/make-signing-cert.sh once so permissions survive rebuilds)"
fi
echo "built $app"

if [ "${1:-}" = "--install" ]; then
  dest="$HOME/Applications/Solin.app"
  mkdir -p "$HOME/Applications"
  pkill -f "Solin.app/Contents/MacOS/Solin.Companion" 2>/dev/null || true
  # She reuses a backend that is already up, so stop it too or the new server code never runs.
  pkill -f "server.companion_api" 2>/dev/null || true
  rm -rf "$dest"
  cp -R "$app" "$dest"
  echo "installed $dest"
  open "$dest"
fi
