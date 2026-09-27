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
| LB / RB | Região anterior/próxima (no teclado virtual: mover cursor) | Ctrl+← / Ctrl+→ |
| LT / RT | Página anterior/próxima (10 itens) | PgUp / PgDn |
| Start | Menu do aplicativo (no teclado virtual: OK) | F10 (no teclado virtual: Enter) |
| Select | Busca (ainda não implementada; não aparece nas legendas) | Ctrl+F |

"Confirmar com botão direito" (menu do app) troca **comportamento e legendas** (`ConfirmBackConvention.EastConfirms`).
Guide/Home não é mapeado. L3/R3 não são usados.

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
- Janela sem foco: roteamento suspenso; ao voltar, estados limpos e só novas transições contam.
- Analógico: zona morta 0,25, ativação 0,55, liberação 0,40 (histerese), dominância 1,25 (diagonais ambíguas ignoradas).

## `Back`

1. Fecha o modal do topo (em diálogos, executa a opção segura declarada).
2. Limpa a seleção, se houver.
3. Cancela um carregamento em andamento.
4. Volta no histórico do painel (restaurando o foco no item de origem).
5. Sem histórico: tela inicial. Na tela inicial: confirmação de saída com foco em "Cancelar".

## Foco

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
com diagnóstico, assistente para controles sem perfil, remapeamento, rumble opcional.
