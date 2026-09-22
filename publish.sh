#!/usr/bin/env bash
# Publishes both executables as self-contained single-file win-x64 builds into dist/.
# The GUI can be built here but only runs on Windows.
set -euo pipefail
cd "$(dirname "$0")"
dotnet publish src/ReaderDetect.Cli/ReaderDetect.Cli.csproj -c Release -p:PublishProfile=win-x64
dotnet publish src/ReaderDetect.Gui/ReaderDetect.Gui.csproj -c Release -p:PublishProfile=win-x64
ls -la dist/*/
