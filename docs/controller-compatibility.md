# Compatibilidade de controles

**Nenhum controle físico foi testado até agora.** Esta tabela é a matriz-alvo; "não testado" não é aprovação.

| Modelo | Transporte | Modo/firmware | Windows | Backend | Resultado |
|---|---|---|---|---|---|
| Xbox Wireless / Series | USB | — | — | SDL 3.5.0 | não testado |
| Xbox Wireless / Series | Bluetooth | — | — | SDL 3.5.0 | não testado |
| DualShock 4 | USB / BT | — | — | SDL 3.5.0 | não testado |
| DualSense | USB / BT | — | — | SDL 3.5.0 | não testado |
| Switch Pro | USB / BT | — | — | SDL 3.5.0 | não testado |
| 8BitDo (modos X/D/S) | USB / BT / receptor | — | — | SDL 3.5.0 | não testado |
| Genérico sem perfil (4 direções + 2 botões) | USB | — | — | SDL 3.5.0 | não testado — assistente de mapeamento (#79) coberto só por eventos simulados |

## O que foi executado

`tools/ControlFS.InputProbe` no macOS arm64, sem controle conectado: SDL 3.5.0 inicializou, bombeou eventos e
encerrou sem erros. Isso valida carregamento da biblioteca nativa e ciclo de vida, **não** hardware nem Windows.

## Como testar (Windows)

```powershell
dotnet run --project tools/ControlFS.InputProbe -- 30
# com convenção alternativa:
dotnet run --project tools/ControlFS.InputProbe -- 30 --east-confirms
```

### Joystick sem perfil (assistente, #79)

Com um joystick que o SDL não reconhece como gamepad (o probe mostra `gamepad=False`), registre: tipo de direcional
(hat, eixos ou botões), se algum eixo repousa fora do centro (gatilho em -1, analógico com drift), se o assistente
capturou cada passo, o resultado do teste antes de salvar e se o perfil voltou a valer ao reconectar e ao reabrir o app.

Registre modelo, transporte, modo, firmware (se conhecido), versão do Windows e a saída do probe nesta tabela,
incluindo a `família` detectada (Xbox, PlayStation, Nintendo ou Generic) — ela decide as legendas no modo automático.
