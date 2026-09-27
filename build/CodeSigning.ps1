<#
.SYNOPSIS
  Assinatura Authenticode das releases (#84). Carregado com ". .\build\CodeSigning.ps1" pelo Publish-ControlFS.ps1
  (-Sign) e pelo release.yml (verificação). Nada de certificado no repositório: tudo vem de secrets do GitHub Actions.

  Provedores, escolhidos pelas variáveis de ambiente presentes (ver docs/build-and-release.md → "Assinatura de código"):
    artifact  Microsoft Artifact Signing (antigo Trusted Signing): ARTIFACT_SIGNING_ENDPOINT, ARTIFACT_SIGNING_ACCOUNT,
              ARTIFACT_SIGNING_PROFILE + AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET.
    pfx       Certificado .pfx: SIGNING_PFX_BASE64, SIGNING_PFX_PASSWORD (opcional SIGNING_TIMESTAMP_URL).
    none      Nada configurado: não assina (o release.yml avisa e publica sem assinatura, como antes).
#>

# Cliente do Artifact Signing (dlib do signtool), baixado do nuget.org e conferido pelo SHA-512 antes de usar.
$script:ArtifactSigningClientVersion = "1.0.128"
$script:ArtifactSigningClientSha512 = "98f06a691f4fc2fa22f19dcf8556733e98607fbef91a312c453b9b0798cc9088dae0acb36e389b552a11b4d2320324785b8541c2b51091a724c05bc5df5cbf95"
$script:SecretVariables = "AZURE_CLIENT_SECRET", "SIGNING_PFX_BASE64", "SIGNING_PFX_PASSWORD"

function Get-CodeSigningMode {
    $artifact = "ARTIFACT_SIGNING_ENDPOINT", "ARTIFACT_SIGNING_ACCOUNT", "ARTIFACT_SIGNING_PROFILE", "AZURE_TENANT_ID", "AZURE_CLIENT_ID", "AZURE_CLIENT_SECRET"
    $missing = @($artifact | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
    if ($missing.Count -eq 0) { return "artifact" }
    # Metade configurada é erro (melhor falhar do que publicar sem assinatura sem perceber).
    if (@("ARTIFACT_SIGNING_ENDPOINT", "ARTIFACT_SIGNING_ACCOUNT", "ARTIFACT_SIGNING_PROFILE", "AZURE_CLIENT_SECRET" | Where-Object { $_ -notin $missing }).Count -gt 0) {
        throw "Artifact Signing configurado pela metade; faltam: $($missing -join ', ')"
    }
    $hasPfx = -not [string]::IsNullOrWhiteSpace($env:SIGNING_PFX_BASE64)
    $hasPassword = -not [string]::IsNullOrWhiteSpace($env:SIGNING_PFX_PASSWORD)
    if ($hasPfx -and $hasPassword) { return "pfx" }
    if ($hasPfx -or $hasPassword) { throw "Assinatura com .pfx precisa de SIGNING_PFX_BASE64 e SIGNING_PFX_PASSWORD." }
    return "none"
}

function Find-SignTool {
    $candidates = @(Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\signtool.exe" -ErrorAction SilentlyContinue) +
        @(Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools\*\bin\10.*\x64\signtool.exe") -ErrorAction SilentlyContinue)
    $best = $candidates | Sort-Object { [version]($_.Directory.Parent.Name) } -Descending | Select-Object -First 1
    if (-not $best) { throw "signtool.exe (Windows SDK) não encontrado." }
    return $best.FullName
}

function Get-ArtifactSigningDlib([string]$WorkDir) {
    $package = Join-Path $WorkDir "artifactsigning.nupkg"
    $url = "https://www.nuget.org/api/v2/package/Microsoft.ArtifactSigning.Client/$script:ArtifactSigningClientVersion"
    Invoke-WebRequest -Uri $url -OutFile $package -UseBasicParsing
    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA512).Hash.ToLowerInvariant()
    if ($hash -ne $script:ArtifactSigningClientSha512) { throw "Microsoft.ArtifactSigning.Client $script:ArtifactSigningClientVersion: SHA-512 inesperado ($hash)." }
    $dir = Join-Path $WorkDir "artifactsigning"
    Expand-Archive -LiteralPath $package -DestinationPath $dir -Force
    $dlib = Join-Path $dir "bin\x64\Azure.CodeSigning.Dlib.dll"
    if (-not (Test-Path -LiteralPath $dlib)) { throw "Azure.CodeSigning.Dlib.dll ausente no pacote." }
    return $dlib
}

<#
  Prepara a assinatura e tira os secrets do ambiente do processo: dotnet publish, ISCC e demais filhos não os herdam.
  Eles voltam só durante cada chamada ao signtool (o dlib do Artifact Signing lê AZURE_* do ambiente).
#>
function Initialize-CodeSigning {
    $mode = Get-CodeSigningMode
    if ($mode -eq "none") { throw "Assinatura pedida (-Sign), mas nenhum provedor configurado (ver docs/build-and-release.md)." }
    $work = Join-Path ([IO.Path]::GetTempPath()) ("controlfs-signing-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $work | Out-Null
    $config = [ordered]@{ Mode = $mode; SignTool = (Find-SignTool); WorkDir = $work; Secrets = @{} }
    foreach ($name in $script:SecretVariables) {
        $config.Secrets[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    if ($mode -eq "artifact") {
        $config.Dlib = Get-ArtifactSigningDlib $work
        $config.Metadata = Join-Path $work "metadata.json"
        [ordered]@{
            Endpoint = $env:ARTIFACT_SIGNING_ENDPOINT
            CodeSigningAccountName = $env:ARTIFACT_SIGNING_ACCOUNT
            CertificateProfileName = $env:ARTIFACT_SIGNING_PROFILE
        } | ConvertTo-Json | Set-Content -LiteralPath $config.Metadata -Encoding ascii
        $config.Timestamp = "http://timestamp.acs.microsoft.com"
    } else {
        $config.Pfx = Join-Path $work "signing.pfx"
        [IO.File]::WriteAllBytes($config.Pfx, [Convert]::FromBase64String($config.Secrets["SIGNING_PFX_BASE64"]))
        $config.Secrets["SIGNING_PFX_BASE64"] = $null
        $config.Timestamp = if ($env:SIGNING_TIMESTAMP_URL) { $env:SIGNING_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }
    }
    Write-Host "Assinatura de código: provedor '$mode', signtool $($config.SignTool)"
    return $config
}

function Invoke-CodeSigning($Config, [string[]]$Files) {
    $signArgs = [System.Collections.Generic.List[string]]@("sign", "/v", "/fd", "SHA256", "/tr", $Config.Timestamp, "/td", "SHA256")
    if ($Config.Mode -eq "artifact") {
        $signArgs.AddRange([string[]]@("/dlib", $Config.Dlib, "/dmdf", $Config.Metadata))
        $env:AZURE_CLIENT_SECRET = $Config.Secrets["AZURE_CLIENT_SECRET"]
    } else {
        $signArgs.AddRange([string[]]@("/f", $Config.Pfx, "/p", $Config.Secrets["SIGNING_PFX_PASSWORD"]))
    }
    $signArgs.AddRange($Files)
    try {
        # Saída do signtool sem ecoar a linha de comando (que teria a senha do .pfx).
        & $Config.SignTool @signArgs 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw "signtool sign falhou (código $LASTEXITCODE)." }
    } finally {
        $env:AZURE_CLIENT_SECRET = $null
    }
    Assert-Authenticode $Files
}

function Remove-CodeSigning($Config) {
    if ($Config -and $Config.WorkDir) { Remove-Item -LiteralPath $Config.WorkDir -Recurse -Force -ErrorAction SilentlyContinue }
}

# Assinatura Authenticode válida, com carimbo de tempo, em cada arquivo (também usada pelo release.yml antes de publicar).
function Assert-Authenticode([string[]]$Files) {
    foreach ($file in $Files) {
        $sig = Get-AuthenticodeSignature -LiteralPath $file
        if ($sig.Status -ne "Valid") { throw "Assinatura inválida em $(Split-Path -Leaf $file): $($sig.Status) $($sig.StatusMessage)" }
        if (-not $sig.TimeStamperCertificate) { throw "Assinatura sem carimbo de tempo em $(Split-Path -Leaf $file)." }
        Write-Host "Authenticode OK: $(Split-Path -Leaf $file) — $($sig.SignerCertificate.Subject) (válido até $($sig.SignerCertificate.NotAfter.ToString('yyyy-MM-dd')))"
    }
}
