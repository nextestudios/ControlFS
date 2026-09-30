<#
.SYNOPSIS
  Gera os manifestos do WinGet (#273) de uma release publicada do ControlFS e, onde o winget existir, valida.
  Não envia nada para o repositório da Microsoft: o envio é um passo manual e consciente (docs/build-and-release.md).
.EXAMPLE
  .\build\New-WingetManifest.ps1 -Tag v0.12.0-alpha.1 -OutDir artifacts\winget
#>
param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$OutDir = "artifacts\winget",
    [string]$Repo = "nextestudios/ControlFS"
)
$ErrorActionPreference = "Stop"

$version = $Tag.TrimStart("v")
$installerName = "ControlFS-Setup-x64.exe"
$url = "https://github.com/$Repo/releases/download/$Tag/$installerName"

# O hash é do arquivo que a release realmente publica (nunca de uma compilação local): baixa e calcula.
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("controlfs-winget-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    $download = Join-Path $work $installerName
    Invoke-WebRequest -Uri $url -OutFile $download -UseBasicParsing
    $sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $download).Hash.ToUpperInvariant()
}
finally {
    Remove-Item -Recurse -Force -LiteralPath $work -ErrorAction SilentlyContinue
}

$id = "nextestudios.ControlFS"
$schema = "1.9.0"
$date = (Get-Date).ToString("yyyy-MM-dd")
$dir = Join-Path $OutDir "manifests\n\nextestudios\ControlFS\$version"
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$header = "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.{0}.{1}.schema.json"

@"
$($header -f "version", $schema)
PackageIdentifier: $id
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@ | Set-Content -Encoding utf8 (Join-Path $dir "$id.yaml")

@"
$($header -f "installer", $schema)
PackageIdentifier: $id
PackageVersion: $version
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
MinimumOSVersion: 10.0.19041.0
ReleaseDate: $date
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@ | Set-Content -Encoding utf8 (Join-Path $dir "$id.installer.yaml")

@"
$($header -f "defaultLocale", $schema)
PackageIdentifier: $id
PackageVersion: $version
PackageLocale: en-US
Publisher: nextestudios
PublisherUrl: https://github.com/$Repo
PublisherSupportUrl: https://github.com/$Repo/issues
PackageName: ControlFS
PackageUrl: https://github.com/$Repo
License: AGPL-3.0-only
LicenseUrl: https://github.com/$Repo/blob/main/LICENSE
ShortDescription: Controller-first file manager for Windows.
Description: ControlFS is a native Windows file manager you can drive entirely with a game controller (or keyboard and mouse), with tabs, dual pane, archives, previews and a built-in media player.
Moniker: controlfs
Tags:
- file-manager
- gamepad
- controller
- explorer
- archive
ReleaseNotesUrl: https://github.com/$Repo/releases/tag/$Tag
ManifestType: defaultLocale
ManifestVersion: $schema
"@ | Set-Content -Encoding utf8 (Join-Path $dir "$id.locale.en-US.yaml")

Write-Host "Manifestos em $dir (SHA-256 do instalador: $sha)"

if (Get-Command winget -ErrorAction SilentlyContinue) {
    winget validate --manifest $dir
    if ($LASTEXITCODE -ne 0) { throw "winget validate falhou ($LASTEXITCODE)" }
} else {
    Write-Warning "winget não encontrado: manifestos gerados, mas não validados."
}
