<#
.SYNOPSIS
  Publica o ControlFS portátil (win-x64, self-contained) e gera dist/ControlFS-Portable-x64.zip.
.EXAMPLE
  .\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.1
#>
param(
    [ValidateSet("x64")] [string] $Runtime = "x64",
    [Parameter(Mandatory = $true)] [string] $Version
)
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$') { throw "Versão inválida: $Version" }

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts/publish-$Runtime"
$dist = Join-Path $root "dist"
Remove-Item -LiteralPath $out, $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $dist | Out-Null

# Versão numérica para o assembly (sem sufixo de pré-release).
$numeric = ($Version -split '-')[0]
dotnet publish (Join-Path $root "src/ControlFS.App/ControlFS.App.csproj") -c Release -r "win-$Runtime" --self-contained true `
    -p:Platform=$Runtime -p:Version=$Version -p:AssemblyVersion="$numeric.0" -p:FileVersion="$numeric.0" `
    -p:DebugType=none -p:DebugSymbols=false -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

foreach ($required in "ControlFS.exe", "SDL3.dll", "SharpCompress.dll") {
    if (-not (Test-Path -LiteralPath (Join-Path $out $required))) { throw "Pacote incompleto: falta $required" }
}
Copy-Item -LiteralPath (Join-Path $root "LICENSE"), (Join-Path $root "THIRD_PARTY_NOTICES.md") -Destination $out

$zip = Join-Path $dist "ControlFS-Portable-$Runtime.zip"
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  ControlFS-Portable-$Runtime.zip" | Set-Content -LiteralPath (Join-Path $dist "SHA256SUMS.txt") -Encoding ascii
Write-Host "Gerado: $zip ($hash)"
