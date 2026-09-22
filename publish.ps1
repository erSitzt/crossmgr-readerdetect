# Publishes both executables as self-contained single-file win-x64 builds into dist\.
# -Version stamps the assemblies (the release workflow passes the tag).
param([string]$Version = "0.0.0-local")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet publish src/ReaderDetect.Cli/ReaderDetect.Cli.csproj -c Release -p:PublishProfile=win-x64 -p:Version=$Version
dotnet publish src/ReaderDetect.Gui/ReaderDetect.Gui.csproj -c Release -p:PublishProfile=win-x64 -p:Version=$Version
Get-ChildItem dist -Recurse -Filter *.exe | Format-Table FullName, Length
