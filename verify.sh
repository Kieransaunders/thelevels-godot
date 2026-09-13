#!/bin/zsh
# P0+ verification gate: build, test, headless import, headless run.
# Usage: ./verify.sh [frame-count]
set -euo pipefail

export DOTNET_ROOT="${DOTNET_ROOT:-/Volumes/External/DevExteralHD/dotnet}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/Volumes/External/DevExteralHD/nuget}"
export PATH="$DOTNET_ROOT:$PATH"
THELEVELS_GODOT_BIN="${THELEVELS_GODOT_BIN:-$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot}"
FRAMES="${1:-120}"

echo "== dotnet build =="
dotnet build TheLevels.sln

echo "== dotnet test =="
dotnet test Tests/TheLevels.Tests.csproj --no-build --logger 'trx;LogFileName=port-tests.trx'

echo "== godot headless import =="
"$THELEVELS_GODOT_BIN" --headless --path . --import >/dev/null

echo "== godot headless run ($FRAMES frames) =="
# The parity gates were measured against the bare RaisedWay map, so they run on --level=sandbox.
"$THELEVELS_GODOT_BIN" --headless --path . --quit-after "$FRAMES" -- --level=sandbox \
    --verify-p3 --verify-p4 --verify-p5 --verify-p6 --verify-p7 --verify-p8 --verify-villagers

# Every level in the catalogue boots, renders and resets. Adding a level adds a run here
# automatically -- the list comes from the build, not from this script.
echo "== per-level boot gates =="
LEVELS=$("$THELEVELS_GODOT_BIN" --headless --path . --quit-after 5 -- --list-levels \
    | awk '/^level /{print $2}')
if [[ -z "$LEVELS" ]]; then echo "no levels reported by --list-levels" >&2; exit 1; fi
for level in ${(f)LEVELS}; do
    echo "-- $level"
    "$THELEVELS_GODOT_BIN" --headless --path . --quit-after "$FRAMES" -- --level="$level" --verify-level
done

echo "== level hop gate (L) =="
"$THELEVELS_GODOT_BIN" --headless --path . --quit-after "$FRAMES" -- --verify-level-hop

echo "== first crossing mission gate =="
"$THELEVELS_GODOT_BIN" --headless --path . --quit-after "$FRAMES" -- --verify-level-one

echo "== verify.sh OK =="
