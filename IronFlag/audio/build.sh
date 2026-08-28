#!/usr/bin/env bash
# Renders IronFlag sounds and music in SuperCollider and writes them into the
# Unity project.
#
# Thin wrapper around audio/build.scd, matching build.ps1 for anyone working
# from a POSIX shell. Locates sclang via $IRONFLAG_SUPERCOLLIDER, then the
# default install folders, then $PATH.
#
#   ./build.sh                          # render everything
#   ./build.sh --sound Cannon           # render one sound
#   ./build.sh --list                   # list known sounds
#   ./build.sh --listen --sound Cannon  # play it instead of writing it
#
# --listen is the iteration loop: edit a recipe, hear it, repeat, and only run
# a real render once it sounds right.
#
# The --flags below are translated into bare words before they reach sclang,
# which would otherwise try to parse them as its own options and exit. The run
# is also given a hard timeout, because sclang does not quit when a script
# raises - it sits in its event loop forever.
set -euo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
timeout_seconds="${IRONFLAG_AUDIO_TIMEOUT:-600}"

find_supercollider() {
    if [[ -n "${IRONFLAG_SUPERCOLLIDER:-}" && -x "${IRONFLAG_SUPERCOLLIDER}" ]]; then
        echo "${IRONFLAG_SUPERCOLLIDER}"
        return
    fi

    # Windows installs, newest version first.
    local candidate
    while IFS= read -r candidate; do
        if [[ -x "${candidate}" ]]; then
            echo "${candidate}"
            return
        fi
    done < <(printf '%s\n' "/c/Program Files"/SuperCollider*/sclang.exe | sort -r)

    for candidate in \
        "/Applications/SuperCollider.app/Contents/MacOS/sclang" \
        "/usr/bin/sclang" \
        "/usr/local/bin/sclang"
    do
        if [[ -x "${candidate}" ]]; then
            echo "${candidate}"
            return
        fi
    done

    if command -v sclang >/dev/null 2>&1; then
        command -v sclang
        return
    fi

    echo "SuperCollider not found. Set IRONFLAG_SUPERCOLLIDER to the sclang executable." >&2
    exit 1
}

forwarded=()
entry="build.scd"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --list)   forwarded+=("list"); shift ;;
        --quiet)  forwarded+=("quiet"); shift ;;
        --listen) entry="audition.scd"; shift ;;
        --sound)  forwarded+=("sound" "${2:?--sound needs a value}"); shift 2 ;;
        --repeat) forwarded+=("repeat" "${2:?--repeat needs a value}"); shift 2 ;;
        --out)    forwarded+=("out" "${2:?--out needs a value}"); shift 2 ;;
        *)        echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

sclang="$(find_supercollider)"
echo "Using ${sclang}"

# `timeout` returns 124 when it had to kill the process; turn that into a
# message rather than a bare exit code, since the cause is always the same.
set +e
# The ${arr[@]+...} form expands to nothing at all when the array is empty,
# which a bare "${arr[@]}" does not do under `set -u`.
timeout "${timeout_seconds}" "${sclang}" "${script_directory}/${entry}" \
    ${forwarded[@]+"${forwarded[@]}"}
status=$?
set -e

if [[ ${status} -eq 124 ]]; then
    echo "SuperCollider did not finish within ${timeout_seconds}s. A script error leaves sclang running; check the output above for an ERROR line." >&2
fi

exit ${status}
