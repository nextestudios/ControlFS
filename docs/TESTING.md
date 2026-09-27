# Testes manuais

Checklist para o que a CI não consegue provar (Windows real, controles, telas). Marque só o que foi **executado**, com
versão, Windows, controle e conexão. Resultados de controles vão para `controller-compatibility.md`.

## Antes de cada release

- [ ] Baixar `ControlFS-Portable-x64.exe` da release, conferir o SHA-256 com o valor do `release-manifest.json` e abrir numa conta padrão; a pasta `ControlFS_Data` aparece ao lado.
- [ ] Tela inicial mostra Downloads/Documentos reais e as unidades.
- [ ] Teclado físico: setas, Enter, Esc, Espaço, F2, F10, F11 (tela cheia).
- [ ] Conectar um controle **depois** de abrir o app: o cabeçalho mostra o controle ao apertar um botão.
- [ ] Segurar Sul ao abrir um diálogo: o diálogo **não** é aceito sozinho.
- [ ] Criar pasta "Relatórios ação" só com o teclado virtual e o controle.
- [ ] Abrir um ZIP, entrar numa subpasta, voltar; foco retorna ao arquivo de origem.
- [ ] Extrair para pasta dedicada, extrair aqui duas vezes (conflito → "Manter ambos"), extrair para… com o seletor interno.
- [ ] ZIP com senha: senha errada pede de novo; senha certa extrai.
- [ ] Desconectar o controle durante uma extração grande: a extração continua; reconectar e seguir navegando.
- [ ] Alt+Tab para outro app: botões do controle não fazem nada no ControlFS até voltar.
- [ ] 1280×720, 1280×800, 1920×1080 e 4K com escala 100–200%: nenhuma ação essencial fica inacessível.
- [ ] Narrador lê o nome dos itens e das teclas do teclado virtual.
- [ ] Teclado virtual em 1920×1080 e num portátil (1280×800): as seis linhas cabem sem rolagem, a tecla focada é clara a 3 m e `Concluir`/página atual/Maiúsculas se destacam.
- [ ] Digitar "Relatório ação 2026" só com direcional e Sul, em cada campo: nova pasta, renomear, senha de arquivo, nome do compactado.
- [ ] Foco (#31): o anel de destaque da lista é igual ao dos menus e diálogos, visível a 3 m da TV e em 4K; itens
      recortados ficam esmaecidos mas o anel continua nítido.
- [ ] Foco (#31): abrir e fechar menus/diálogos com o teclado físico e com o controle; as setas continuam funcionando
      logo depois (inclusive após clicar numa opção com o mouse).
- [ ] Foco (#31): marcar vários itens no meio de uma pasta grande, excluir; o foco vai para o item seguinte e fica
      visível sem rolar manualmente. Voltar de uma subpasta numa pasta com milhares de itens: o item de origem aparece
      focado já no primeiro quadro.
- [ ] Com controle real: segurar Oeste por 2 s apaga vários caracteres e para na hora ao soltar; segurar LB/RB move o cursor continuamente; segurar Sul em Concluir não repete.
- [ ] Favoritar uma pasta num pendrive, fechar o app, remover o pendrive e reabrir: o favorito aparece primeiro como indisponível (⚠); reconectar e voltar ao início: volta a abrir.
- [ ] Cursor do teclado virtual visível a 3 m e em 4K; renomear "ControlFS" para "Control-FS" com LT, RB e Sul; LT/RT num nome longo; o Narrador lê a posição do cursor.
- [ ] Seleção no teclado virtual (#44): ao renomear `example-file.zip`, `example-file` aparece destacado e sublinhado, visível a 3 m e em 4K; digitar `novo` resulta em `novo.zip`; `Sel. tudo` na página `…` e Ctrl+A selecionam tudo; o Narrador lê "N de M caracteres selecionados".

## Narrador (#40)

Com o Narrador ligado (Ctrl+Win+Enter), usando só o controle:

- [ ] Início: ao abrir, lê "Início" e o local focado com o tipo e a posição ("… 2 de 9"); cada movimento lê o novo local.
- [ ] Pasta: ao entrar, lê "Pasta <caminho>" e o item focado com tipo e tamanho; ao andar, só o item (sem repetir o contexto).
- [ ] Estados por extenso: marcar (Oeste/X) lê "marcado"; recortar lê "recortado"; entrada bloqueada lê "bloqueado: motivo";
      compactado com senha lê "com senha".
- [ ] Grade: mover em 2D lê cada bloco; trocar lista/grade não perde nem repete o item.
- [ ] Menu (Start e Norte): lê "Menu <título>" e o item; item indisponível lê "indisponível: motivo".
- [ ] Diálogo de exclusão: lê o título, os itens, a mensagem e "Cancelar, botão 1 de 3"; ao andar, só o botão.
- [ ] Teclado virtual: lê "Teclado virtual: <título>" e cada tecla focada; o cursor lê a posição.
- [ ] Barra superior (L1/R1) e abas (Cima na barra): lê o segmento/atalho/aba focado e "ativa".
- [ ] Avisos do rodapé (ex.: "3 itens restaurados") são lidos sem mover o foco; nenhuma informação depende só de som,
      vibração ou cor.
- [ ] Caps+Tab (ler o item atual) repete o último foco anunciado.

## Testes de UI Automation (#83)

As jornadas (`tests/ControlFS.UnitTests`) conduzem o `AppController`, mas não veem o que o WinUI desenha. O
`build/Test-UiAutomation.ps1` abre o portátil de verdade, manda teclas para a janela (`SendKeys`) e confere pela
**UI Automation** do Windows (`System.Windows.Automation`, do .NET Framework, no Windows PowerShell 5.1) o que está na
tela. Escolha: UIA puro, sem WinAppDriver (parado e sem suporte ao WinUI 3 atual) nem dependências novas; roda no
workflow **Smoke** (manual), no passo "UI Automation", e deixa `uia-results.json`, logs e prints de falha em
`smoke-uia` no artefato `smoke-evidence`.

O foco do ControlFS é lógico (o anel é desenhado; o foco do XAML fica na raiz para receber o teclado), então o
`ModalView` marca o texto focado com AutomationIds estáveis: `ControlFS.ModalTitle`, `ControlFS.FocusedOption`,
`ControlFS.FocusedKey` e `ControlFS.KeyboardField` (nome "Texto: …, cursor …", o mesmo que o Narrador lê). O que é
conferido: F10 abre o Menu com uma opção focada; ↓ e PageDown movem o anel; **Sair abre a confirmação com o foco em
Cancelar** (opção segura); F10 dentro dela não abre outro modal; → e ← movem o foco; Esc fecha sem sair; Ctrl+F numa
pasta abre o teclado virtual com as teclas desenhadas e uma focada; ↓ move o foco; texto digitado aparece no campo; Esc
fecha. Para rodar num Windows: `powershell -File build\Test-UiAutomation.ps1 -Exe <ControlFS.exe> -OutDir uia`
(não mexa no mouse e no teclado enquanto roda; o script cria `ControlFS_Data` ao lado do .exe, sem verificar
atualizações).

Não cobre: controle físico (SDL), Narrador de verdade, TV/DPI reais (ver as outras seções).

## Layout responsivo (#36)

A faixa de layout sai do tamanho **efetivo** da janela (pixels ÷ escala do Windows) e do tamanho do texto do Windows
(`LayoutBreakpoints`, testado na CI): **compacta** abaixo de 900 de altura ou 1360 de largura "em texto" (portáteis
1280×720/800, 1080p a 150%, 4K a 300%), **normal** (1080p a 100%, 4K a 200%) e **grande** a partir de 1300 de altura
(4K a 100–125%: texto e espaços crescem até 2×, com as proporções de 1080p). O texto do Windows maior reduz o espaço
"em texto" e pode deixar o layout compacto; o próprio WinUI aumenta cada texto.

Evidência automática: o workflow **Smoke** (manual) roda `ControlFS-Portable-x64.exe --render-screens <pasta>`, que
monta as telas reais (início, pasta, lista compacta, menu com foco abaixo da dobra, teclado virtual e, em 720p, 800p,
1080p e 4K 100/200%, os modais da seção "Modais (#172)") em 1280×720,
1280×800 (e com texto a 150%), 1920×1080 (100% e 150%) e 3840×2160 (100%, 200% e 300%), além da galeria de glifos
(escuro/claro, 100/200/300%), e publica os PNGs no artefato `smoke-screens` com um `report.txt` das alturas medidas.
A resolução é simulada (a tela do runner é pequena); o modo usa uma pasta temporária, não acessa a rede e fecha sozinho.
Para iterar rápido: `mode=screens` pula instalador, lançamentos e UIA (só publica a pasta do app), e os filtros
`screens` (prefixos das capturas, ex. `m1,2d,glyphs`) e `sizes` (alvos, ex. `1920x1080,1280x720`) viram
`--only`/`--sizes` do `--render-screens`. Cada captura espera o layout, dois quadros desenhados e os ícones do sistema
chegarem, em vez de esperas fixas.

Ainda manual, num aparelho real:

- [ ] Portátil 1280×800 (e 1280×720) a 100% e 125%: cabeçalho, lista, rodapé e todos os menus visíveis; nenhuma
      legenda do rodapé some (quebram linha); no menu principal o item focado sempre aparece ao descer até "Sair".
- [ ] TV 1080p e TV 4K (100% e 300%) a ~3 m: nomes da lista, rodapé e teclado virtual legíveis; anel de foco visível.
- [ ] Windows → Acessibilidade → Tamanho do texto em 150% e 200% com o app aberto: o layout se ajusta na hora, nada
      essencial fica cortado (menus rolam até o item focado).
- [ ] Arrastar a janela entre um monitor 100% e outro 150%/200%: textos e ícones nítidos, sem reiniciar.

## Modais (#172)

Automático: `ModalSystemJourneyTests` (ícone em toda opção de menu e botão de diálogo, com símbolo; ações perigosas
marcadas e nunca focadas ao abrir; entrada — botões, clique na lista, abas — nunca chega à tela atrás de um modal;
modais aninhados fecham um por vez e o foco volta), UIA (Sair começa em Cancelar; F10 não abre outro modal por cima) e
as capturas `m1-menu-actions`, `m1b-menu-destructive-focus`, `m2-confirm-delete`, `m2b-confirm-delete-solid`
(transparência reduzida), `m3-extract-summary`, `m4-password`, `m5-result-error`, `m6-operations`,
`m7-operation-details`, `m8-picker-menu`, `m9-about`, `4-menu` e `5-keyboard` em 1280×720, 1280×800, 1920×1080 e
3840×2160 (100% e 200%), mais `icons/action-icons.png` (todos os ícones com o nome) no artefato `smoke-screens`.

Não validado em hardware (controle real, TV a ~3 m, DPI real, Configurações do Windows reais):
- [ ] TV 1080p e 4K a ~3 m: dá para dizer qual opção está focada sem depender da cor (preenchimento, negrito, tamanho);
      os ícones são reconhecíveis e combinam com o texto; títulos de grupo legíveis.
- [ ] Portátil 1280×800/720: menus longos (Menu, ações de arquivo) rolam até o item focado; o painel e as legendas do
      rodapé do painel cabem sem cortar.
- [ ] Xbox, PlayStation, Nintendo e genérico: as legendas do painel mostram o glifo certo e fazem a ação escrita
      (Escolher, Fechar, a opção focada do diálogo, Concluir/Cancelar no teclado); troca a quente atualiza na hora.
- [ ] Windows → Personalização → Cores → **Efeitos de transparência** desligados com um menu aberto: o painel fica
      sólido na hora e o fundo mais escuro; ligar de novo volta ao fosco. Economia de energia/área de trabalho remota:
      o acrílico cai para a cor sólida sozinho.
- [ ] Windows → Acessibilidade → **Temas de contraste** ligado: painel sólido, texto e foco legíveis.
- [ ] Windows → Acessibilidade → Efeitos visuais → **Efeitos de animação** desligados: modais aparecem sem transição;
      ligados: só um esmaecer curto (≤ 120 ms), sem atrasar a entrada (apertar Sul logo ao abrir já escolhe).
- [ ] Sombra do painel visível sobre a tela escurecida (ThemeShadow; o `--render-screens` não desenha sombras).
- [ ] Confirmações perigosas (Sair, Excluir permanentemente, Executar, Substituir, Descartar) começam em Cancelar/opção
      segura; a opção perigosa focada fica vermelha com texto escuro e o símbolo de alerta.
- [ ] Mouse: clicar numa opção faz o mesmo que Sul; clicar fora do painel não aciona nada atrás dele.
- [ ] Narrador: opções perigosas são lidas com "ação perigosa"; o título do modal é lido ao abrir.

## Barra de caminho (#30)

- [ ] Com o controle: LB na lista leva o foco para a barra; esquerda/direita; Sul numa pasta de cima navega e foca a
      pasta de origem; baixo ou Leste voltam para a lista. Mesmo com Ctrl+←/→ no teclado.
- [ ] Dentro de um ZIP em subpasta: o `▸` e o ícone de pacote separam disco e compactado; escolher a pasta do disco
      foca o arquivo .zip.
- [ ] Caminho longo (8+ níveis) em 1280×720: o meio vira `…`, nada sai da tela; Sul no `…` mostra as pastas escondidas.
- [ ] Clique/toque num segmento navega até ele.

## Barra superior e cabeçalho (redesenho, fase A2)

Não validado em hardware.

- [ ] Início, com controle: LB (ou RB) leva o foco a Favoritos; direita percorre Arquivos recentes, as pastas do Windows (com os
      ícones do Windows, sem emoji), Meu computador e Lixeira; Sul em Downloads abre a pasta; Leste/baixo voltam à lista.
- [ ] Numa pasta funda: a barra mostra "Meu computador › C:\ › … › pasta" (caminho real, meio recolhido); LB foca a pasta
      de cima; direita pula a pasta atual e passa aos atalhos; Sul num atalho abre na mesma aba e Leste volta.
- [ ] Favoritos e Arquivos recentes abrem uma lista; fechar a lista devolve o foco ao atalho. Sem favoritos, a lista
      explica como adicionar.
- [ ] 1280×720 e 1280×800: os atalhos que não cabem mostram só o ícone (o focado e o da pasta atual mostram o nome);
      nada sai da tela; os glifos de LB e RB aparecem nas pontas da barra e não no rodapé.
- [ ] O logo com o nome aparece em todas as telas; minimizar, maximizar, fechar, redimensionar, mover e F11 continuam
      funcionando (barra de título do Windows).
- [ ] Clique/toque num atalho ou num segmento faz o mesmo que focar e apertar Sul.
- [ ] Narrador: "Acesso rápido. Downloads, 3 de 10" ao andar pelos atalhos; "Barra de caminho" nos segmentos.

## Barra superior com L1/R1 (#176)

Não validado em hardware.

- [ ] O cabeçalho não mostra a pasta atual num chip separado nem uma legenda de R1 ao lado do logo (com uma aba só).
- [ ] Xbox, PlayStation, Nintendo (Switch Pro) e genérico: os glifos nas pontas da barra são LB/RB, L1/R1, L/R e
      L1/R1 genérico; com o teclado, Ctrl+← / Ctrl+→. O glifo troca na hora ao pegar outro controle.
- [ ] Numa pasta: L1 foca a pasta de cima, R1 o primeiro atalho; L1/R1 e esquerda/direita andam pelos alvos com o anel
      de foco bem visível a 3 m; a pasta atual (último segmento) e o atalho da pasta atual nunca recebem o foco nem
      recarregam, nem com clique/toque.
- [ ] Sul num segmento de cima ou num atalho navega e o foco volta à lista (no filho de onde veio, ou no primeiro
      item); Leste/baixo voltam à lista com o foco onde estava. Em lista e em grade.
- [ ] 1920×1080, 1280×720 e 1280×800, em lista e grade: a barra e os glifos cabem, nada é cortado.

## Abas (#50)

- [ ] Com uma aba, a faixa não aparece. Com duas ou mais, aparece ao lado do logo sem legenda de botão.
- [ ] Com o controle: L1/R1 na lista, depois Cima, mostra o anel de foco na aba ativa; L1/R1 trocam de aba e a lista
      abaixo muda na hora; baixo ou Leste voltam para a lista. O rodapé mostra "Aba anterior / Próxima aba /
      Nova/fechar aba". Menu → Abas cria, fecha e troca de aba.
- [ ] Cinco abas com nomes longos em 1280×720: a faixa não empurra a lista para fora da tela; nomes cortados com "…".
- [ ] Clique/toque numa aba troca para ela; o Narrador lê "Aba N de M: nome, ativa".

## Ir para caminho (#54)

- [ ] Com o controle: Menu → Ir para caminho…; digitar `C:\Windows` só com o teclado virtual (página de símbolos para `:`
      e `\`) e Concluir abre a pasta.
- [ ] Caminho inexistente mostra "Pasta não encontrada" no teclado, sem fechá-lo; corrigir e Concluir navega.
- [ ] Teclado físico: colar com Ctrl+V um caminho copiado com "Copiar como caminho" do Explorador (com aspas) funciona.

## Tamanho de pasta (#55)

- [ ] Propriedades → Calcular tamanho numa pasta grande (ex.: C:\Windows\WinSxS): a lista e o controle continuam
      respondendo; Leste cancela em menos de um segundo e mostra o parcial.
- [ ] O total bate com Propriedades do Explorador ("Tamanho", não "Tamanho em disco") para uma pasta comum.

## Filtros da busca (#47)

- [ ] Com o controle: nos resultados, Norte abre os filtros; Sul em "Imagens" e "Vídeos" marca os dois (✓ visível e lido
      pelo Narrador) sem fechar o menu; a lista atrás muda na hora.
- [ ] Com uma busca grande ainda em andamento, ligar um filtro não reinicia a busca; os novos resultados já chegam filtrados.

## OneDrive sob demanda (#126)

- [ ] Com "Arquivos sob demanda" ligado e uma pasta do OneDrive só na nuvem (ícone de nuvem no Explorador), buscar um
      nome que existe numa subpasta dela: o resultado aparece.
- [ ] Depois da busca, os arquivos continuam "somente online" no Explorador (nada foi baixado); o mesmo vale para
      Propriedades → Calcular tamanho na pasta do OneDrive.
- [ ] Uma junção criada dentro do OneDrive (`mklink /J`) continua sem ser percorrida.

## Lista: estados e densidade (#28)

- [ ] Captura de tela da lista convertida para tons de cinza: item focado, item marcado, item focado **e** marcado, item
      recortado e entrada com senha são distinguíveis.
- [ ] Nome longo: com reticências fora do foco; no foco aparece inteiro (até três linhas) sem sobrepor a linha seguinte.
- [ ] Menu → Densidade da lista: compacta mostra colunas de tipo, tamanho e data alinhadas; confortável volta às duas
      linhas. Fechar e abrir o app mantém a escolha.
- [ ] 1280×720, 1920×1080 e 4K (100–200%) nas duas densidades: colunas não se sobrepõem e o texto fica legível a 3 m
      no modo confortável.

## Lista em colunas (redesenho, fase C1)

Não validado em hardware (controle real, TV a ~3 m, DPI real, mouse).
- [ ] 1920×1080, 1280×720 e 4K (100/200/300%): cabeçalho alinhado com as colunas das linhas, nas densidades
      confortável e compacta; seta da ordenação no título certo; capturas `1-home`, `2-folder`, `2c-folder-sorted-size`
      e `3-folder-compact` do `--render-screens`.
- [ ] Controle real: linha focada legível a distância (fundo azul, borda ciano, seta ciano); X marca e a caixa acende
      âmbar sem mover o foco; foco nunca marca.
- [ ] Mouse: clicar em "Tamanho" ordena por tamanho, clicar de novo inverte (Menu → Ordem acompanha); clicar na caixa do
      cabeçalho marca todos / limpa; clicar numa linha abre como Sul.
- [ ] Início em lista: "Pasta do sistema" com o tamanho real das pastas principais ("Calculando…" até terminar), sem
      travar a navegação.
- [ ] Abrir uma pasta pelo meio de outra lista: o foco começa no primeiro item; Voltar/Esquerda focam a pasta de origem.
- [ ] Janela estreita: a coluna de tipo some antes do nome ficar ilegível.

## Painel de detalhes (redesenho, fase C2)

Não validado em hardware (controle real, TV a ~3 m, DPI real).
- [ ] 1920×1080 e 4K (100/200%): painel à direita com ícone grande, nome, tipo e linhas legíveis; capturas `1-home`,
      `2d-details-folder`, `2e-details-image`, `2f-details-archive` do `--render-screens` conferem.
- [ ] 1280×720/800 (portátil) e 4K a 300%: o painel sai e a lista usa a largura toda (sem espremer o nome).
- [ ] Percorrer rápido uma pasta com muitas subpastas grandes segurando o direcional: nada trava; só a pasta em que o
      foco parar é somada ("Calculando…" e depois os valores); voltar a ela não soma de novo por 10 minutos.
- [ ] Foto grande real (JPG de celular 12 MP): miniatura aparece em segundo plano, na orientação certa; imagem acima dos
      limites mostra "Sem miniatura: …" sem decodificar.
- [ ] ZIP/7z com milhares de entradas: contagem de arquivos em até ~3 s ou omitida; RAR/TAR.GZ sem contagem (só formato).
- [ ] Unidade (Meu computador em lista, início): sistema de arquivos, capacidade, livre, usado e barra de uso reais.
- [ ] Narrador: o painel tem nome "Detalhes: …" com as linhas.

## Painel de detalhes na grade (#177)

Não validado em hardware (controle real, TV a ~3 m, DPI real).
- [ ] 1920×1080 e 4K (100/200%): grade com o painel à direita, cartões inteiros (sem cortar nome, tipo ou seta) nas
      colunas que sobram; capturas `1c-home-grid`, `3c-folder-grid` e `3d-folder-grid-details-toggled` conferem.
- [ ] 1280×720/800: sem painel por padrão; Menu → Mostrar painel de detalhes abre o painel mais estreito e a grade fica
      com uma coluna inteira (captura `3d` em 1280x720); Ocultar devolve as colunas.
- [ ] Controle real: segurar o direcional pela grade com o painel à mostra não trava; só a pasta em que o foco parar é
      somada; o foco nunca vai para o painel.
- [ ] Mostrar/Ocultar no meio de uma pasta longa rolada: o mesmo cartão continua focado e à vista, com as marcas.
- [ ] Marcar itens e subir para a barra superior (LB): o painel mostra "N itens marcados · Nenhum item em foco".
- [ ] Fechar e abrir o app: lista e grade mantêm cada uma a sua escolha do painel.

## Grade (#29)

- [ ] Menu → Exibição: grade (e Ctrl+G): o item focado continua o mesmo nas duas direções; fechar e abrir o app mantém.
- [ ] Controle real: direcional e analógico andam em 2D; na ponta da linha passam para a linha seguinte/anterior;
      descer para a última linha incompleta vai ao último item; gatilhos paginam; LB entra na barra de caminho.
- [ ] Pasta com 10.000 arquivos em grade: rolagem sem travar enquanto os ícones grandes aparecem.
- [ ] 1280×720, 1080p e 4K (100–300%), confortável e compacta: cartões (fase B2) sem cortes, foco inteiro (borda ciano,
      halo, leve aumento) sem cobrir o vizinho, nome com reticências; colunas 3 (1080p; 2 com o painel de detalhes), 2 (portátil), 1 (estreita),
      4 (TV 4K); as capturas `3b-folder-grid-compact` e `3c-folder-grid` do `--render-screens` conferem.
- [ ] Busca e Lixeira em grade: a terceira linha do cartão mostra "em <pasta>" quando não há estado.
- [ ] Redimensionar a janela: o número de colunas acompanha e a navegação usa as colunas que aparecem.
- [ ] Narrador lê nome e estado do bloco focado.

## Lixeira (#26)

- [ ] Excluir arquivos e uma pasta pelo ControlFS → Início → Lixeira: aparecem com a pasta de origem e a data; o ícone
      da Lixeira no início segue o do Windows (vazia/cheia).
- [ ] Restaurar um arquivo cuja pasta original foi apagada: a pasta é recriada e o arquivo volta; o Explorador de
      Arquivos deixa de mostrá-lo na Lixeira.
- [ ] Restaurar quando já existe um item com o mesmo nome no local original: aviso, nada sobrescrito, item continua na
      Lixeira.
- [ ] Excluir permanentemente (um e vários marcados): o diálogo abre em Cancelar; Voltar não apaga.
- [ ] Pendrive com Lixeira (formatado em NTFS): itens dele aparecem e restauram.
- [ ] Controle real e Narrador: Sul/A abre Restaurar/Excluir; o Narrador lê nome e local original.

## Ícones do Windows (#24)

- [ ] Início: Downloads, Documentos, Área de trabalho, Imagens, Vídeos e Músicas com o ícone próprio; unidade fixa, pendrive,
      leitor óptico e unidade de rede com ícones diferentes.
- [ ] Pasta com .txt, .pdf, .docx, .zip, .exe e .ico: cada um com o ícone do programa associado; .exe com o ícone do próprio programa.
- [ ] Dentro de um ZIP: ícones por extensão (nenhum emoji na lista).
- [ ] Pasta com 10.000 arquivos: rolar com o gatilho e o analógico; a rolagem não trava enquanto os ícones aparecem.
- [ ] 1080p 100%, 4K 200% e portátil (1280×800 a 150%): ícones nítidos, sem serrilhado; mover a janela para um monitor
      com outra escala troca os ícones para o novo tamanho.
- [ ] Anel de foco e marcação continuam legíveis sobre os ícones.

## Atalhos de jogos da Steam e .lnk (#168)

Não validado em hardware (Steam real, controle real). Evidência automática: capturas `6-shortcuts-list` e
`6b-shortcuts-grid` do `--render-screens` (atalhos de exemplo com ícones de um .dll do Windows, um sem ícone, um site
e um .lnk); `ShortcutIconIntegrationTests` extrai ícones reais de .ico/.dll e de .lnk no runner.

- [ ] Área de trabalho com atalhos reais criados pela Steam (ex.: Valheim, Dead Space): em lista e em grade, título sem
      ".url", tipo "Jogo da Steam" e o ícone **do próprio jogo** (não globo, não logo da Steam, não documento em branco).
- [ ] Dois jogos com ícones diferentes aparecem diferentes; trocar Grade/Lista e atualizar (F5) mantém os ícones e o foco.
- [ ] Apagar/renomear o `.ico` de um jogo em `Steam\steam\games`: o atalho mostra o símbolo de jogo, a navegação não
      trava e Abrir ainda inicia o jogo pela Steam.
- [ ] Com controle: focar o jogo, Sul → "Abrir este jogo da Steam?" começa em Cancelar; Jogar inicia o jogo pela Steam.
- [ ] Sem a Steam instalada (ou num PC sem ela): Abrir mostra "A Steam não está instalada…" sem travar.
- [ ] Atalho de site (`https://`) abre no navegador padrão e não aparece como jogo.
- [ ] `.lnk` na Área de trabalho (ex.: atalho de um programa) mostra o ícone do programa, não o documento em branco.
- [ ] Painel de detalhes e Ações → Propriedades de um jogo: nome real "Valheim.url", o `steam://rungameid/…` e o tipo real.
- [ ] Narrador: "Valheim, jogo da steam, …" ao focar o atalho.

## Tipos de unidade (#25)

- [ ] Início com o app aberto: conectar um pendrive faz ele aparecer em até ~2 s, com ícone de USB e "Removível (USB)";
      removê-lo tira da lista sem reiniciar e o foco fica num local válido.
- [ ] Leitor de cartão vazio → inserir cartão: a unidade aparece. Leitor óptico com disco: ícone óptico e "Óptica".
- [ ] Unidade de rede mapeada (`net use Z: \\servidor\pasta`): aparece com ícone de rede; desconectar o servidor não
      trava o início.
- [ ] Narrador lê o nome e o tipo da unidade (ex.: "PENDRIVE (E:), unidade removível (USB)").
- [ ] Seletor de pasta → "Ir para outro local": cada unidade mostra tipo, espaço livre e total.

## Busca (#46)

- [ ] Select/View numa pasta abre o teclado; Concluir inicia a busca e os primeiros resultados aparecem antes de ela terminar.
- [ ] Árvore com ~50.000 arquivos (ex.: `C:\Windows\WinSxS` ou uma pasta gerada), termo que casa com muitos itens: a lista e o
      rodapé continuam respondendo ao direcional e ao analógico durante a busca; Leste/B cancela na hora e mantém o parcial.
- [ ] `C:\` com subpastas: pastas sem permissão aparecem no rodapé ("pastas … puladas") e em Norte → Pastas puladas.
- [ ] Sul/A num resultado abre a pasta dele com o foco no item; Voltar volta aos resultados com o foco no mesmo item.
- [ ] Menu → "Busca em subpastas: não incluir": a busca seguinte só lê a pasta atual.
- [ ] OneDrive com arquivos sob demanda: pastas marcadas como ponto de nova análise não são percorridas (limitação conhecida; anotar o que aparece).

## Navegador de compactados (#68)

- [ ] Abrir um ZIP com senha e links (ou um RAR com arquivos protegidos): o cabeçalho mostra formato, arquivos, tamanho,
      "N com senha" e "N bloqueadas", legível a ~3 m numa TV e sem cortar o caminho em 1280×720.
- [ ] As linhas mostram "compactado: … (N%)", o cadeado nas entradas com senha e "Bloqueado: motivo" nas bloqueadas; o
      Narrador lê o estado.
- [ ] Marcar uma entrada: o rodapé mostra "Extrair seleção (1)" com o glifo do botão Norte do controle em uso.

## Formatos, compactar e abrir com o Windows

- [ ] Abrir e extrair um RAR e um 7z reais baixados da internet (conferir a marca de origem nos extraídos).
- [ ] Compactar uma pasta com fotos em ZIP e abrir o ZIP no Explorador do Windows.
- [ ] Sul num .pdf/.jpg abre no programa padrão; voltar ao ControlFS com Alt+Tab.
- [ ] "Abrir com…" mostra a caixa do Windows e usa o programa escolhido.
- [ ] Sul num .exe pede confirmação começando em "Cancelar".

## Instalador e atualizações

- [ ] Instalar `ControlFS-Setup-x64.exe` numa conta padrão (sem pedir administrador); atalho no menu Iniciar aparece.
- [ ] Com a versão anterior instalada e uma nova publicada: abrir o app, ver "Atualização X pronta", escolher
      **Instalar e reiniciar** e confirmar que o app reabre na nova versão com as preferências mantidas.
- [ ] Escolher **Depois**, sair do app e confirmar que a nova versão está instalada na próxima abertura.
- [ ] Desligar "Verificar automaticamente" e confirmar (monitor de rede) que nenhuma requisição é feita ao abrir.
- [ ] Sem internet: o app abre normalmente e "Verificar agora" mostra erro claro.
- [ ] Desinstalar pelo Windows: arquivos do app e cache de atualizações removidos; `settings.json` preservado.
- [ ] Windows 10 22H2 limpo, sem Windows App Runtime instalado: o portátil e o instalado abrem, mostram a lista, o teclado
      virtual e os ícones (#85: só os componentes WinUI/Foundation do Windows App SDK vão no pacote).

## Teste de controles (#78)

Não precisa do SDK do .NET: use o instalador ou o portátil da release. Controles da matriz: Xbox Wireless/Series (USB e
Bluetooth), DualShock 4, DualSense, Switch Pro, 8BitDo (em cada modo X/D/S que tiver) e um genérico USB.

1. Abra o ControlFS e conecte **um** controle. Menu (Start ou F10) → **Teste de controles…**.
2. Confira a linha do controle: nome, `SDL type`, `family` (Xbox, PlayStation, Nintendo ou Generic), `VID:PID`,
   gamepad ou joystick cru, e `ACTIVE` depois de apertar um botão.
3. Aperte, soltando entre um e outro: direcional ↑ ↓ ← →, analógico esquerdo ↑ ↓ ← →, Sul, Leste, Oeste, Norte, LB, RB,
   LT, RT, Start e Select. Cada pressão aparece em destaque como `controle físico → ação`. Esperado (Confirmar com botão
   inferior): direções → `Navigate…`, Sul → `Confirm`, Leste → `Back`, Oeste → `ToggleSelection`, Norte →
   `OpenContextMenu`, LB/RB → `PreviousRegion`/`NextRegion`, LT/RT → `PageUp`/`PageDown`, Start → `OpenAppMenu`,
   Select → `Search`. Confira também se o nome entre parênteses é o impresso no botão (ex.: `South (Botão cruz)` no DualSense).
4. Segure **Confirmar 1 s** (ou Enter, ou toque em "Copiar relatório"): o aviso "Relatório copiado" aparece.
5. Cole num comentário da issue [#78](https://github.com/nextestudios/ControlFS/issues/78) e escreva em cima o
   transporte (USB, Bluetooth ou receptor), o modo do controle (8BitDo) e qualquer coisa estranha (botão que não
   aparece, direção trocada, desconexão).
6. Segure **Voltar 1 s** (ou Esc) para sair. Repita para cada controle e transporte.

- [ ] Relatório colado não tem nome de usuário, caminho do dispositivo, GUID nem número de série.
- [ ] Com dois controles conectados, apertar um botão no outro marca-o como `ACTIVE`; desconectar um deixa a linha como `disconnected`.
- [ ] Joystick genérico sem perfil: as pressões aparecem como `botão N`/`direcional N ↑`/`eixo N ±` → `nenhuma ação`, e
      sair com Esc funciona.

## Controle ativo e duplicatas (#80)

- [ ] Com o Steam aberto (Steam Input ligado para controles PlayStation/Nintendo) e um DualSense ou Switch Pro conectado: o rodapé avisa que "Steam Virtual Gamepad" parece uma cópia; Menu → Controle ativo mostra os dois, com `virtual (Steam Input)` na cópia.
- [ ] Escolher um deles com Sul/A: navegar não pula dois itens por pressão, e botões do outro não fazem nada (nem ocioso). Escolher **Automático** volta a deixar qualquer um assumir.
- [ ] DS4Windows (ViGEm) com um DualShock 4: o Xbox 360 emulado aparece como `provável DS4Windows/ViGEm` e escolher um deles elimina a ação dupla.
- [ ] Desconectar o controle escolhido: o rodapé avisa e outro controle assume com uma nova pressão.
- [ ] Com o controle escolhido, abrir uma confirmação sensível (ex.: excluir): outro controle continua sem assumir; no automático, a regra antiga vale (nenhum outro assume dentro da confirmação).

## Controles da matriz

Xbox (USB/BT), DualShock 4, DualSense, Switch Pro, 8BitDo, um genérico. Para cada um: navegação, confirmar/voltar na
posição certa, gatilhos (página), Start (menu), reconexão, família detectada no teste de controles e legendas certas em "Legendas: automáticas".

- [ ] Troca a quente: com um Xbox ativo, apertar um botão num DualSense passa o comando para ele e as legendas mudam
      sem reiniciar; voltar ao Xbox faz o mesmo.
- [ ] Com um controle ativo, apertar uma seta do teclado: o rodapé passa a mostrar teclas (Enter, Esc…); apertar um
      botão do controle volta aos glifos.
- [ ] Rodapé por contexto com controle: pasta, compactado (Explorar/Extrair…), dentro do compactado, itens marcados
      (Operações (N)/Cancelar seleção), teclado virtual (Selecionar/Apagar/Concluir/Cancelar) e diálogos; cada legenda faz
      o que diz.
- [ ] Trocar "Confirmar com" no menu: o glifo de Abrir passa para o botão direito e o de Voltar para o inferior.
- [ ] Segurar o direcional no controle ativo e apertar um botão no outro: o outro **não** assume até o primeiro ser solto.
- [ ] Menu → Legendas fixada em "PlayStation" com um Xbox ativo: legendas continuam PlayStation.

## Joystick sem perfil (assistente, #79)

A CI só prova a máquina de estados, a validação dos perfis e o fluxo com eventos crus simulados. Com um controle USB
genérico **não reconhecido como gamepad** (registre o resultado em `controller-compatibility.md`):

- [ ] Conectar com o app aberto: apertar um botão mostra o aviso "segure qualquer botão…"; segurar 2 s abre o assistente.
- [ ] Neutro: com tudo solto o assistente avança em ~1 s; segurando um botão ele espera soltar.
- [ ] Mapear direções num hat e, em outro controle, num analógico (inclusive um eixo invertido); cada passo só avança
      depois de soltar; apertar de novo uma entrada já usada é recusado.
- [ ] Um eixo que repousa em -1 (gatilho) não dispara sozinho e funciona como botão quando mapeado.
- [ ] Pular opcionais com o Voltar do próprio joystick; refazer com ← do teclado e, no teste, por "Refazer um passo…".
- [ ] Teste antes de salvar: direções movem o foco, Confirmar escolhe, Voltar pergunta antes de descartar.
- [ ] Ficar 20 s sem tocar em nada: o assistente fecha com "nada foi salvo".
- [ ] Desconectar o joystick no meio: o assistente fecha sem salvar e o app continua respondendo ao teclado.
- [ ] Salvar, fechar o ControlFS e reabrir: o joystick navega sem configurar de novo; reconectar em outra porta USB também.
- [ ] Com perfil salvo, configurar de novo e cancelar (Esc e "Cancelar" na confirmação): o perfil antigo continua valendo.
- [ ] Mapear só direções, confirmar e voltar (pular todos os opcionais): segurar Confirmar ~0,6 s abre Ações, segurar
      Voltar abre o Menu, pressões curtas confirmam/voltam como antes e o rodapé mostra "Ações (segure)" no glifo de
      Confirmar e "Menu (segure)" no de Voltar.
- [ ] Exportar para uma pasta, importar em outro computador (ou após apagar `controllers\` na pasta de dados) e usar.
- [ ] Importar um `.json` qualquer (não perfil) e um perfil editado com um campo extra: ambos recusados com mensagem clara.

## Início em grade e Meu computador (redesenho, fase B1)

Não validado em hardware. Capturas do Smoke: `1c-home-grid`, `1d-home-grid-drives`, `1e-this-pc-grid`, `1f-this-pc-list`.

- [ ] PC real com Downloads/Documentos grandes e OneDrive: os cartões mostram "Calculando…" e depois "N itens • tamanho"
      sem travar a navegação; nenhum arquivo do OneDrive sob demanda é baixado; voltar ao início em menos de 10 minutos
      não recalcula (disco quieto).
- [ ] Pasta com centenas de milhares de arquivos: depois de ~20 s o cartão mostra o parcial com "+".
- [ ] Unidades: rótulo, barra de uso e "X livres de Y · NTFS" batem com o Explorador; pendrive (exFAT/FAT32) e unidade
      de rede mapeada aparecem com o tipo; conectar um pendrive com o início aberto adiciona o cartão sem perder o foco.
- [ ] Controle real: direcional passa de "Pastas principais" para "Unidades e dispositivos" e volta na mesma coluna;
      LT/RT vão ao começo da seção anterior/seguinte; o cartão focado (borda ciano, fundo azul, halo, leve aumento) fica
      à vista ao rolar.
- [ ] 1920×1080, 1280×720 (portátil), 4K a 100/150/200% e janela estreita: 3/2/1 colunas de pastas, unidades em 2 (ou 1
      no portátil), nada cortado, textos legíveis a 3 m na TV.
- [ ] Meu computador (acesso rápido e raiz do caminho): unidades em cartões na grade e em linhas na lista; Sul abre,
      Voltar retorna com o foco na mesma unidade; Norte → Propriedades mostra capacidade, livre, usado e sistema de
      arquivos.
- [ ] Narrador no início em grade lê nome, caminho e "N itens • tamanho" (ou "Calculando…"); nas unidades, o espaço livre.

## Redesenho: tokens, rodapé e R3 (fase A)

Inventário e matriz de regressão: `docs/ui-redesign.md`. Não validado em hardware.

- [ ] Xbox, DualSense, Switch Pro e um controle genérico: apertar o analógico direito (R3) alterna lista ↔ grade nas
      pastas, no início, na busca e na Lixeira, mantendo o item focado; dentro de menus, diálogos e do teclado virtual
      não faz nada. O rodapé mostra "Grade" na lista e "Lista" na grade, com o glifo do analógico pressionado.
- [ ] TV 1080p a ~3 m: o anel ciano com halo e o fundo azul do item focado são visíveis; a transição do fundo é curta
      (não pisca nem atrasa a navegação com o direcional mantido).
- [ ] Rodapé: A verde, B vermelho, X azul e Y amarelo legíveis (letra contrastando), na ordem Abrir · Voltar · Marcar ·
      Ações · Menu · Buscar · Lista/Grade; com PlayStation/Nintendo/genérico os glifos continuam os da família.
- [ ] Capturas do Smoke (`smoke-screens`, 1920×1080 e 1280×720) comparadas com as referências do redesenho.

## Glifos dos botões

- [ ] Com Legendas fixadas em cada estilo (genéricas, Xbox, PlayStation, Nintendo) e um controle ativo, o rodapé mostra
      glifos desenhados (não texto): faces, ombros/gatilhos (LB/RB/LT/RT, L1/R1/L2/R2, L/R/ZL/ZR), Menu/Exibir,
      Options/Create, +/−. Nintendo: A à direita e B embaixo.
- [ ] Glifos nítidos e alinhados ao texto do rodapé em 1080p e 4K, com escala 100%, 150%, 200% e 300%.
      Referência automática: galeria `glyphs/` no artefato `smoke-screens` do workflow Smoke (ver "Layout responsivo").
- [ ] Narrador lê o nome do botão (ex.: "Botão cruz", "Botão A", "Botão Menu").

## Pausar e continuar (#21)

- [ ] Copiar vários GB para um pendrive lento; Menu → Operações → a cópia → Pausar: a luz do pendrive para em até ~1 s
      e o Gerenciador de Tarefas mostra o disco sem atividade do ControlFS. No destino só existe o temporário oculto
      `.controlfs-copy-*.part` do arquivo atual.
- [ ] Continuar: a cópia termina e os arquivos abrem normalmente (comparar tamanho/hash de um arquivo grande).
- [ ] Pausar e depois Cancelar (sem continuar): o temporário some, os arquivos já concluídos ficam.
- [ ] Extração e compactação não mostram "Pausar".

## Desfazer e refazer (#22)

- [ ] Mandar um arquivo para a Lixeira real do Windows e Menu → Desfazer: ele volta ao local original e sai da Lixeira
      (conferir no Explorador). Refazer manda de novo.
- [ ] Mover uma pasta grande para outra unidade e desfazer: volta inteira, com a origem igual à de antes.
- [ ] Copiar para um pendrive e desfazer: a cópia vai para a Lixeira (ou é excluída, se o pendrive não tiver Lixeira) e o
      original continua intacto.

## Limpeza após queda

- [ ] Durante uma extração grande (e, em outra rodada, uma cópia grande), encerrar o ControlFS pelo Gerenciador de
      Tarefas. Reabrir: o rodapé avisa "Limpeza: … removido(s)" e a pasta oculta `.controlfs-staging-*` (ou o arquivo
      `.controlfs-copy-*.part`) sumiu do destino; nada mais na pasta mudou.
- [ ] Com duas janelas do ControlFS abertas, iniciar uma extração numa e reabrir a outra no meio: a extração em andamento
      não é afetada (os temporários de uma instância viva nunca são limpos).

## Visualização de imagens (#57)

- [ ] Sul numa foto de celular (JPG com EXIF de rotação): abre em pé, sem travar; o nome, "N de M" e a resolução aparecem.
- [ ] Imagem grande (ex.: 12000 × 6000, ~60 MB): o rodapé e o controle continuam respondendo enquanto carrega; depois
      abre nítida em 4K (100% e 200%).
- [ ] Esquerda/Direita e LB/RB seguem a ordem da lista; no início/fim o rodapé avisa; ao fechar, o foco fica na última imagem.
- [ ] RT/LT dão zoom até 800%; com zoom, o direcional percorre a imagem sem passar das bordas; Sul volta a ajustar.
- [ ] PNG com transparência, GIF animado (primeiro quadro), BMP e WebP (Windows 11) aparecem corretamente.
- [ ] Um `.exe` renomeado para `.png` e uma imagem acima de 80 megapixels mostram o motivo da recusa, sem abrir nada.
- [ ] O Narrador lê o nome da imagem, a posição e a resolução.

## Visualização de texto (#58)

- [ ] Sul num `.log` de 50 MB: a tela não trava, aparece o aviso de prévia parcial (primeiras 10.000 linhas) e a rolagem
      com Cima/Baixo mantidos, LT/RT e LB/RB é fluida.
- [ ] Arquivos do Bloco de Notas em UTF-8, UTF-8 com BOM, UTF-16 e ANSI (acentos corretos em PT-BR), e o nome da codificação no topo.
- [ ] Linha muito longa (JSON minificado): Esquerda/Direita deslocam; nada quebra o layout em 1080p e 4K a ~3 m.
- [ ] Sul alterna fonte fixa/proporcional; Norte → Visualizar como texto num `.ps1` mostra o script sem executar.
- [ ] Um `.exe` ou `.png` pelo menu "Visualizar como texto" é recusado como binário.
- [ ] O Narrador lê as linhas visíveis e a posição.

## Sugestões do teclado (#45) — não validado em hardware
- [ ] Com controle real: em Nova pasta, digitar parte de um nome da pasta → faixa aparece; cima na primeira linha foca, Sul usa, baixo volta; cima na faixa leva à última linha.
- [ ] Senha de compactado: nenhuma faixa aparece; após concluir, o histórico (Menu → Sugestões do teclado) não contém a senha.
- [ ] Ir para caminho: favoritos e pastas recentes aparecem como sugestões; legível a 3 m e em 1280×720.
