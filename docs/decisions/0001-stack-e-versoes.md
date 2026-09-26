# 0001 — Stack e versões fixadas

- **Data:** 2026-09-26
- **Estado:** aceita

## Decisão

| Componente | Versão fixada | Onde | Evidência |
|---|---|---|---|
| .NET SDK | 10.0.401 (`global.json`, `rollForward: latestPatch`) | todos | `releases.json` oficial: canal 10.0 com `support-phase: active`, último runtime 10.0.12, último SDK 10.0.401 (consultado em 2026-09-26). |
| Runtime alvo | `net10.0` / `net10.0-windows10.0.19041.0` | projetos | — |
| Windows App SDK | 2.5.1 (metapacote) | App | NuGet: versão estável mais recente. |
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

- O metapacote `Microsoft.WindowsAppSDK` 2.5.1 traz componentes de IA/ML (incl. ONNX Runtime) que o app não usa.
  Isso aumenta o pacote portátil. Próximo passo: referenciar apenas os componentes necessários (WinUI, Foundation, Base)
  e medir o tamanho publicado — **não feito**.
- SharpCompress 1.0.0 é uma major recente. Comportamentos observados nesta sessão estão em `0004`.
