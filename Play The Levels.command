#!/bin/zsh
# Double-click to play The Levels. Requires the installed Godot .NET engine
# (~/Applications/Godot_mono.app) and the local .NET runtime (DOTNET_ROOT).
cd "$(dirname "$0")"
export DOTNET_ROOT="${DOTNET_ROOT:-/Volumes/External/DevExteralHD/dotnet}"
exec "$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot" --path "$PWD"
