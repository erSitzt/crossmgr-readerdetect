# Publishes both executables as self-contained single-file win-x64 builds into dist\.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet publish src/ReaderDetect.Cli/ReaderDetect.Cli.csproj -c Release -p:PublishProfile=win-x64
dotnet publish src/ReaderDetect.Gui/ReaderDetect.Gui.csproj -c Release -p:PublishProfile=win-x64
Get-ChildItem dist -Recurse -Filter *.exe | Format-Table FullName, Length
