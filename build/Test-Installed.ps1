<#
.SYNOPSIS
  Instala o ControlFS em silêncio numa pasta temporária, confere os arquivos, abre o app instalado
  (Test-Startup.ps1) e desinstala. Falha se qualquer etapa falhar. Usado pela CI.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Setup,
    [Parameter(Mandatory = $true)] [string] $OutDir
)
$ErrorActionPreference = "Stop"
$dir = Join-Path $env:RUNNER_TEMP "controlfs-install"
$p = Start-Process -Wait -PassThru -FilePath $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=$dir"
if ($p.ExitCode -ne 0) { throw "Instalação falhou (código $($p.ExitCode))" }
foreach ($f in "ControlFS.exe", "resources.pri", "ControlFS.installed", "SDL3.dll", "SharpCompress.dll", "unins000.exe") {
    if (-not (Test-Path -LiteralPath (Join-Path $dir $f))) { throw "Instalação incompleta: falta $f" }
}
& (Join-Path $PSScriptRoot "Test-Startup.ps1") -Exe (Join-Path $dir "ControlFS.exe") -OutDir $OutDir
$startupOk = $LASTEXITCODE -eq 0
$u = Start-Process -Wait -PassThru -FilePath (Join-Path $dir "unins000.exe") -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
if ($u.ExitCode -ne 0) { throw "Desinstalação falhou (código $($u.ExitCode))" }
Start-Sleep -Seconds 3
if (Test-Path -LiteralPath (Join-Path $dir "ControlFS.exe")) { throw "Desinstalação deixou o executável" }
if (-not $startupOk) { throw "O app instalado não abriu" }
Write-Host "Instalar, abrir e desinstalar: OK"
