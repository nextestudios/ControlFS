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

## Comparativo com o Explorador de Arquivos e o Files

Medido pelo passo **Compare with File Explorer and Files** do Smoke (modo `full`), que roda `build/Compare-Performance.ps1` e
publica `performance-comparison.md/.json` no artefato `performance-comparison`. Para reproduzir: `gh workflow run smoke.yml
-f mode=full` e baixe o artefato (ou rode o script num Windows com o exe portátil: `pwsh build/Compare-Performance.ps1
-Portable <ControlFS-Portable-x64.exe> -OutDir out`).

**Aviso importante.** O runner `windows-latest` (Windows 10.0.26100, 4 vCPUs) **não tem GPU (renderização por software,
WARP) nem controle**: o custo de desenho difere de um PC real, e os números variam com o hardware. Cada valor é a mediana
de 3 execuções depois de uma abertura de aquecimento descartada; o script não estima nada.

**Método (igual para os três).** Cada app abre a mesma pasta. "Até a janela" = do comando de abertura até existir uma
janela visível e sem dono com tamanho de janela de verdade (o mesmo detector para todos; não é "conteúdo pronto": o
primeiro quadro do ControlFS veio ~240 ms depois da janela). Depois de 3 s de espera, mede-se 10 s: CPU (tempo de processador
÷ tempo de relógio, % de **um** núcleo), conjunto de trabalho, bytes privados (`PrivateMemorySize64`, memória comprometida) e
threads, **somando todos os processos do app**:

- **ControlFS** (exe portátil da build do commit): o processo e seus descendentes. Ele não tem argumento de pasta, então
  abre pela pasta de dados (`settings.json` com duas abas restauradas: a ativa é a pasta do cenário, a outra é a pasta
  pequena), sem verificar atualizações e sem boas-vindas.
- **Files** (4.2.9.0, instalado com `winget install --id FilesCommunity.Files -e`): todo processo cujo executável está na
  pasta do pacote (ou de nome `Files*.exe`) e seus descendentes; aberto com o alias `files.exe "<pasta>"`. Ele mostrou 1 processo.
- **Explorador de Arquivos:** aberto com `explorer.exe "<pasta>"`. Nesse runner cada abertura cria um processo
  `explorer.exe` **próprio** (não há um shell de área de trabalho hospedando a janela), que continua vivo depois de
  fechar a janela; o que se soma é a **diferença** do conjunto de processos `explorer.exe` entre antes (linha de base medida
  na hora, 10 s) e depois de abrir a janela, e o CPU é a taxa com a janela menos a taxa da linha de base (por isso pode
  dar levemente negativo: é ruído). Depois de cada execução os processos novos são encerrados. Num PC de verdade a janela
  vive dentro do shell em execução, então o custo real de uma janela do Explorador difere deste.

Cenários: (a) pasta com 40 arquivos (20 mais uma subpasta com 20); (b) pasta com 5.000 arquivos de 1 KB (mais uma subpasta).
O cenário (c), "entrar numa subpasta e voltar", **não foi medido**: não dá para automatizar a navegação da mesma forma nos
três apps, e não estimamos nada.

Commit `e584b39` + o script (run 36477018137, nextestudios/ControlFS, 28/09/2026); medianas de 3:

| Cenário | App | Processos | Até a janela (ms) | Conjunto de trabalho (MB) | Bytes privados (MB) | CPU (% de 1 núcleo) | Threads |
|---|---|---|---|---|---|---|---|
| a: 40 arquivos | ControlFS | 1 | 741 | 157,8 | 62,1 | 0,47 | 34 |
| a: 40 arquivos | Explorador (janela) | 1 | 563 | 145,3 | 56,6 | -0,15 | 58 |
| a: 40 arquivos | Files | 1 | 530 | 266,9 | 110,4 | 0,78 | 53 |
| b: 5.000 arquivos | ControlFS | 1 | 754 | 161,3 | 65,9 | 0,47 | 35 |
| b: 5.000 arquivos | Explorador (janela) | 1 | 610 | 146,7 | 57,7 | 0 | 55 |
| b: 5.000 arquivos | Files | 1 | 491 | 267,2 | 110,3 | 0,31 | 55 |

O que os números dizem: o ControlFS usa **~40% menos memória que o Files** (conjunto de trabalho e bytes privados) nos dois
cenários, e as três CPUs em repouso ficam abaixo de 1% de um núcleo (as diferenças estão dentro do ruído: no cenário b o
Explorador chegou a 1,87% numa das três execuções). Mas o ControlFS **não** é o mais leve em tudo: a janela do Explorador
custou ~15 MB a menos de conjunto de trabalho e ~8 MB a menos de bytes privados, e o Explorador e o Files mostraram a
janela ~150 a ~260 ms antes. Para o ControlFS, o primeiro quadro (mediana) foi 995 ms no cenário b. O "conjunto de trabalho"
conta páginas compartilhadas de novo em cada processo; os bytes privados são a leitura mais fiel. O ControlFS minimizado
(modo Leve em segundo plano, medida do passo anterior no mesmo run) ficou em ~18 MB de conjunto de trabalho; os outros dois
não foram medidos minimizados. Pasta de 5.000 arquivos não mudou o custo de nenhum dos três de forma relevante (as listas
são virtualizadas ou carregadas sob demanda).

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
