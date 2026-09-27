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

## Como testar (Windows, sem SDK)

O próprio ControlFS tem a tela **Teste de controles** (Menu → Teste de controles…). Não precisa do SDK do .NET: basta o
instalador ou o portátil da release. Passo a passo em `docs/TESTING.md` → "Teste de controles (#78)". Resumo:

1. Abra o ControlFS, conecte **um controle por vez** (primeiro USB, depois Bluetooth/receptor) e abra Menu → Teste de controles….
2. Confira a linha do controle: nome, tipo do SDL, família, VID:PID, gamepad ou joystick cru e se é o ativo.
3. Aperte cada controle: direcional (4 direções), analógico esquerdo (4 direções), Sul, Leste, Oeste, Norte, LB, RB, LT,
   RT, Start e Select. Cada pressão mostra o controle físico e a ação, por exemplo `South (Botão A) → Confirm`.
4. Segure Confirmar 1 s (ou Enter) para **copiar o relatório** e cole num comentário da issue
   [#78](https://github.com/nextestudios/ControlFS/issues/78), dizendo o transporte (USB/Bluetooth/receptor) e o modo do
   controle (ex.: 8BitDo em X/D/S). Segure Voltar 1 s (ou Esc) para sair.

O relatório leva versão do app, do Windows e do SDL, os campos de cada controle e as linhas "controle → ação". Não leva
nome de usuário, caminhos do dispositivo, GUID nem número de série. Com o relatório, esta tabela é atualizada.

### Remapeadores e duplicatas (#80)

Steam Input e DS4Windows/ViGEm expõem o controle físico e uma cópia virtual. O ControlFS marca como virtual só o que
dá para reconhecer sem o nome: joystick virtual do SDL, "Steam Virtual Gamepad" (28DE:11FF) e, como **provável**, um
Xbox 360 com fio (045E:028E) conectado junto de um controle PlayStation ou Nintendo. Não observado em hardware ainda:
registre no relatório do teste de controles se a cópia aparece e se Menu → Controle ativo elimina a ação dupla.

### Joystick sem perfil (assistente, #79)

Com um joystick que o SDL não reconhece como gamepad (a tela de teste mostra `raw joystick, no profile`), registre: tipo
de direcional (hat, eixos ou botões — o teste mostra `botão 3`, `direcional 1 ↑`, `eixo 2 +`), se algum eixo repousa fora
do centro (gatilho em -1, analógico com drift), se o assistente capturou cada passo, o resultado do teste antes de
salvar e se o perfil voltou a valer ao reconectar e ao reabrir o app. Depois de salvo, a tela de teste mostra a entrada
crua e o controle traduzido juntos (ex.: `botão 1 = South → Confirm`).

### Para desenvolvedores

`tools/ControlFS.InputProbe` continua disponível (`dotnet run --project tools/ControlFS.InputProbe -- 30`, com
`--east-confirms` para a convenção alternativa) para depurar o backend fora do app.
