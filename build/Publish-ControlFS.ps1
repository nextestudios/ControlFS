<#
.SYNOPSIS
  Publica o ControlFS (WinUI 3, sem pacote MSIX, self-contained) e gera em dist/:
    ControlFS-Portable-x64.exe  um único .exe portátil (dados em ControlFS_Data ao lado dele)
    ControlFS-Setup-x64.exe     instalador por usuário (Inno Setup; dados em %LOCALAPPDATA%\ControlFS)
    SHA256SUMS.txt
.EXAMPLE
  .\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.3
#>
param(
    [ValidateSet("x64")] [string] $Runtime = "x64",
    [Parameter(Mandatory = $true)] [string] $Version,
    [ValidateSet("All", "Portable", "Installer")] [string] $Target = "All"
)
$ErrorActionPreference = "Stop"
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$') { throw "Versão inválida: $Version" }

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/ControlFS.App/ControlFS.App.csproj"
$singleDir = Join-Path $root "artifacts/publish-$Runtime-single"
$appDir = Join-Path $root "artifacts/publish-$Runtime-app"
$dist = Join-Path $root "dist"
Remove-Item -LiteralPath $singleDir, $appDir, $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $dist | Out-Null
$numeric = ($Version -split '-')[0]

function Invoke-Publish([string]$OutDir, [bool]$SingleFile) {
    # O RID vem do próprio ControlFS.App.csproj: passar -r aqui propagaria o RID às bibliotecas e quebraria o restore travado.
    # PublishSingleFile acrescenta implicitamente Microsoft.NET.ILLink.Tasks (do próprio SDK), que os lock files não
    # listam. Só nesta publicação o restore não é travado nem reescreve os lock files; as versões continuam fixadas
    # em Directory.Packages.props e o build da CI segue em modo travado.
    $lockArgs = if ($SingleFile) { @("-p:RestoreLockedMode=false", "-p:RestorePackagesWithLockFile=false") } else { @() }
    dotnet publish $project -c Release --self-contained true `
        -p:Platform=$Runtime "-p:PublishSingleFile=$($SingleFile.ToString().ToLowerInvariant())" `
        -p:Version=$Version -p:AssemblyVersion="$numeric.0" -p:FileVersion="$numeric.0" `
        @lockArgs -o $OutDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou ($OutDir)" }
    if (-not (Test-Path -LiteralPath (Join-Path $OutDir "ControlFS.exe"))) { throw "Publish não gerou ControlFS.exe em $OutDir" }
}

$files = @()
if ($Target -in "All", "Installer") {
    Write-Host "Publicando versão para o instalador (pasta)..."
    Invoke-Publish $appDir $false
    foreach ($required in "ControlFS.exe", "ControlFS.pri", "SDL3.dll", "SharpCompress.dll") {
        if (-not (Test-Path -LiteralPath (Join-Path $appDir $required))) { throw "Pacote incompleto: falta $required" }
    }
    Copy-Item -LiteralPath (Join-Path $root "LICENSE"), (Join-Path $root "THIRD_PARTY_NOTICES.md") -Destination $appDir
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) não encontrado." }
    & $iscc /Q "/DAppVersion=$Version" "/DNumericVersion=$numeric.0" "/DSourceDir=$appDir" "/DOutputDir=$dist" (Join-Path $PSScriptRoot "ControlFS.iss")
    if ($LASTEXITCODE -ne 0) { throw "ISCC falhou" }
    $files += Join-Path $dist "ControlFS-Setup-$Runtime.exe"
}

if ($Target -in "All", "Portable") {
    Write-Host "Publicando versão portátil (um único .exe)..."
    Invoke-Publish $singleDir $true
    $portable = Join-Path $dist "ControlFS-Portable-$Runtime.exe"
    Copy-Item -LiteralPath (Join-Path $singleDir "ControlFS.exe") -Destination $portable
    $files += $portable
}
# LF explícito: "sha256sum -c" / "shasum -c" falham com CRLF no nome do arquivo.
$lines = foreach ($f in $files) { "$((Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path -Leaf $f)" }
[IO.File]::WriteAllText((Join-Path $dist "SHA256SUMS.txt"), (($lines -join "`n") + "`n"), [Text.Encoding]::ASCII)
$lines | ForEach-Object { Write-Host "Gerado: $_" }
