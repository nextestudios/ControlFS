# 0007 — Experimento: mira por giroscópio no teclado virtual (#77)

- **Data:** 2026-09-27
- **Estado:** experimental (protótipo atrás de um ajuste, desligado por padrão; **não validado em hardware**)

## O que foi feito

- **Sensor:** SDL3 `SDL_GamepadHasSensor`/`SDL_SetGamepadSensorEnabled(SDL_SENSOR_GYRO)` e o evento
  `SDL_EVENT_GAMEPAD_SENSOR_UPDATE` (rad/s; usa o relógio do próprio sensor quando existe). O sensor só é ligado com
  Configurações → Controles → **Mira por giroscópio no teclado** ativo; desligado, nada muda (nem bateria, nem banda
  Bluetooth). Qualquer gamepad em que o SDL exponha giroscópio funciona (DualSense, DualShock 4, Switch Pro, Joy-Con,
  Steam Deck); Xbox não tem.
- **Filtro (`GyroPointer`, Core):** calibração contínua (o desvio do sensor é aprendido enquanto o controle está
  parado, abaixo de 4°/s), zona morta suave de 2,5°/s contra tremor, suavização exponencial (0,5), sensibilidade de
  1 tecla a cada 4° (≈45° de um lado ao outro do teclado) e lacunas limitadas a 50 ms (Bluetooth em rajadas não vira
  salto). Valores iniciais escolhidos no papel.
- **Camada de ponteiro (`AppController.AimKeyboard`):** o ponteiro parte do centro da tecla focada e anda em frações de
  tecla; ao cruzar para outra tecla, ela recebe o foco e **Sul digita como sempre**. Nas bordas o ponteiro para (nunca dá
  a volta). O direcional continua valendo e reancora o ponteiro na tecla que ele focou. **R3** recentraliza. Só o
  controle ativo mira, só no teclado virtual, nunca com a janela em segundo plano. Nada depende do giroscópio.
- **Testes:** `GyroPointerTests` (desvio aprendido/tremor ignorado; direção, proporção e lacuna) e
  `GyroKeyboardJourneyTests` (desligado por padrão, aponta sem dar a volta, direcional reancora, R3 não troca a exibição).

## Desvios do pedido e por quê

- **"Segurar L2 para mirar":** no teclado, LT/RT já levam o cursor ao início/fim do texto, e a entrada do app é
  semântica (o gatilho vira `PageUp` antes de chegar ao teclado). Para não roubar um comando existente, o protótipo mira
  **sempre** que o teclado está aberto e o ajuste está ligado, confiando na zona morta. Se no hardware o ponteiro
  "andar sozinho" enquanto se digita com o direcional, a próxima etapa é um botão de segurar dedicado (L3, hoje sem
  função) ou "mira só depois de um giro rápido".
- **Sensibilidade, suavização e zona morta ajustáveis pelo usuário:** existem como `GyroSettings`, mas ainda não na
  interface. Primeiro medir no hardware quais valores importam.

## O que falta medir (hardware; ver `docs/TESTING.md`)

- DualSense por USB e por Bluetooth: taxa real de leitura, latência percebida, desvio em repouso após 1 min.
- Se mirar é mais rápido que o direcional para digitar um nome de 10–15 letras (cronometrar as duas formas).
- Se 4°/tecla é confortável no sofá e no portátil (Steam Deck/ROG Ally com giroscópio embutido, se o SDL expuser).
- Se o ponteiro anda sem querer ao apertar botões com força (aí, botão de segurar).

## Decisão

Manter como experimento desligado por padrão até os testes acima; promover (e expor os ajustes) só com resultado
positivo registrado em `docs/controller-compatibility.md`.
