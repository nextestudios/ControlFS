# 0002 — Binding SDL3 e modelo de thread

- **Data:** 2026-09-26
- **Estado:** aceita (validação em Windows pendente)

## Contexto

A especificação pede SDL3 como backend único, sem janela SDL, respeitando exigências de thread principal.

## Alternativas avaliadas

| Pacote | Licença | Nativos incluídos | Situação |
|---|---|---|---|
| `ppy.SDL3-CS` 2026.722.0 | MIT | win-x64, win-x86, win-arm64, osx, linux, ios, android | **Escolhido**: mantido pela equipe do osu!, nativos no pacote, API gerada dos cabeçalhos oficiais. |
| `SDL3-CS` 3.4.16.1 (edwardgushchin) | arquivo de licença | a verificar | Não escolhido: não havia necessidade de dois bindings; nativos não inspecionados. |
| Interop próprio | — | — | Desnecessário enquanto um binding mantido existir. |

API confirmada por reflexão (não por suposição): `SDL_Init(SDL_InitFlags)`, `SDL_PollEvent(SDL_Event*)`, `SDL_OpenGamepad`,
`SDL_IsGamepad`, `SDL_GetGamepadName/Vendor/Product/Path/Type`, `SDL_GetJoystickGUIDForID`, `SDL_IsJoystickVirtual`,
`SDL_SetHint(Utf8String, Utf8String)`, estruturas `gbutton/gaxis/gdevice/jdevice` e enums `SDL_GamepadButton` por posição
(`SOUTH/EAST/WEST/NORTH`).

## Decisão de thread

- `SDL_Init` e `SDL_PollEvent` ocorrem **na thread de UI do WinUI**, por um `DispatcherQueueTimer` (8 ms ativo, 120 ms em
  segundo plano). Nada de `Task.Run` para SDL.
- `SDL_HINT_JOYSTICK_THREAD=1`: no Windows o SDL processa mensagens de joystick em thread própria, sem depender do laço de
  mensagens do WinUI. **Hipótese a validar no Windows.**
- `SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS=1` explícito: sem janelas SDL o SDL não sabe se o app tem foco. O foco é
  decidido pela aplicação (`Window.Activated` → `InputRouter.Suspend/Resume`). Resultado: nenhum comando é roteado para a
  UI com o app em segundo plano.

## Evidência

- `tools/ControlFS.InputProbe` executado no macOS arm64: SDL 3.5.0 inicializa, bombeia eventos por 3 s e encerra;
  0 dispositivos (nenhum controle conectado). **Não prova** comportamento de hardware nem de Windows.

## Pendências

- Executar o probe e o app em Windows 11 x64 com controles reais (matriz em `docs/controller-compatibility.md`).
- Joysticks sem perfil de gamepad publicam eventos crus; comandam a UI depois do assistente de mapeamento (#79).
