<#
.SYNOPSIS
  Gera dist/release-manifest.json e o assina (ECDSA P-256, SHA-256, IEEE P1363 em base64) em
  dist/release-manifest.json.sig com a chave privada do ambiente UPDATE_SIGNING_KEY (PEM PKCS#8).
  A assinatura é conferida contra a chave pública embutida no app antes de terminar.
  A chave privada nunca é gravada em disco nem impressa.
#>
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $Repository = "nextestudios/ControlFS"
)
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($env:UPDATE_SIGNING_KEY)) { throw "UPDATE_SIGNING_KEY ausente: o manifesto não pode ser assinado." }
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"

function Get-Asset([string]$name) {
    $path = Join-Path $dist $name
    if (-not (Test-Path -LiteralPath $path)) { throw "Arquivo ausente: $name" }
    [ordered]@{ name = $name; size = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
}

$manifest = [ordered]@{
    schema = 1
    product = "ControlFS"
    repository = $Repository
    version = $Version
    releasedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    installer = Get-Asset "ControlFS-Setup-x64.exe"
    portable = Get-Asset "ControlFS-Portable-x64.exe"
}
$json = $manifest | ConvertTo-Json -Compress -Depth 4
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
[IO.File]::WriteAllBytes((Join-Path $dist "release-manifest.json"), $bytes)

$key = [Security.Cryptography.ECDsa]::Create()
try {
    $key.ImportFromPem($env:UPDATE_SIGNING_KEY)
    $signature = $key.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
} finally { $key.Dispose() }
[IO.File]::WriteAllText((Join-Path $dist "release-manifest.json.sig"), [Convert]::ToBase64String($signature) + "`n", [Text.Encoding]::ASCII)

# Confere com as chaves públicas que o app usa de verdade (evita publicar com um secret trocado). Durante uma rotação
# (docs/decisions/0006) o app confia em mais de uma chave; a assinatura precisa conferir com uma delas.
$trust = Get-Content -LiteralPath (Join-Path $root "src/ControlFS.Infrastructure.Updates/UpdateTrust.cs") -Raw
$pems = @([regex]::Matches($trust, "-----BEGIN PUBLIC KEY-----[\s\S]+?-----END PUBLIC KEY-----") | ForEach-Object { $_.Value -replace '(?m)^\s+', '' })
if (-not $pems) { throw "Nenhuma chave pública encontrada em UpdateTrust.cs." }
$signedBy = $null
foreach ($pem in $pems) {
    $public = [Security.Cryptography.ECDsa]::Create()
    try {
        $public.ImportFromPem($pem)
        if ($public.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)) {
            $signedBy = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($public.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
            break
        }
    } finally { $public.Dispose() }
}
if (-not $signedBy) { throw "A assinatura não confere com nenhuma chave pública do app (UpdateTrust.cs). Secret incorreto?" }
Write-Host "Assinado pela chave $signedBy ($($pems.Count) chave(s) confiável(is) no app)."
Write-Host "Manifesto assinado e conferido: $json"
