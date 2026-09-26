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
| Genérico sem perfil (4 direções + 2 botões) | USB | — | — | SDL 3.5.0 | não testado — sem assistente ainda (Etapa 3) |

## O que foi executado

`tools/ControlFS.InputProbe` no macOS arm64, sem controle conectado: SDL 3.5.0 inicializou, bombeou eventos e
encerrou sem erros. Isso valida carregamento da biblioteca nativa e ciclo de vida, **não** hardware nem Windows.

## Como testar (Windows)

```powershell
dotnet run --project tools/ControlFS.InputProbe -- 30
# com convenção alternativa:
dotnet run --project tools/ControlFS.InputProbe -- 30 --east-confirms
```

Registre modelo, transporte, modo, firmware (se conhecido), versão do Windows e a saída do probe nesta tabela.
