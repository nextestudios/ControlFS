# Desempenho

O ControlFS precisa ser **leve** e caber ao lado de um jogo: em repouso quase não gastar CPU, e minimizado (ou atrás do
jogo) ceder o processador e a memória. Metas de projeto (ainda não medidas): p95 < 50 ms de retorno visual à navegação;
rolagem próxima de 60 fps; pastas de 10 mil itens sem materializar milhares de controles; memória da extração não
proporcional ao tamanho descompactado.

## Como medir

O workflow **Smoke** (manual, modo `full`) tem a etapa **Measure performance**, que roda `build/Measure-Performance.ps1`
no portátil e no instalado e publica a tabela no resumo do job e no artefato `performance` (`performance.md`,
`performance.json` e os logs de cada execução). Para cada pacote: uma **primeira** abertura (portátil com a pasta de
extração do .exe único vazia, `DOTNET_BUNDLE_EXTRACT_BASE_DIR`) e três aberturas **quentes**; a tabela mostra cada uma e
a mediana das quentes. Em cada abertura (`--no-onboarding`):

1. **Janela** e **primeiro quadro**: ms desde o início do processo, lidos das linhas `Janela ativada (N ms desde o início
   do processo)` e `Primeiro quadro (…)` do `startup.log`. O primeiro quadro é registrado numa tarefa de prioridade baixa
   enfileirada no `Loaded` da raiz, que roda depois do layout e do desenho (nunca `CompositionTarget.Rendering`, que
   derrubava o app no WARP). O log também tem as fases da `MainWindow` (serviços, SDL, layout, controlador).
2. **Primeiro plano**: janela trazida para a frente, 3 s de espera, 10 s medidos: CPU (tempo de processador ÷ tempo de
   relógio, em % de **um** núcleo), conjunto de trabalho, bytes privados e threads.
3. **Minimizado**: `ShowWindow(SW_MINIMIZE)`, 3 s de espera, 10 s medidos, as mesmas colunas.
4. Tamanho do portátil, do instalador e da pasta instalada.

Uma abertura que fecha no meio da medição falha a etapa (foi assim que a queda ao minimizar apareceu). O runner do GitHub
(`windows-latest`, 4 vCPUs, sem GPU: WARP, sem controle conectado) varia bastante de uma máquina para outra: a mesma
compilação mediu de 543 a 1100 ms até a janela em runs diferentes. Por isso cada lado abaixo é a mediana de **três runs
do Smoke** (cada uma já é a mediana de três aberturas), e diferenças de tempo de abertura menores que ~300 ms não contam.

## Resultados (setembro de 2026, PR "light background mode")

Antes = `main` com só a correção da queda ao minimizar (sem ela, o `main` fechava ao minimizar e a coluna "minimizado"
não existia); depois = este PR. Medianas de três runs do Smoke; entre parênteses, instalado.

| Medida | Antes | Depois |
|---|---|---|
| Até a janela (ms) | 1031 (933) | 901 (851) |
| Até o primeiro quadro (ms) | 1274 (1169) | 1099 (1058) |
| CPU em repouso, janela à frente (% de um núcleo) | 1,72 (1,41) | 0,47 (0,62) |
| Conjunto de trabalho à frente (MB) | 155,3 (155,1) | 152,8 (152,4) |
| Bytes privados à frente (MB) | 61,7 (62,2) | 60,5 (60,6) |
| Threads à frente | 35 (36) | 34 (35) |
| CPU minimizado (% de um núcleo) | 2,03 (1,41) | 0,47 (0,47) |
| Conjunto de trabalho minimizado (MB) | 153,1 (152,9) | **17,9 (17,8)** |
| Bytes privados minimizado (MB) | 61,7 (62,3) | 55,7 (55,7) |
| Threads minimizado | 34 (35) | 32 (33) |
| Portátil / instalador / pasta instalada (MB) | 70,5 / 46,6 / 182,4 | 70,5 / 46,6 / 182,4 |

Runs: antes 36376517596, 36378314242, 36378318367; depois 36377487211, 36378316173, 36378320665 (nextestudios/ControlFS).
A abertura ficou um pouco mais rápida, mas dentro do ruído do runner. Primeira abertura do portátil (extrai ~180 MB):
~1,4–1,7 s até a janela nos dois lados.

Fases da abertura quente (log de uma abertura típica): runtime e WinUI até o `Main` do app ~250–450 ms; serviços 13 ms;
SDL 16–32 ms; layout montado ~120 ms; controlador iniciado (preferências, locais, primeiro `Render`) ~290 ms; primeiro
quadro ~130 ms depois de ativar a janela.

## Decisões (com as medições que as sustentam)

### Leitura dos controles (`InputCadence`)

| Situação | Leitura |
|---|---|
| Janela à frente, controle conectado, Fluidez máxima | thread de 8 ms com o relógio do Windows em 1 ms |
| Janela à frente, controle conectado, Fluidez economia | temporizador da UI de 8 ms (~15,6 ms na prática) |
| Janela à frente, **sem controle** | temporizador de 250 ms, só para notar a chegada de um (teclado e mouse chegam por eventos do WinUI) |
| Minimizada/inativa, Leve em segundo plano | 1 s, SDL em silêncio (só conexões) |
| Minimizada/inativa, sem Leve em segundo plano | 120 ms, SDL em silêncio (só conexões) |

Antes, a thread de 8 ms e o relógio de 1 ms ficavam ligados com a janela à frente mesmo sem controle nenhum (o caso do
runner): era a maior parte do 1,4–1,7% de CPU em repouso. "SDL em silêncio" desliga no SDL os eventos de botões, eixos,
toques e sensores (conexões e desconexões continuam): com um jogo usando o mesmo controle, o ControlFS não enfileira
milhares de eventos por segundo para descartar. Ao voltar, o estado dos analógicos e gatilhos é relido sem gerar ações.

### Leve em segundo plano (`BackgroundModePolicy`, ligado por padrão)

Janela inativa **ou minimizada** (minimizar a única janela da área de trabalho a deixa ativa, sem `Deactivated`; o estado
do presenter também conta): prioridade abaixo do normal + EcoQoS (modo de eficiência do Windows 11), monitor de unidades
parado (confere na hora ao voltar) e, depois de 5 s ainda em segundo plano, uma coleta agressiva do GC (compacta também o
heap de objetos grandes) seguida de `SetProcessWorkingSetSize(-1, -1)`. Resultado: conjunto de trabalho de ~153 MB para
~18 MB (o log registra "memória devolvida (151 MB → 1 MB)"). Ativar a janela restaura a prioridade na hora; as páginas
voltam sob demanda. Mídia tocando mantém tudo como está. Menu → Configurações → **Leve em segundo plano** desliga (volta ao
comportamento anterior: prioridade normal, 120 ms).

### O que já era sob demanda (conferido) e o que foi adiado

- **LibGit2** (nativo): só carrega na primeira leitura de status, que só acontece com "Status do Git" ligado.
- **Media Foundation / PDF**: as fábricas não criam nada até o primeiro áudio, vídeo ou PDF.
- **Verificação de atualizações**: agora começa depois do primeiro quadro (`AppController.OnFirstFrame`), não no meio da
  abertura (preparar o HTTPS não disputa a thread de UI).
- **SDL**: `SDL_Init` mediu 16–32 ms; fica no caminho da abertura (adiar atrasaria a detecção de um controle já
  conectado sem ganho visível).
- **Ícones do Shell**: a thread STA começa ociosa; nada é pedido antes de a tela precisar.

### Memória

- Caches de ícones por tamanho: linhas 512 (48 px), grade 256 (64 px), barra superior 128, cartões do início 128 (96 px),
  painel de detalhes **32** (144 px: ~330 KB por ícone no dobro da escala; antes 512 = até ~170 MB no pior caso).
- Logo decodificada em até 128 px lógicos (o arquivo tem 900×269).
- Visualização de imagens já decodificava reduzida ao tamanho da tela (`WicImageDecoder`, `ImagePreviewPolicy`).

### ReadyToRun: desligado

Medido num run de experimento (branch `exp/perf-r2r`, `PublishReadyToRun=true` no portátil e no instalado): portátil
**91,4 MB** (+21 MB), instalador **55,9 MB** (+9 MB), pasta instalada 229 MB (+47 MB), e nenhum ganho de abertura (591 ms
portátil / 715 ms instalado até a janela, contra 543 / 594 ms do `main` medido na mesma hora). O runtime do .NET já vem
pré-compilado; o custo da abertura está na inicialização do WinUI/Windows App SDK e no primeiro layout, não no JIT do app.
TieredCompilation e TieredPGO ficam no padrão do .NET 10 (sem R2R, a compilação rápida do nível 0 é o que mantém a
abertura curta).

### Compressão do portátil: ligada

Sem `EnableCompressionInSingleFile` (branch `exp/perf-nocompress`): portátil **171 MB** em vez de 70,5 MB. A compressão
só custa na **primeira** abertura, quando o .exe único se extrai (1648 → 1397 ms até a janela sem compressão); nas
seguintes a extração é reaproveitada e o tempo é o mesmo. 2,4× menos download vale ~0,25 s uma vez.

## Decisões anteriores com impacto em desempenho

- Listagem e extração fora da thread de UI (`Task.Run`); navegação cancela a listagem anterior e descarta respostas antigas.
- `ListView` virtualizado com modelo de linha sem bindings, preenchido em `ContainerContentChanging`.
- Extração em fluxo com buffer de 80 KB do `ArrayPool` (memória constante por entrada).

## Riscos conhecidos

- `MainWindow.Render` reconstrói rodapé e camada modal a cada mudança de estado; a medir com navegação p95.
- Trocar a seleção recria `ItemsSource` (para repintar marcas); em pastas grandes isso pode custar — otimizar com
  atualização de contêineres visíveis.
- A listagem ordena a pasta inteira antes de exibir (sem listagem incremental ainda).
- O runner não tem controle nem GPU: a leitura rápida com controle, o EcoQoS e o jogo ao lado estão nas verificações
  manuais de `docs/TESTING.md` ("Leve em segundo plano e repouso").

## Pendente

Navegação p95 (carimbo no `InputRouter` → `Render`), primeira página de uma pasta grande e consumo durante a extração.
