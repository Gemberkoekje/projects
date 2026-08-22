#!/usr/bin/env bash
# Builds IronFlag art assets in Blender and writes them into the Unity project.
#
# Thin wrapper around blender/build.py, matching build.ps1 for anyone working
# from a POSIX shell. Locates Blender via $IRONFLAG_BLENDER, then the default
# install folders, then $PATH.
#
#   ./build.sh                 # rebuild everything
#   ./build.sh --asset Jeep    # rebuild one asset
#   ./build.sh --list          # list known assets
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

find_blender() {
    if [[ -n "${IRONFLAG_BLENDER:-}" && -x "${IRONFLAG_BLENDER}" ]]; then
        echo "${IRONFLAG_BLENDER}"
        return
    fi

    # Windows installs, newest version first.
    local candidate
    while IFS= read -r candidate; do
        if [[ -x "${candidate}" ]]; then
            echo "${candidate}"
            return
        fi
    done < <(printf '%s\n' "/c/Program Files/Blender Foundation"/*/blender.exe | sort -r)

    for candidate in \
        "/Applications/Blender.app/Contents/MacOS/Blender" \
        "/usr/bin/blender" \
        "/usr/local/bin/blender" \
        "/snap/bin/blender"
    do
        if [[ -x "${candidate}" ]]; then
            echo "${candidate}"
            return
        fi
    done

    if command -v blender >/dev/null 2>&1; then
        command -v blender
        return
    fi

    echo "Blender not found. Set IRONFLAG_BLENDER to the Blender executable." >&2
    exit 1
}

blender="$(find_blender)"
echo "Using ${blender}"
exec "${blender}" --background --factory-startup \
    --python "${script_directory}/build.py" -- "$@"
