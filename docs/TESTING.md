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

## Layout responsivo (#36)

A faixa de layout sai do tamanho **efetivo** da janela (pixels ÷ escala do Windows) e do tamanho do texto do Windows
(`LayoutBreakpoints`, testado na CI): **compacta** abaixo de 900 de altura ou 1360 de largura "em texto" (portáteis
1280×720/800, 1080p a 150%, 4K a 300%), **normal** (1080p a 100%, 4K a 200%) e **grande** a partir de 1300 de altura
(4K a 100–125%: texto e espaços crescem até 2×, com as proporções de 1080p). O texto do Windows maior reduz o espaço
"em texto" e pode deixar o layout compacto; o próprio WinUI aumenta cada texto.

Evidência automática: o workflow **Smoke** (manual) roda `ControlFS-Portable-x64.exe --render-screens <pasta>`, que
monta as telas reais (início, pasta, lista compacta, menu com foco abaixo da dobra, teclado virtual) em 1280×720,
1280×800 (e com texto a 150%), 1920×1080 (100% e 150%) e 3840×2160 (100%, 200% e 300%), além da galeria de glifos
(escuro/claro, 100/200/300%), e publica os PNGs no artefato `smoke-screens` com um `report.txt` das alturas medidas.
A resolução é simulada (a tela do runner é pequena); o modo usa uma pasta temporária, não acessa a rede e fecha sozinho.

Ainda manual, num aparelho real:

- [ ] Portátil 1280×800 (e 1280×720) a 100% e 125%: cabeçalho, lista, rodapé e todos os menus visíveis; nenhuma
      legenda do rodapé some (quebram linha); no menu principal o item focado sempre aparece ao descer até "Sair".
- [ ] TV 1080p e TV 4K (100% e 300%) a ~3 m: nomes da lista, rodapé e teclado virtual legíveis; anel de foco visível.
- [ ] Windows → Acessibilidade → Tamanho do texto em 150% e 200% com o app aberto: o layout se ajusta na hora, nada
      essencial fica cortado (menus rolam até o item focado).
- [ ] Arrastar a janela entre um monitor 100% e outro 150%/200%: textos e ícones nítidos, sem reiniciar.

## Barra de caminho (#30)

- [ ] Com o controle: LB na lista leva o foco para a barra; esquerda/direita; Sul numa pasta de cima navega e foca a
      pasta de origem; RB volta para a lista. Mesmo com Ctrl+←/→ no teclado.
- [ ] Dentro de um ZIP em subpasta: o `▸` e o ícone de pacote separam disco e compactado; escolher a pasta do disco
      foca o arquivo .zip.
- [ ] Caminho longo (8+ níveis) em 1280×720: o meio vira `…`, nada sai da tela; Sul no `…` mostra as pastas escondidas.
- [ ] Clique/toque num segmento navega até ele.

## Lista: estados e densidade (#28)

- [ ] Captura de tela da lista convertida para tons de cinza: item focado, item marcado, item focado **e** marcado, item
      recortado e entrada com senha são distinguíveis.
- [ ] Nome longo: com reticências fora do foco; no foco aparece inteiro (até três linhas) sem sobrepor a linha seguinte.
- [ ] Menu → Densidade da lista: compacta mostra colunas de tipo, tamanho e data alinhadas; confortável volta às duas
      linhas. Fechar e abrir o app mantém a escolha.
- [ ] 1280×720, 1920×1080 e 4K (100–200%) nas duas densidades: colunas não se sobrepõem e o texto fica legível a 3 m
      no modo confortável.

## Ícones do Windows (#24)

- [ ] Início: Downloads, Documentos, Área de trabalho, Imagens, Vídeos e Músicas com o ícone próprio; unidade fixa, pendrive,
      leitor óptico e unidade de rede com ícones diferentes.
- [ ] Pasta com .txt, .pdf, .docx, .zip, .exe e .ico: cada um com o ícone do programa associado; .exe com o ícone do próprio programa.
- [ ] Dentro de um ZIP: ícones por extensão (nenhum emoji na lista).
- [ ] Pasta com 10.000 arquivos: rolar com o gatilho e o analógico; a rolagem não trava enquanto os ícones aparecem.
- [ ] 1080p 100%, 4K 200% e portátil (1280×800 a 150%): ícones nítidos, sem serrilhado; mover a janela para um monitor
      com outra escala troca os ícones para o novo tamanho.
- [ ] Anel de foco e marcação continuam legíveis sobre os ícones.

## Busca (#46)

- [ ] Select/View numa pasta abre o teclado; Concluir inicia a busca e os primeiros resultados aparecem antes de ela terminar.
- [ ] Árvore com ~50.000 arquivos (ex.: `C:\Windows\WinSxS` ou uma pasta gerada), termo que casa com muitos itens: a lista e o
      rodapé continuam respondendo ao direcional e ao analógico durante a busca; Leste/B cancela na hora e mantém o parcial.
- [ ] `C:\` com subpastas: pastas sem permissão aparecem no rodapé ("pastas … puladas") e em Norte → Pastas puladas.
- [ ] Sul/A num resultado abre a pasta dele com o foco no item; Voltar volta aos resultados com o foco no mesmo item.
- [ ] Menu → "Busca em subpastas: não incluir": a busca seguinte só lê a pasta atual.
- [ ] OneDrive com arquivos sob demanda: pastas marcadas como ponto de nova análise não são percorridas (limitação conhecida; anotar o que aparece).

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

## Glifos dos botões

- [ ] Com Legendas fixadas em cada estilo (genéricas, Xbox, PlayStation, Nintendo) e um controle ativo, o rodapé mostra
      glifos desenhados (não texto): faces, ombros/gatilhos (LB/RB/LT/RT, L1/R1/L2/R2, L/R/ZL/ZR), Menu/Exibir,
      Options/Create, +/−. Nintendo: A à direita e B embaixo.
- [ ] Glifos nítidos e alinhados ao texto do rodapé em 1080p e 4K, com escala 100%, 150%, 200% e 300%.
      Referência automática: galeria `glyphs/` no artefato `smoke-screens` do workflow Smoke (ver "Layout responsivo").
- [ ] Narrador lê o nome do botão (ex.: "Botão cruz", "Botão A", "Botão Menu").

## Limpeza após queda

- [ ] Durante uma extração grande (e, em outra rodada, uma cópia grande), encerrar o ControlFS pelo Gerenciador de
      Tarefas. Reabrir: o rodapé avisa "Limpeza: … removido(s)" e a pasta oculta `.controlfs-staging-*` (ou o arquivo
      `.controlfs-copy-*.part`) sumiu do destino; nada mais na pasta mudou.
- [ ] Com duas janelas do ControlFS abertas, iniciar uma extração numa e reabrir a outra no meio: a extração em andamento
      não é afetada (os temporários de uma instância viva nunca são limpos).
