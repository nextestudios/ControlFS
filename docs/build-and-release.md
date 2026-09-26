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

- Implantação: não empacotada (`WindowsPackageType=None`), Windows App SDK self-contained, .NET self-contained.
  **Precisa ser demonstrada em máquina limpa** antes de anunciar "portátil".
- Nativos: `SDL3.dll` (win-x64) vem do pacote `ppy.SDL3-CS` e é copiado para a saída (verificado no build).
- Manifesto: `asInvoker` (nunca pede elevação), PerMonitorV2, `longPathAware`.
- Sem assinatura de código: o artefato da CI chama-se `...-nao-assinado`. Instalador/MSIX somente com estratégia de
  assinatura definida. Nunca instruir usuários a desabilitar proteções ou importar certificados-raiz.

## CI

- `ci.yml` (PR e `main`): Windows — restore travado por lock files, build de toda a solução (inclui o app WinUI), testes
  unitários/jornadas, testes de integração Windows e checagem de pacotes vulneráveis; Linux — núcleo portátil.
  Primeira execução (2026-09-26): verde; 133 + 6 testes no Windows, 133 no Linux.
- `release.yml`: push de tag `vX.Y.Z` ou `vX.Y.Z-pre.N` roda os testes, gera `ControlFS-Portable-x64.zip` +
  `SHA256SUMS.txt` com `build/Publish-ControlFS.ps1` e publica a release (pré-lançamento quando há sufixo). As notas vêm
  de `CHANGELOG.en-US.md`, com link para `CHANGELOG.md`; o workflow falha se faltar a seção em algum dos dois.
- Release inclui `ControlFS-Setup-x64.exe` (Inno Setup, por usuário) e `ControlFS-Portable-x64.exe` (arquivo único).
  Antes de publicar, a CI **abre de verdade** o portátil e o app instalado (`build/Test-Startup.ps1`,
  `build/Test-Installed.ps1`: processo vivo, janela, eventos do Windows, logs e print). O workflow `smoke.yml` faz o
  mesmo em PRs, na `main` e sob demanda para uma release já publicada. Inclui também e `release-manifest.json` + `.sig` assinados com o secret `UPDATE_SIGNING_KEY`
  (`build/New-ReleaseManifest.ps1` confere a assinatura com a chave pública do app antes de publicar).
- `codeql.yml` (C# e workflows) e `dependabot.yml` (NuGet e Actions, mensal, agrupado).

Para publicar: adicione a seção da versão nos dois changelogs, faça merge na `main` e crie a tag (somente mantenedores).
