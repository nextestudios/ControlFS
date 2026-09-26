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

`.github/workflows/ci.yml`: Windows (restore travado por lock files, build, testes unitários e de integração Windows,
listagem de pacotes vulneráveis, publish portátil como artefato) e Linux (núcleo portátil). **A CI ainda não foi
executada** — o repositório não tem remoto.
