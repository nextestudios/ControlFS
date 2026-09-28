# Entrada e foco

## Pipeline

```text
SDL3 (gamepad por posição física) ─► StickNormalizer (analógico) ─► PhysicalControl
     ─► InputRouter: dispositivo ativo · ActionMap (convenção) · repetição só de navegação · trava pós-contexto
     ─► InputAction ─► AppController (modal do topo ▸ tela) ─► comando
Teclado físico ─► InputHost.OnKeyDown ─► InputAction (mesmo AppController)
Mouse/toque ─► Pointer* (posiciona o foco) ─► Confirm (mesmo AppController)
Celular (#223) ─► PhoneLinkServer (quadro autenticado) ─► PhoneProtocol ─► AppController.HandlePhone ─► InputAction / TypeText
```

## Mapeamento padrão (posições físicas)

| Entrada | Ação | Teclado |
|---|---|---|
| D-pad / analógico esquerdo | Navegar | Setas |
| South | Confirmar / abrir | Enter |
| East | Voltar / fechar | Esc, Backspace |
| West | Marcar/desmarcar | Espaço |
| North | Ações do item | F2, tecla Menu |
| LB / RB (L1/R1, L/R) | Barra superior: no conteúdo, entra (LB na pasta de cima, RB no primeiro atalho); na barra, alvo anterior/seguinte; na faixa de abas, aba anterior/seguinte (no teclado virtual: mover cursor) | Ctrl+← / Ctrl+→ |
| LT / RT | Página anterior/próxima (10 itens) | PgUp / PgDn |
| Start | Menu do aplicativo (no teclado virtual: OK) | F10 (no teclado virtual: Enter) |
| Select / View | Busca (no teclado virtual: símbolos) | Ctrl+F |
| R3 (pressionar analógico direito) | Lista ↔ grade (`ChangeView`) | Ctrl+G |
| L3 (pressionar analógico esquerdo) | Com dois painéis (#56): troca o painel ativo (`SwitchPane`); nunca muda marcação nem inicia operação | Tab |

"Confirmar com botão direito" (Menu → Configurações) troca **comportamento e legendas** (`ConfirmBackConvention.EastConfirms`).
Guide/Home não é mapeado. L3 troca o painel ativo com os dois painéis ligados (sem conflito: LB/RB são da barra
superior, LT/RT das abas com 2+ abas, R3 da exibição); com um painel só, não faz nada. R3 troca lista ↔ grade (também Ctrl+G e Menu → Configurações → Exibição). Joysticks sem
perfil não têm R3 no assistente: usam Ctrl+G ou o Menu.

## Regras implementadas e testadas (unitário)

- Confirmar/voltar/menus disparam uma vez por transição; **nunca repetem** (`InputRouterTests`).
- Foco ao trocar de local (`FileListState.SetItems(..., newLocation)`): abrir outra pasta ou compactado sem um item
  pedido foca o **primeiro** item; Voltar (histórico), subir (Esquerda/LB) e Atualizar focam o item guardado
  (`ListModeJourneyTests::Opening_another_location_…`).
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

## Celular como controle (#223)

- Um toque na página do celular vira **uma** `InputAction` (direções, `PageUp/PageDown`, `Scroll*` pela área de deslizar,
  `Confirm`, `Back`, `ToggleSelection`, `OpenContextMenu`, `OpenAppMenu`, `Search`, `ChangeView`,
  `PreviousRegion/NextRegion`), entregue direto ao `AppController.Handle`, como o teclado físico: não passa pelo
  `InputRouter`, não muda o controle ativo nem as legendas e não tem estado mantido. Segurar uma seta na página repete
  **no celular** (380 ms, depois a cada 110 ms) mandando pressões avulsas; soltar, sair do botão, esconder a página ou
  perder a conexão param na hora e nada fica preso no PC.
- Texto do teclado do celular vai para o campo do teclado na tela aberto (`TypeText`, `TypeBackspace`); Enter do
  celular é o OK do teclado na tela (`OpenAppMenu` nele). Sem teclado na tela aberto, texto é ignorado e a página
  desativa o campo.
- Confirmação sensível aberta (`Modal.IsSensitive`: excluir, substituir, desfazer, mapear controle): do celular só passa
  `Back` (a opção segura); o resto é descartado com um aviso no rodapé e a página mostra "responda no PC". É o mesmo
  princípio do `InputRouter.AllowAutomaticActivation` (outro dispositivo não assume uma confirmação sensível).
- Nada do celular chega antes de o usuário escolher Permitir no PC. Testes: `PhoneJourneyTests`,
  `PhoneLinkIntegrationTests`; manual: `docs/TESTING.md` → Celular como controle.

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
Nas telas (não nos modais) a ordem é fixa: Confirmar, Voltar, Marcar, Ações, Menu, Buscar, Lista/Grade, L1, R1
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

- Início em grade (fase B): seções de cartões (`AppController.HomeSections`: Favoritos, Pastas principais, Unidades e
  dispositivos, Outros locais), cada uma com as colunas que a tela mostra (`SetHomeGridLayout`). Esquerda/direita seguem
  a ordem visual e passam de seção; cima/baixo trocam de linha na mesma coluna (limitada às colunas da seção de destino);
  LT/RT vão ao começo da seção anterior/seguinte (`SectionGridNavigation`). O foco continua sendo um índice de
  `Places` (a lista mostra a mesma coleção na ordem original), então trocar lista ↔ grade mantém o local focado.
- Regiões (#30, #50, redesenho, #176): conteúdo, barra superior (caminho + acesso rápido) e abas
  (`AppController.FocusRegion`: `PaneState.Region` no navegador, estado próprio no início). A barra superior é uma
  região só, uma linha:
  `[L1] [Locais|Meu computador] › segmentos › atual │ Favoritos · Arquivos recentes · pastas do Windows · Meu computador · Lixeira [R1]`.
  - Alvos (`TopBarTargets`, na ordem da tela): segmentos e atalhos que levam a outro lugar. A pasta atual (último
    segmento) é o rótulo do local, não uma ação: é pulada por L1/R1/esquerda/direita, não tem legenda de Sul e o toque
    nela não faz nada; no início, "Locais › Início" já é aqui (só os atalhos são alvos); o atalho que aponta para o local
    atual (`IsQuickAccessActive`) também é pulado. Favoritos e Arquivos recentes (menus) são sempre alvos.
  - Entrar a partir do conteúdo: a pasta atual fica entre o caminho e o acesso rápido, então **L1** foca o alvo logo
    antes dela (a pasta de cima) e **R1** o logo depois (o primeiro atalho); sem alvo desse lado, o mais próximo do
    outro (no início, os dois vão para Favoritos).
  - Na barra: **L1/R1** e **esquerda/direita** vão ao alvo anterior/seguinte, sem dar a volta (param nas pontas); do
    último segmento alvo passam ao primeiro atalho. LT/RT: primeiro/último alvo da parte atual (caminho ou atalhos).
  - Sul: segmento navega (com histórico) e foca o filho de onde viemos; a raiz "Locais" vai ao início e "Meu computador"
    abre as unidades na aba atual com histórico (fase B; no seletor de pasta, abre os outros locais); atalho de pasta/Lixeira abre na aba atual com
    histórico (no início, abre o navegador); Favoritos e Arquivos recentes abrem um menu, e fechar o menu devolve o foco
    ao atalho.
  - Norte num segmento: caminho completo em menu. Baixo e Leste voltam ao conteúdo com o foco da lista onde estava
    (depois de navegar, o foco vai para a lista: no filho de onde viemos ou no primeiro item); Start abre o menu.
  - Abas: a faixa fica no cabeçalho ao lado do logo e só aparece com 2+ abas (com uma, repetiria o caminho), sem
    legenda própria. **Cima** na barra superior entra nela; lá, L1/R1 e esquerda/direita trocam de aba, Norte abre
    Nova/Fechar/lista das abas, Sul/baixo/Leste voltam ao conteúdo. Menu → Abas faz o mesmo sem a faixa; "Abrir em nova
    aba" continua nas ações de pasta. (Antes do #176, RB no conteúdo ia direto para a faixa.)
  - Só um foco fica visível: na barra ou nas abas, o anel da lista some. Enquanto o foco está no conteúdo, os glifos
    de L1/R1 ficam nas pontas da barra (família do controle em uso via `IControllerPromptProvider`) e saem do rodapé;
    na barra, o rodapé mostra Ir para/Abrir, Anterior/Próximo, Abas (Cima, com 2+) e Voltar à lista.
  - Coberto por `BreadcrumbJourneyTests`, `TabsJourneyTests` e `TopBarJourneyTests` (L1/R1 pelos alvos, pasta atual
    pulada e sem recarregar, restauração do foco em lista e grade).
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
- Menus com ações rápidas (#193): uma grade de blocos no topo (`MenuItem.Placement = Quick`, decidido no
  `AppController`; até 4 por linha, blocos perigosos no fim) e a lista das demais opções abaixo.
  - Na grade: **Esquerda/Direita** andam entre os blocos da linha e **param nas pontas** (não fecham o menu nem
    escolhem); **Baixo** desce uma linha e, da última, entra no primeiro item da lista (sem lista: volta à primeira
    linha, mesma coluna); **Cima** sobe uma linha e, da primeira, dá a volta para o último item da lista (sem lista: vai
    à última linha).
  - Na lista: Cima/Baixo como sempre (dá a volta: do último item para o primeiro bloco); **Cima no primeiro item** sobe
    para a última linha da grade, na coluna de onde o foco saiu. Direita escolhe e Esquerda fecha, como antes.
  - LT/RT (PageUp/PageDown): primeiro bloco / último item. Confirmar escolhe; Voltar fecha; o rodapé não muda.
  - Foco inicial: o primeiro item que o `AppController` passou (ou `FocusOn`), nunca um perigoso (`SafeInitialFocus`).
  - Narrador: "rótulo completo, estado (ação perigosa / indisponível: motivo), ação rápida N de M"; na lista, a posição
    conta só os itens da lista. Mouse/toque num bloco escolhe (mesmo caminho de `PointerChooseModalOption`).
  - Configurações (Menu → Configurações…): opções `KeepOpen` aplicam o ajuste e o menu continua aberto com o texto novo
    e o foco no mesmo ajuste; opções com tela própria fecham Configurações. Coberto por `ModalSystemJourneyTests`.
  - Grades por grupo (#227, `MenuModal(..., sectionGrids: true)`, só em Configurações): a ordem do `AppController` vale e
    cada sequência de blocos do mesmo grupo é uma grade (`MenuModal.Grids`), seguida da lista desse grupo. As regras da
    grade acima valem para todas; entre partes:
    - **Baixo** na última linha de uma grade vai ao item seguinte (outra grade logo abaixo: mesma coluna, encolhida se a
      linha for mais curta); **Baixo** numa linha de lista que tem uma grade embaixo entra no **primeiro bloco** dela.
    - **Cima** na primeira linha de uma grade vai ao item anterior; **Cima** para dentro de uma grade (vindo de uma linha
      de lista ou de outra grade) cai na **última linha, na coluna de onde o foco saiu** da última grade (`GridColumn`).
    - No fim/começo do menu dá a volta (último item ↔ primeiro), como na lista.
    - Narrador: "rótulo completo com o valor (ex.: Itens ocultos: mostrar), descrição, bloco N de M" (M = blocos do grupo).
  - Largura (#227): todo menu tem largura fixa (`MenuModal.PanelWidth`: 540 com grade, 460 sem; mínimo = máximo, os dois
    limitados pela janela). Mover o foco ou mostrar a descrição da linha focada nunca muda a largura; o texto quebra linha.
- O WinUI não recebe foco de XAML para navegação: a raiz (`ContentControl`) captura teclas em `PreviewKeyDown`; teclas
  `Gamepad*` do WinUI são descartadas para evitar entrada dupla com o SDL.

## Pendente

Troca explícita de dispositivo ativo pela UI, preferência de físico sobre virtual na troca a quente (ex.: Steam Input), recuperação após suspensão do sistema (testar), duplicidade físico+virtual
com diagnóstico, remapeamento de gamepads conhecidos, rumble opcional.

## Abas nos gatilhos (#185)

No navegador (lista, grade e início) com 2+ abas, LT/RT (L2/R2) trocam para a aba anterior/seguinte, dando a volta nas
pontas (`HandleTabTrigger`). Com uma aba só, os gatilhos mantêm a paginação da lista e o pulo entre seções do início.
Modais, teclado virtual, visualizações e a barra superior tratam os gatilhos antes (primeiro/último alvo na barra) e
nunca trocam de aba; L1/R1 nunca trocam de aba. Coberto por `TabsJourneyTests`.
