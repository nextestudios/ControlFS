# Build e distribuição

## Pré-requisitos

- .NET SDK 10.0.401 (fixado em `global.json`; patches mais novos da linha 10.0.4xx são aceitos).
- Windows 11 x64 para compilar/empacotar o app WinUI por completo.

## Comandos (executados nesta sessão no macOS arm64)

```bash
dotnet restore ControlFS.slnx
dotnet build ControlFS.slnx          # compila tudo, inclusive o app WinUI (sem PRI/manifesto fora do Windows)
dotnet test ControlFS.slnx           # 133 aprovados; 6 pulados (exclusivos do Windows)
dotnet run --project tools/ControlFS.InputProbe -- 3
```

Fora do Windows o app **compila mas não executa**: `makepri.exe` e `mt.exe` são pulados por condição de MSBuild.

## Windows (não executado nesta sessão)

```powershell
dotnet build ControlFS.slnx -c Release
dotnet test ControlFS.slnx -c Release
dotnet publish src/ControlFS.App -c Release -r win-x64 --self-contained true -o artifacts/ControlFS-win-x64
artifacts\ControlFS-win-x64\ControlFS.exe
```

- Implantação: não empacotada (`WindowsPackageType=None`), Windows App SDK self-contained (só os componentes usados, ver
  `docs/decisions/0001`), .NET self-contained. O `Publish-ControlFS.ps1` imprime o tamanho de cada pacote no log.
  **Precisa ser demonstrada em máquina limpa** antes de anunciar "portátil".
- Nativos: `SDL3.dll` (win-x64) vem do pacote `ppy.SDL3-CS` e é copiado para a saída (verificado no build).
- Manifesto: `asInvoker` (nunca pede elevação), PerMonitorV2, `longPathAware`.
- Assinatura de código: o `release.yml` já assina quando há secrets configurados (ver "Assinatura de código" abaixo);
  até lá as releases saem sem assinatura. Nunca instruir usuários a desabilitar proteções ou importar certificados-raiz.

## CI

- `ci.yml` (PR e `main`): Windows — restore travado por lock files, build de toda a solução (inclui o app WinUI), testes
  unitários/jornadas, testes de integração Windows e checagem de pacotes vulneráveis.
  Primeira execução (2026-09-26): verde; 133 + 6 testes no Windows. (Um job extra em Linux existiu até 2026-09-27 e
  foi removido: o produto é só para Windows e os mesmos testes já rodam lá.)
- `release.yml`: push de tag `vX.Y.Z` ou `vX.Y.Z-pre.N` roda os testes, gera `ControlFS-Portable-x64.zip` +
  o manifesto assinado com `build/Publish-ControlFS.ps1` e publica a release (como release normal, marcada como "Latest"). As notas vêm
  de `CHANGELOG.en-US.md`, com link para `CHANGELOG.md`; o workflow falha se faltar a seção em algum dos dois.
- Release inclui `ControlFS-Setup-x64.exe` (Inno Setup, por usuário) e `ControlFS-Portable-x64.exe` (arquivo único).
  Antes de publicar, a CI **abre de verdade** o portátil e o app instalado (`build/Test-Startup.ps1`,
  `build/Test-Installed.ps1`: processo vivo, janela, eventos do Windows, logs e print). O workflow `smoke.yml` faz o
  mesmo em PRs, na `main` e sob demanda para uma release já publicada. Inclui também e `release-manifest.json` + `.sig` assinados com o secret `UPDATE_SIGNING_KEY`
  (`build/New-ReleaseManifest.ps1` confere a assinatura com a chave pública do app antes de publicar).
- Capturas de layout: o `smoke.yml` (build do commit) também roda o portátil com `--render-screens <pasta>`, um modo
  só de desenvolvimento que monta as telas reais em 1280×720, 1280×800, 1920×1080 e 3840×2160 (várias escalas), gera
  a galeria dos glifos dos botões e fecha; os PNGs saem no artefato `smoke-screens` (ver `docs/TESTING.md`). Sem o
  argumento o app se comporta normalmente; com ele usa só uma pasta temporária e não acessa a rede.
- Testes de UI Automation: o `smoke.yml` também roda `build/Test-UiAutomation.ps1` no portátil (foco, confirmação com
  foco na opção segura, escopo do modal, teclado virtual; ver `docs/TESTING.md`). Não há workflow novo nem gatilho novo.
- Velocidade: CI e smoke usam cache de pacotes NuGet (chave nos lock files) e não rodam em mudanças só de documentação
  (`**/*.md`, `docs/**`). A release não usa cache (pacotes sempre baixados na hora). O teste dos arquivos já
  publicados (`smoke.yml` com `release_tag`) roda em versões estáveis ou sob demanda — a release já abre o app antes
  de publicar.
- Desenvolvimento: `dotnet build ControlFS.slnx` local serve só como checagem rápida de compilação; testes, abertura do
  app, pacotes e releases valem apenas quando executados na CI.
- `codeql.yml` (C# e workflows) e `dependabot.yml` (NuGet e Actions, mensal, agrupado).

Releases só quando o mantenedor pede (PRs são mergeados sem publicar). Versões `0.x.y-alpha.N` até a 1.0, publicadas como release normal (aparece como "Latest"), com dois downloads (`ControlFS-Setup-x64.exe`, `ControlFS-Portable-x64.exe`) mais o manifesto assinado de atualização. Para publicar antes da 1.0: PRs de feature já estão na `main`; adicione a seção da versão nos dois changelogs num PR para `main` e crie a tag `vX.Y.Z` no merge. A partir da 1.0 (Gitflow completo): crie `release/X.Y.Z` a partir de `develop`, adicione a seção da versão nos dois changelogs, abra PR para `main`, crie a tag `vX.Y.Z` no merge em `main` e faça merge de `main` de volta em `develop` (somente mantenedores). O workflow de release recusa tags fora da `main`.

## Assinatura de código (#84)

O `release.yml` assina, **só em builds de tag**, o `ControlFS.exe` do instalador (antes de empacotar), o
`ControlFS-Setup-x64.exe` e o `ControlFS-Portable-x64.exe`, com carimbo de tempo, antes do `SHA256SUMS.txt` e do
manifesto de atualização. Depois do teste de abertura, o passo **Verify Authenticode signatures** confere
(`Get-AuthenticodeSignature`) que os três têm assinatura válida e carimbo de tempo; se não, a release não é publicada.
Sem nenhum secret configurado, o passo "Code signing mode" deixa um aviso e a release sai sem assinatura, como antes.
Configuração pela metade faz a release falhar (melhor do que publicar sem assinatura sem perceber).

A lógica fica em `build/CodeSigning.ps1` (usado por `Publish-ControlFS.ps1 -Sign`). O script tira os secrets do
ambiente antes do `dotnet publish` e do Inno Setup e só os devolve para o `signtool`. Nenhum certificado, senha ou
chave fica no repositório. Validado uma vez no Smoke (branch do #84, commit temporário revertido) com um certificado
autoassinado de teste confiado no runner: os três arquivos foram assinados, passaram no `Assert-Authenticode`, e o
portátil e o app instalado assinados abriram normalmente. A assinatura com um certificado real depende do mantenedor.

### Aprovação manual

O job de release usa o ambiente do GitHub **`release`** (criado sozinho na primeira execução). Em Settings →
Environments → `release`: marque **Required reviewers** (o mantenedor) para cada release esperar aprovação, e em
"Deployment branches and tags" permita as tags `v*` e a branch `main` (o build manual sem publicar roda na `main`).
Coloque os secrets de assinatura **nesse ambiente** (não no repositório): só o job aprovado os recebe.

### Opção A: Microsoft Artifact Signing (antigo Trusted Signing), recomendada

1. No Azure: crie uma conta **Artifact Signing** e faça a validação de identidade (pessoa física ou organização).
   Crie um **perfil de certificado** "Public Trust".
2. Crie um registro de aplicativo (service principal) no Microsoft Entra ID, gere um segredo de cliente e dê a ele o
   papel **Artifact Signing Certificate Profile Signer** (antigo "Trusted Signing Certificate Profile Signer") na conta.
3. No ambiente `release` do GitHub:
   - Variables: `ARTIFACT_SIGNING_ENDPOINT` (ex.: `https://eus.codesigning.azure.net`, a região da conta),
     `ARTIFACT_SIGNING_ACCOUNT` (nome da conta), `ARTIFACT_SIGNING_PROFILE` (nome do perfil).
   - Secrets: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`.

O workflow baixa `Microsoft.ArtifactSigning.Client` (versão e SHA-512 fixados em `build/CodeSigning.ps1`), instala o
runtime do .NET 8 que ele exige e assina com `signtool /dlib`, carimbo `http://timestamp.acs.microsoft.com`.

### Opção B: certificado .pfx (ex.: de um programa de assinatura para código aberto)

No ambiente `release`: secrets `SIGNING_PFX_BASE64` (o `.pfx` em Base64:
`[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))`) e `SIGNING_PFX_PASSWORD`. Opcional: variable
`SIGNING_TIMESTAMP_URL` (padrão `http://timestamp.digicert.com`). Certificados em token/HSM que não exportam `.pfx`
precisam da opção A ou de outro provedor.

### Depois de configurar

Na próxima release, confira no log "Release será assinada (provedor: …)" e as linhas "Authenticode OK", e depois
atualize `docs/CODE_SIGNING.md` (que ainda diz "ainda não têm assinatura"). O desinstalador do Inno Setup
(`unins000.exe`) não é assinado nesta etapa.

