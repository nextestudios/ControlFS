<#
.SYNOPSIS
  Publica o ControlFS (win-x64, self-contained) e gera em dist/:
  ControlFS-Setup-x64.exe (instalador por usuário), ControlFS-Portable-x64.zip e SHA256SUMS.txt.
.EXAMPLE
  .\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.2
  .\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.2 -Target Portable
#>
param(
    [ValidateSet("x64")] [string] $Runtime = "x64",
    [Parameter(Mandatory = $true)] [string] $Version,
    [ValidateSet("All", "Portable", "Installer")] [string] $Target = "All"
)
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$') { throw "Versão inválida: $Version" }

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts/publish-$Runtime"
$dist = Join-Path $root "dist"
Remove-Item -LiteralPath $out, $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $dist | Out-Null

# O RID vem do próprio ControlFS.App.csproj: passar -r aqui propagaria o RID às bibliotecas e quebraria o restore travado.
# Versão numérica para o assembly (sem sufixo de pré-release).
$numeric = ($Version -split '-')[0]
dotnet publish (Join-Path $root "src/ControlFS.App/ControlFS.App.csproj") -c Release --self-contained true `
    -p:Platform=$Runtime -p:Version=$Version -p:AssemblyVersion="$numeric.0" -p:FileVersion="$numeric.0" `
    -p:DebugType=none -p:DebugSymbols=false -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

foreach ($required in "ControlFS.exe", "SDL3.dll", "SharpCompress.dll") {
    if (-not (Test-Path -LiteralPath (Join-Path $out $required))) { throw "Pacote incompleto: falta $required" }
}
Copy-Item -LiteralPath (Join-Path $root "LICENSE"), (Join-Path $root "THIRD_PARTY_NOTICES.md") -Destination $out

$files = @()
if ($Target -in "All", "Portable") {
    $zip = Join-Path $dist "ControlFS-Portable-$Runtime.zip"
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
    $files += $zip
}
if ($Target -in "All", "Installer") {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) não encontrado." }
    & $iscc /Q "/DAppVersion=$Version" "/DNumericVersion=$numeric.0" "/DSourceDir=$out" "/DOutputDir=$dist" (Join-Path $PSScriptRoot "ControlFS.iss")
    if ($LASTEXITCODE -ne 0) { throw "ISCC falhou" }
    $files += Join-Path $dist "ControlFS-Setup-$Runtime.exe"
}

# LF explícito: "sha256sum -c" / "shasum -c" falham com CRLF no nome do arquivo.
$lines = foreach ($f in $files) { "$((Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path -Leaf $f)" }
[IO.File]::WriteAllText((Join-Path $dist "SHA256SUMS.txt"), (($lines -join "`n") + "`n"), [Text.Encoding]::ASCII)
$lines | ForEach-Object { Write-Host "Gerado: $_" }
