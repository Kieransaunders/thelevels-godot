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
"$THELEVELS_GODOT_BIN" --headless --path . --quit-after "$FRAMES" -- --verify-p3 --verify-p4 --verify-p5 --verify-p6

echo "== verify.sh OK =="
