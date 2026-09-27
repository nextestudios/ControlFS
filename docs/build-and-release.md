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
- Sem assinatura de código: o artefato da CI chama-se `...-nao-assinado`. Instalador/MSIX somente com estratégia de
  assinatura definida. Nunca instruir usuários a desabilitar proteções ou importar certificados-raiz.

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
