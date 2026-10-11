#!/usr/bin/env bash
# Builds the Windows beta zip: exports the game with Godot and packs it with a README and licences.
#
#   tools/packaging/build-windows.sh <version> [out-dir]      e.g. 0.1.42-beta build
#
# Run from the-curator/. Needs the Godot 4.6.3 .NET editor (GODOT, default `godot`) with the
# 4.6.3.stable.mono export templates installed, the .NET 10 SDK, and zip. CI runs this
# (.github/workflows/ci-the-curator.yml); it works the same locally on Linux or macOS.
# Prints the zip's path last. Under GitHub Actions it also writes `zip` and `folder` (the unzipped
# build) to $GITHUB_OUTPUT.
set -euo pipefail

version="${1:?usage: tools/packaging/build-windows.sh <version> [out-dir]}"
out="$(mkdir -p "${2:-build}" && cd "${2:-build}" && pwd)"
godot="${GODOT:-godot}"
here="$(cd "$(dirname "$0")" && pwd)"
commit="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
name="TheCurator-${version}"
stage="${out}/${name}"
zip="${out}/${name}-windows.zip"

# Windows file versions are four numbers: 0.1.42-beta becomes 0.1.42.0.
numeric="${version%%-*}"
while [ "$(tr -cd '.' <<<"$numeric" | wc -c)" -lt 3 ]; do numeric="${numeric}.0"; done

# The version goes into the game (the title screen shows it) and the exe's file properties for
# this export only: both files are put back however the script ends.
backup="$(mktemp -d)"
cp game/project.godot game/export_presets.cfg "$backup/"
trap 'cp "$backup/project.godot" "$backup/export_presets.cfg" game/; rm -rf "$backup"' EXIT
if grep -q '^config/version=' game/project.godot; then
  sed -i.bak "s|^config/version=.*|config/version=\"${version}\"|" game/project.godot
else
  sed -i.bak "/^\[application\]$/a\\
config/version=\"${version}\"
" game/project.godot
fi
sed -i.bak -e "s|^application/file_version=.*|application/file_version=\"${numeric}\"|" \
  -e "s|^application/product_version=.*|application/product_version=\"${numeric}\"|" game/export_presets.cfg
rm -f game/project.godot.bak game/export_presets.cfg.bak

rm -rf "$stage" "$zip"
mkdir -p "$stage/licenses"
"$godot" --headless --path game --import
"$godot" --headless --path game --export-release "Windows Desktop" "${stage}/TheCurator.exe"
for file in TheCurator.exe TheCurator.pck; do
  [ -s "${stage}/${file}" ] || { echo "The export made no ${file}." >&2; exit 1; }
done
compgen -G "${stage}/data_*_windows_x86_64" >/dev/null || { echo "The export made no .NET data folder." >&2; exit 1; }

# What a playtester reads first, and the licences for what ships inside: the engine (asked of
# Godot itself, so it matches the build) and every font.
sed -e "s|{{VERSION}}|${version}|g" -e "s|{{COMMIT}}|${commit}|g" "${here}/README.txt" >"${stage}/README.txt"
licence_dir="$(mktemp -d)"
printf 'extends SceneTree\n\nfunc _init() -> void:\n\tprint("@@LICENCE@@")\n\tprint(Engine.get_license_text())\n\tquit()\n' >"${licence_dir}/licence.gd"
"$godot" --headless --path game --script "${licence_dir}/licence.gd" | sed -n '/@@LICENCE@@/,$p' | tail -n +2 >"${stage}/licenses/Godot-Engine.txt"
rm -rf "$licence_dir"
grep -q "Godot Engine contributors" "${stage}/licenses/Godot-Engine.txt" || { echo "Couldn't read Godot's licence." >&2; exit 1; }
for font in game/fonts/*/; do
  cp "${font}OFL.txt" "${stage}/licenses/$(basename "$font")-OFL.txt"
done

(cd "$out" && zip -qr9 "$zip" "$name")
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  echo "zip=${zip}" >>"$GITHUB_OUTPUT"
  echo "folder=${stage}" >>"$GITHUB_OUTPUT"
fi
echo "$zip"
