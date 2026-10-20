# Publishes Mini Local Chat as one signed exe, ready to spread over the LAN auto-update.
#
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
#
# Bump <Version> in src\MiniLocalChat\MiniLocalChat.csproj first: computers only install a HIGHER version.
# Then run the published exe (publish\MiniLocalChat.exe, with MiniLocalChat.exe.sig next to it) on any
# one computer; the others pick it up from there, and pass it on.
# Signing needs the private key made once with:  dotnet run tools/UpdateKey.cs -- new
param([string]$Output = "publish")

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

dotnet publish src/MiniLocalChat/MiniLocalChat.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Get-ChildItem $Output -Filter *.pdb | Remove-Item  # native debug symbols (~95 MB), not needed to run

dotnet run tools/UpdateKey.cs -- sign (Join-Path $Output "MiniLocalChat.exe")
if ($LASTEXITCODE -ne 0) { throw "signing failed" }

$version = (Get-Item (Join-Path $Output "MiniLocalChat.exe")).VersionInfo.FileVersion
Write-Host "Published and signed version $version in $Output\ (MiniLocalChat.exe + MiniLocalChat.exe.sig)"
