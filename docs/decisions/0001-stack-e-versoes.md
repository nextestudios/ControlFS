# 0001 — Stack e versões fixadas

- **Data:** 2026-09-26
- **Estado:** aceita

## Decisão

| Componente | Versão fixada | Onde | Evidência |
|---|---|---|---|
| .NET SDK | 10.0.401 (`global.json`, `rollForward: latestPatch`) | todos | `releases.json` oficial: canal 10.0 com `support-phase: active`, último runtime 10.0.12, último SDK 10.0.401 (consultado em 2026-09-26). |
| Runtime alvo | `net10.0` / `net10.0-windows10.0.19041.0` | projetos | — |
| Windows App SDK | 2.5.1 (componentes WinUI 2.3.9, Foundation 2.3.12, InteractiveExperiences 2.1.9, Base 2.0.4) | App | NuGet: versão estável mais recente. |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2705 | App | NuGet: estável mais recente. |
| SharpCompress | 1.0.0 (commit `b6cc95af`) | Infrastructure.Archives | NuGet, licença MIT. API inspecionada por reflexão nesta sessão. |
| ppy.SDL3-CS | 2026.722.0 (commit `7f836c9f`), SDL nativo 3.5.0 | Infrastructure.Input.Sdl3 | NuGet, licença MIT; `SDL_GetVersion()` executado no macOS arm64 devolveu 3.5.0. |
| xunit.v3 / runner / Test SDK | 3.2.2 / 3.1.5 / 18.10.1 | testes | NuGet. |

Versões são centralizadas em `Directory.Packages.props`; restauração é verificável por `packages.lock.json`
(`RestoreLockedMode` ativo quando `CI=true`).

## Por que não as últimas majors de xUnit (4.x)

Escolha conservadora: xunit.v3 3.2.2 é a linha estável anterior, com runner compatível conhecido. Trocar exige apenas
alterar `Directory.Packages.props` e revalidar.

## Riscos registrados

- O metapacote `Microsoft.WindowsAppSDK` 2.5.1 trazia IA/ML (ONNX Runtime), Widgets, Search e DWriteCore, que o app não
  usa. Desde #85 o app referencia só os componentes (`Microsoft.WindowsAppSDK.WinUI`, `.Foundation`,
  `.InteractiveExperiences`, `.Base`) com as mesmas versões do metapacote 2.5.1. Medido pelo `Publish-ControlFS.ps1` no
  `smoke.yml` (Windows, self-contained): portátil 97,2 MB → 72,2 MB, instalador 65,3 MB → 47,5 MB; o portátil e o
  instalado abriram normalmente. Sem o pacote `Microsoft.WindowsAppSDK.Runtime` o SDK assume self-contained; fora do
  Windows o `.csproj` compila como dependente de framework só para verificação. Ao atualizar o Windows App SDK, use as
  versões de componentes listadas no metapacote novo e meça de novo.
- SharpCompress 1.0.0 é uma major recente. Comportamentos observados nesta sessão estão em `0004`.
