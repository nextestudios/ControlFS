<h1 align="center"><img src="logos/controlfs-logo-text.png" alt="ControlFS" width="560"></h1>

Um **gerenciador de arquivos nativo para Windows, feito para o controle**, com **extrator integrado**: navegue, organize, visualize e descompacte arquivos do sofá, num PC ligado à TV ou num portátil Windows. Escrito em C# com **.NET 10 e WinUI 3**: sem Electron, sem motor de navegador embutido, sem web view. Código aberto (AGPL-3.0-only), local, sem login, **sem telemetria**.

🇺🇸 [Read in English](README.md)

![Windows](https://img.shields.io/badge/Windows-11%20x64-blue)
![Versão](https://img.shields.io/github/v/release/nextestudios/ControlFS?include_prereleases&label=vers%C3%A3o&color=brightgreen)
![Licença](https://img.shields.io/badge/licen%C3%A7a-AGPL--3.0--only-blue)
![CI](https://github.com/nextestudios/ControlFS/actions/workflows/ci.yml/badge.svg)

> **Pré-alfa.** A primeira jornada funciona e é coberta por testes automatizados sobre arquivos reais, mas o app **ainda não foi validado em Windows com controles físicos**. Veja o [PROGRESS.md](PROGRESS.md).

<p align="center">
  <img src="docs/images/controlfs-tour.gif" alt="Tour animado do ControlFS: navegar do sofá com o direcional, visualização de imagens em tela grande, player de vídeo em tela cheia com legendas e faixas de áudio, extrair ZIP, 7z, RAR, TAR e GZ, o teclado na tela e os temas claro e escuro com cores de destaque" width="800">
</p>

## Capturas de tela

Capturas reais do app (tema escuro, 1920x1080), geradas pela CI do projeto.

| | |
|---|---|
| ![Início em grade com as pastas principais, unidades e o painel de detalhes](docs/images/home-grid.png) | ![Lista com o painel de detalhes mostrando uma imagem](docs/images/folder-details.png) |
| ![Dois painéis lado a lado, com itens marcados](docs/images/dual-pane.png) | ![Menu de ações do item com os blocos de ações rápidas](docs/images/actions-menu.png) |

## Download

Baixe a **[0.11.1-alpha.1](https://github.com/nextestudios/ControlFS/releases/tag/v0.11.1-alpha.1)** (pré-lançamento):

- **`ControlFS-Setup-x64.exe`** (recomendado): instala por usuário, sem admin, e **se atualiza sozinho** (atualizações assinadas e verificadas).
- **`ControlFS-Portable-x64.exe`**: um único executável que guarda os dados na pasta `ControlFS_Data` ao lado dele; avisa de novas versões, a troca é manual.

Windows 11 x64. Ainda sem assinatura de código, então o SmartScreen pode avisar ([política](docs/CODE_SIGNING.md)). Está na 0.1.0-alpha.1 ou alpha.2? Elas não abriam: rode o instalador uma vez; daí em diante as atualizações são automáticas.

## Leve no seu PC

O ControlFS é um app nativo, não uma página web numa caixa, então cabe ao lado de um jogo. Medimos contra o Explorador de Arquivos do Windows e o [Files](https://files.community) abrindo a **mesma pasta com 5.000 arquivos pequenos** (medianas de 3 execuções, 10 s depois de abrir, somando todos os processos de cada app; Files 4.2.9.0):

| Pasta com 5.000 arquivos | ControlFS | Explorador* | Files |
|---|---|---|---|
| RAM, conjunto de trabalho (ControlFS depois da própria limpeza em repouso) | 18 MB | 144 MB | 264 MB |
| RAM, bytes privados | 59 MB | 56,5 MB | 109 MB |
| CPU em repouso (% de um núcleo) | 0,31 | cerca de 0 | 0,47 |
| Threads | 36 | 55 | 50 |

![Gráfico de barras: conjunto de trabalho e bytes privados do ControlFS, Explorador de Arquivos e Files](docs/images/performance-comparison.svg)

- O ControlFS usa **cerca de 45% menos memória que o Files** (bytes privados; 59 contra 109 MB), e a CPU mediana em repouso fica abaixo de 1% de um núcleo nos três.
- **Conjunto de trabalho:** os 18 MB são medidos depois de o ControlFS devolver ao Windows, uma vez, o que a abertura deixou de sobra (dois segundos depois do primeiro quadro; o Windows faz o mesmo com processos ociosos e as páginas voltam sob demanda). O Explorador e o Files não passam por essa limpeza nossa, então compare a linha de **bytes privados**, que a limpeza quase não muda: nela o ControlFS ainda fica cerca de 2,5 MB (uns 4%) acima de uma janela do Explorador.
- Ele **não** é o mais leve em todas as colunas: a janela do Explorador usa cerca de 2,5 MB a menos de memória privada e fica em ~0% de CPU onde o ControlFS fica em cerca de 0,3%.
- Minimizado (ou atrás de um jogo), o ControlFS se enxuga ainda mais: cerca de 12 MB de conjunto de trabalho no modo leve em segundo plano (medido só para o ControlFS; os outros não foram medidos minimizados).
- \* O Explorador mostra o custo do processo `explorer.exe` da própria janela: no runner de CI cada `explorer.exe <pasta>` inicia um processo próprio, e é esse processo que é contado. Num desktop real a janela vive dentro do shell em execução e o custo é outro.

**Por que leva cerca de um segundo para abrir?** No registro de abertura (uma abertura "quente" na máquina de teste), o tempo vai para três coisas: o Windows carregando o **.NET e o Windows App SDK / WinUI** antes de rodar uma única linha do ControlFS (uns 250 a 450 ms); o ControlFS **montando a tela** (uns 120 ms); e **ler as suas preferências, listar as pastas e desenhar o primeiro quadro** (uns 300 ms). O Explorer não paga isso porque já está residente no Windows, e uma janela nativa em C++ não tem um framework para iniciar. A primeira abertura do `.exe` portátil também se descompacta uma vez (uns 1,5 s); a versão instalada não. Depois de aberto, o app continua leve: o custo está na partida, não durante o uso. Estamos trabalhando para mostrar a janela mais cedo e preencher o conteúdo enquanto ele carrega.

**Leia isto antes de citar os números.** Eles vêm de um runner `windows-latest` do GitHub Actions (4 vCPUs, **sem GPU, renderização por software (WARP), sem controle**), não de um PC real: o custo de renderização difere do hardware de verdade e os números variam de máquina para máquina. "Conjunto de trabalho" conta páginas compartilhadas uma vez por processo; "bytes privados" é a medida de memória mais honesta. Uma pasta de 40 arquivos também foi medida (mesmas conclusões) e o cenário "entrar numa subpasta e voltar" **não** foi medido (não dá para automatizar da mesma forma nos três apps). Testado: ControlFS compilado do `main` no commit `55eaef4`, Windows 10.0.26100, 28/09/2026, medianas de três execuções do Smoke ([36491965336](https://github.com/nextestudios/ControlFS/actions/runs/36491965336), [36491968039](https://github.com/nextestudios/ControlFS/actions/runs/36491968039), [36491972002](https://github.com/nextestudios/ControlFS/actions/runs/36491972002)). O método exato, todos os cenários e como reproduzir estão em [docs/performance.md](docs/performance.md) (em português); o script é [build/Compare-Performance.ps1](build/Compare-Performance.ps1) (rode pelo workflow Smoke, modo `full`).

## Como funciona

1. Abra o app: a tela inicial lista suas pastas (Downloads, Documentos…) e unidades.
2. Navegue com o direcional ou o analógico; **Sul** abre, **Leste** volta, **Norte** mostra as ações.
3. Num `.zip`, **Sul** abre em modo somente leitura; **Norte → Extrair** descompacta numa pasta dedicada, aqui, ou numa pasta escolhida dentro do app.

| Controle (posição) | Faz | Teclado |
|---|---|---|
| Direcional / analógico esquerdo | Mover | Setas |
| Sul (A / ✕) | Abrir / confirmar | Enter |
| Leste (B / ○) | Voltar / fechar | Esc |
| Oeste (X / □) | Marcar item | Espaço |
| Norte (Y / △) | Ações do item | F2 |
| LT / RT | Página anterior / próxima | PgUp / PgDn |
| Start | Menu do app | F10 |
| Menu → Tela cheia | Tela cheia (também o botão ao lado de minimizar) | F11 |

Os botões seguem a **posição física**, então um controle Nintendo não inverte confirmar e voltar. Dá para trocar para "confirmar com o botão direito" no menu.

## Recursos

**Navegar e se localizar**
- Pastas e unidades reais, histórico, ordenação, itens ocultos, marcação (marcar todos / limpar), propriedades, **pastas favoritas**, **pastas e arquivos recentes** no Início, **barra de caminho navegável**
- **Abas** (restauradas ao abrir, reabrir aba fechada, duplicar) e **dois painéis** lado a lado (L3 troca; copiar/mover/extrair para o outro painel)
- **Locais de rede** (unidades mapeadas e atalhos de rede), tipos de unidade à primeira vista (local, USB, óptica, rede), atualizados ao conectar ou remover um pendrive
- **Busca** por nome na pasta atual (ou nas pastas principais, a partir do Início), com ou sem subpastas e com filtros: os resultados aparecem enquanto são encontrados e dá para cancelar; sem índice, links nunca seguidos
- **Selos de status do Git** nas pastas de repositório (opcional, desligado por padrão)

**Exibição**
- **Lista ou grade** (R3 ou Ctrl+G), com navegação 2D pelo controle, e **painel de detalhes** com as informações e a prévia do item
- **Tema escuro ou claro** (segue o Windows por padrão) e **cores de destaque**, todas com contraste conferido; **barra de título com o tema** e **tela cheia** (F11)
- **Layout responsivo** para portáteis 720p/800p, desktop e TVs 1080p/4K; ícones nativos do Windows para arquivos, pastas e unidades
- **Leitor de tela:** o Narrador anuncia o item focado, a posição e os estados

**Operações de arquivo**
- Renomear, **renomear em lote** (numeração, localizar e substituir, prefixo/sufixo, caixa), copiar, recortar, colar, mover e excluir para a **Lixeira**, com conflitos (pular / manter ambos / substituir / mesclar pastas) e resultado por item
- **Desfazer e refazer** operações reversíveis, **pausar e retomar**, **tentar de novo** uma operação que falhou ou só os itens que falharam, **histórico de operações**; restos de operações interrompidas são limpos na próxima abertura
- **Lixeira** no Início: restaure para a pasta original ou exclua de vez (sempre com confirmação)
- **Análise de uso do disco** (maiores pastas e arquivos primeiro), criar pasta, **abrir terminal aqui**
- **Abrir com o Windows:** programa padrão, "Abrir com…", "Mostrar no Explorador de Arquivos"; programas e scripts pedem confirmação

**Compactados**
- **Navegar e extrair** ZIP (inclusive ZIP64 e AES), 7z, RAR4/RAR5, TAR, TAR.GZ e GZ sem descompactar antes; **volumes divididos** (`.7z.001`, `.part1.rar`, `.z01`) abrem a partir de qualquer parte; compactados com senha; vários de uma vez, cada um na sua pasta; **teste de integridade**
- **Criar** ZIP, TAR.GZ e 7z a partir dos itens marcados (RAR não pode ser criado: formato proprietário) ([matriz](docs/archive-support.md))

**Visualização**
- **Imagens** (JPG, PNG, GIF, BMP, WebP) com zoom, deslocamento e anterior/próxima; **texto** (logs, notas, configurações, código) com **edição leve**; **PDF** sem o Edge
- **Player de áudio** e **player de vídeo em tela cheia** (legendas, faixa de áudio, retomar de onde parou)
- **Montar e desmontar** ISO, IMG, VHD e VHDX com a montagem do próprio Windows

**Controle**
- **Legendas que acompanham o seu controle** (Xbox, PlayStation, Nintendo, genérico), glifos vetoriais originais, rodapé por contexto; os botões seguem a posição física
- **Assistente de mapeamento** para joysticks sem perfil, **tela de teste de controles** e troca do **controle ativo**
- **Rolagem com o analógico direito** em listas, menus, texto, imagens ampliadas e diálogos
- **Celular como controle** pela sua rede local: leia um QR code, sem instalar app, conexão criptografada (AES-256-GCM), pareamento com código e um "Permitir" explícito no PC
- **Teclado virtual** próprio (português/inglês, acentos, símbolos, sugestões, segurar para repetir, senha mascarada) usável só com direções + confirmar + voltar

**Segurança e atualizações**
- **Extração segura:** nada é gravado fora do destino, links são bloqueados, colisões de nome são recusadas, limites de tamanho, staging temporário, verificação CRC ([modelo de segurança](docs/security-model.md))
- **Atualizações automáticas e verificadas** (versão instalada): verificação diária, download em segundo plano; manifesto assinado + SHA-256; desligável ([como funciona](docs/GUIDE.pt-BR.md#atualizações))

**Primeira vez e jogos**
- **Boas-vindas e tutorial guiado** na primeira vez: os botões de verdade do seu controle e um passo a passo interativo, que dá para pular e nunca mexe em arquivos (Menu → Ajuda e tutorial)
- **Mais da equipe:** uma tela, uma única vez, com os outros apps da equipe, NextBoost PRO e Console Mode; dá para abrir de novo em Menu → Ajuda e tutorial
- **Leve em segundo plano:** minimizado ou atrás de um jogo, o ControlFS baixa a prioridade, entra no modo de eficiência do Windows e devolve memória

## Roadmap

**Próximos:** validação com controles reais (#78) e assinatura de código (#84) antes da 1.0 ([roadmap por prioridade MoSCoW](https://github.com/nextestudios/ControlFS/issues/95)) Detalhes em [docs/roadmap.md](docs/roadmap.md).

## Política de assinatura de código (Code signing policy)

Assinatura de código gratuita fornecida por [SignPath.io](https://about.signpath.io), certificado da [SignPath Foundation](https://signpath.org) (pedido em andamento: até a aprovação, as releases saem sem assinatura e o SmartScreen do Windows pode avisar na primeira execução).

- **Autores e revisores:** [@nextestudios](https://github.com/nextestudios) (mantenedor; toda mudança passa por pull request e CI)
- **Aprovadores:** [@nextestudios](https://github.com/nextestudios) (cada release é aprovada manualmente antes de assinar)
- **Build:** as releases são geradas só pelo [workflow de release](.github/workflows/release.yml) público no GitHub Actions a partir de uma tag no `main`; o certificado nunca sai do serviço de assinatura.
- **Privacidade:** este programa não transfere nenhuma informação para outros sistemas em rede, a menos que seja pedido especificamente pelo usuário ou por quem o instala ou opera. O único acesso à internet é a verificação opcional de atualizações no GitHub; conectar o celular como controle (Menu → Conectar celular) usa só a sua rede local, e só enquanto você usa ([política de privacidade](docs/PRIVACY.md)).

Detalhes: [docs/CODE_SIGNING.md](docs/CODE_SIGNING.md).

## Mais

- [Guia](docs/GUIDE.pt-BR.md): controles, extração, privacidade, solução de problemas, compilar
- **Feedback:** [este formulário](https://github.com/nextestudios/ControlFS/issues/new?template=feedback.yml)
- Histórico: [português](CHANGELOG.md) · [English](CHANGELOG.en-US.md)

Licença [GNU Affero General Public License v3.0 only](LICENSE) ([notas sobre a licença](docs/LICENSING.md)) · [Avisos de terceiros](THIRD_PARTY_NOTICES.md) · [Política de assinatura de código](docs/CODE_SIGNING.md) · [Segurança](SECURITY.md)
