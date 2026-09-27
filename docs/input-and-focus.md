# Entrada e foco

## Pipeline

```text
SDL3 (gamepad por posição física) ─► StickNormalizer (analógico) ─► PhysicalControl
     ─► InputRouter: dispositivo ativo · ActionMap (convenção) · repetição só de navegação · trava pós-contexto
     ─► InputAction ─► AppController (modal do topo ▸ tela) ─► comando
Teclado físico ─► InputHost.OnKeyDown ─► InputAction (mesmo AppController)
Mouse/toque ─► Pointer* (posiciona o foco) ─► Confirm (mesmo AppController)
```

## Mapeamento padrão (posições físicas)

| Entrada | Ação | Teclado |
|---|---|---|
| D-pad / analógico esquerdo | Navegar | Setas |
| South | Confirmar / abrir | Enter |
| East | Voltar / fechar | Esc, Backspace |
| West | Marcar/desmarcar | Espaço |
| North | Ações do item | F2, tecla Menu |
| LB / RB | Barra de caminho ↔ lista (no teclado virtual: mover cursor) | Ctrl+← / Ctrl+→ |
| LT / RT | Página anterior/próxima (10 itens) | PgUp / PgDn |
| Start | Menu do aplicativo (no teclado virtual: OK) | F10 (no teclado virtual: Enter) |
| Select / View | Busca (no teclado virtual: símbolos) | Ctrl+F |
| R3 (pressionar analógico direito) | Lista ↔ grade (`ChangeView`) | Ctrl+G |

"Confirmar com botão direito" (menu do app) troca **comportamento e legendas** (`ConfirmBackConvention.EastConfirms`).
Guide/Home não é mapeado. L3 não é usado; R3 troca lista ↔ grade (também Ctrl+G e Menu → Exibição). Joysticks sem
perfil não têm R3 no assistente: usam Ctrl+G ou o Menu.

## Regras implementadas e testadas (unitário)

- Confirmar/voltar/menus disparam uma vez por transição; **nunca repetem** (`InputRouterTests`).
- Navegação repete após 380 ms, acelerando de 130 ms até 45 ms.
- Ao abrir/fechar modal, botões mantidos ficam travados até serem soltos — o botão que abriu o diálogo não o aceita.
- Somente um dispositivo comanda a UI; o primeiro a pressionar assume. Outro controle assume com uma nova pressão
  quando o ativo não está segurando nada (troca a quente; evita ação dupla de um par físico+virtual). Com modal sensível
  aberto (conflito, substituir, sair), outro dispositivo não assume. Desconexão do ativo libera a vaga; operações em
  disco continuam.
- Família do controle (`ControllerFamilies.Detect`): tipo de gamepad do SDL (`xbox360`/`xboxone` → Xbox,
  `ps3`/`ps4`/`ps5` → PlayStation, `switchpro`/`joycon*`/`gamecube` → Nintendo); se o SDL não souber, o vendor ID
  (Microsoft, Sony, Nintendo); senão, genérico. Joystick sem perfil é sempre genérico. Nunca pelo nome do dispositivo.
  Só muda legendas. "Legendas: automáticas" (padrão) segue o controle ativo; o menu pode fixar uma família.
- Legendas do rodapé: `AppController.Hints` (ações válidas no contexto) passam por `IControllerPromptProvider`
  (`ControllerPromptProvider`), que devolve glifo + família, tecla e texto para o Narrador. Usa a convenção
  confirmar/voltar (`ActionMap.ControlFor`), então trocar a convenção troca comportamento e legenda. Sem controle ativo,
  ou depois de uma tecla do teclado físico, mostra teclas; a próxima pressão do controle volta aos glifos. A UI nunca
  testa nomes de dispositivo.

## Joysticks sem perfil (#79)

- `Sdl3InputBackend` publica eventos crus (botões, hats, eixos) só de joysticks sem perfil de gamepad
  (`IInputSink.OnRawInput`) e o estado instantâneo (`GetRawState`) para calibrar o neutro.
- `InputHost` entrega o evento ao `AppController.OnRawInput`: com assistente em andamento ou joystick ainda sem perfil,
  a aplicação consome; com perfil salvo, `ControllerProfileTranslator` gera `PhysicalControl` (neutro e zona morta
  calibrados, histerese, diagonal de hat ignorada) e segue pelo `InputRouter` como qualquer gamepad.
- `ControllerMappingWizard` (Core, testado): neutro (1 s com tudo solto, mede ruído → zona morta 0,2–0,6) → um passo
  por controle (direções, confirmar e voltar conforme "Confirmar com", depois opcionais) → teste. Cada passo exige soltar
  tudo antes do próximo; entrada repetida é recusada; o Voltar já mapeado pula opcionais; sem entrada por 20 s (60 s no
  teste) cancela. O sentido de um eixo é a inversão (Cima em +1 = eixo invertido).
- Perfis: `ControllerProfileSerializer` (JSON versionado, `format` + `schemaVersion`, até 64 KB, profundidade 4, só
  campos conhecidos, faixas e controles obrigatórios conferidos) e `JsonControllerProfileStore` (`<dados>\controllers`,
  um arquivo por GUID ou vendor/product, gravação atômica + `.bak`). Aplicado por GUID e, se ele mudar, por vendor/product.
  Substituir um perfil sempre pede confirmação.
- Controle de dois botões: um perfil sem Norte (Ações) e/ou sem Start (Menu) ganha `LongPressFallback` (Core, testado
  com o `InputRouter`). O `InputHost` passa os controles do perfil pelo `LongPressTranslator`: Confirmar/Voltar só saem
  ao soltar (pressão curta igual a antes) e, mantidos por 0,6 s, viram uma pressão de Norte/Start — Ações/Menu — e o
  soltar não gera mais nada. Só vale para o que falta no perfil; gamepads e perfis completos não mudam. O rodapé mostra
  Ações/Menu no glifo de Confirmar/Voltar com "(segure)". Consequência: nesses controles, segurar Sul sobre `⌫ ◀ ▶` do
  teclado virtual não repete (abre Ações = Maiúsculas).
  Na tela Teste de controles o substituto não age: cada pressão aparece como o controle do perfil.

## Rodapé por contexto

`AppController.BuildHints` é a fonte única; `HintJourneyTests` cobre os contextos. Ação que não funciona não aparece.
Nas telas (não nos modais) a ordem é fixa: Confirmar, Voltar, Marcar, Ações, Menu, Buscar, Lista/Grade, LB, RB
(`PromptJourneyTests`). Lista/Grade (R3, Ctrl+G) aparece no início, nas pastas, na busca e na Lixeira e diz a exibição
de destino: "Grade" na lista, "Lista" na grade. No Xbox, os glifos das faces têm as cores do controle (A verde,
B vermelho, X azul, Y amarelo); as outras famílias mantêm seus desenhos.

| Contexto | Legendas |
|---|---|
| Pasta / arquivo | Abrir · Voltar · Marcar/Desmarcar · Ações · Menu · Lista/Grade |
| Compactado no disco | Explorar · Marcar · Extrair… (o menu abre em "Extrair para \"nome\"") · Menu · Voltar |
| Dentro de um compactado | Abrir/Detalhes · Marcar · Extrair… · Menu · Voltar |
| Itens marcados | Abrir · Desmarcar/Marcar · Operações (N) · Menu · Cancelar seleção |
| Carregando | Menu · Cancelar |
| Item bloqueado | Motivo · Ações · Menu · Voltar |
| Pasta do disco (além do acima) | Buscar (Select/View) |
| Resultados da busca | Mostrar na pasta · Ações · Nova busca · Menu · Cancelar busca (buscando) / Voltar |
| Teclado virtual | Selecionar (só com controle) · Apagar · Maiúsculas · Cursor · Símbolos · Concluir (Start/Options) · Cancelar |
| Menu | Escolher (oculto em item indisponível: o motivo aparece no item) · Fechar |
| Diálogo | nome da opção em foco · opção segura de Voltar |

**Decisão (extrair direto, #35):** Oeste continua sendo Marcar também em compactados, para que possam entrar em
copiar/mover/excluir/compactar em lote. Os botões livres estão reservados (Select para a busca, LB/RB para regiões e
dois painéis), então o atalho de extrair é Norte → Sul: num compactado, Norte aparece como "Extrair…" e o menu abre
com foco em "Extrair para \"nome\"". O rótulo usa a extensão (rápido); a ação sempre detecta o formato pelo conteúdo.
- Janela sem foco: roteamento suspenso; ao voltar, estados limpos e só novas transições contam.
- Analógico: zona morta 0,25, ativação 0,55, liberação 0,40 (histerese), dominância 1,25 (diagonais ambíguas ignoradas).

## `Back`

1. Fecha o modal do topo (em diálogos, executa a opção segura declarada).
2. Limpa a seleção, se houver.
3. Cancela um carregamento ou uma busca em andamento (a busca cancelada mantém os resultados parciais).
4. Volta no histórico do painel (restaurando o foco no item de origem).
5. Sem histórico: tela inicial. Na tela inicial: confirmação de saída com foco em "Cancelar".

## Foco

- Regiões do navegador (#30): lista e barra de caminho (`PaneState.Region`). LB entra na barra com foco na pasta acima;
  esquerda/direita escolhem; Sul navega (com histórico) e foca o filho de onde viemos; RB/baixo/Leste voltam à lista;
  Norte mostra o caminho completo em menu. Só um foco fica visível: na barra, o anel da lista some.
- Foco lógico por identidade (`FileListState`); sobrevive a reordenação/atualização. Item focado removido (excluir,
  mover, mudança externa) → o **próximo item que sobreviveu** na ordem anterior; sem próximo, o anterior. Com itens na
  lista, o foco nunca fica vazio (`StateTests`).
- Anel de foco único (`Theme.ApplyFocus`: borda de destaque de 3 px + fundo suave) na lista, nos menus e nos diálogos; o
  teclado virtual usa os mesmos tokens. A seleção nativa do `ListView` fica desligada para não haver dois realces.
- Ao trocar de pasta, a lista é medida e rola até o item focado antes do primeiro quadro; ao mover o foco, só as duas
  linhas afetadas são redesenhadas.
- Ao fechar um modal, se o foco do XAML ficou em um elemento que saiu da árvore, a raiz recebe o foco de volta (o teclado
  físico nunca fica sem destino).
- Após criar pasta: foco na pasta criada. Ao sair de um compactado ou subir de nível: foco no item de origem.
- Diálogos destrutivos iniciam na opção segura; conflito inicia em "Pular (manter existente)".
- O WinUI não recebe foco de XAML para navegação: a raiz (`ContentControl`) captura teclas em `PreviewKeyDown`; teclas
  `Gamepad*` do WinUI são descartadas para evitar entrada dupla com o SDL.

## Pendente

Troca explícita de dispositivo ativo pela UI, preferência de físico sobre virtual na troca a quente (ex.: Steam Input), recuperação após suspensão do sistema (testar), duplicidade físico+virtual
com diagnóstico, remapeamento de gamepads conhecidos, rumble opcional.
