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

## Controles da matriz

Xbox (USB/BT), DualShock 4, DualSense, Switch Pro, 8BitDo, um genérico. Para cada um: navegação, confirmar/voltar na
posição certa, gatilhos (página), Start (menu), reconexão, família detectada no probe e legendas certas em "Legendas: automáticas".

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
- [ ] Exportar para uma pasta, importar em outro computador (ou após apagar `controllers\` na pasta de dados) e usar.
- [ ] Importar um `.json` qualquer (não perfil) e um perfil editado com um campo extra: ambos recusados com mensagem clara.

## Glifos dos botões

- [ ] Com Legendas fixadas em cada estilo (genéricas, Xbox, PlayStation, Nintendo) e um controle ativo, o rodapé mostra
      glifos desenhados (não texto): faces, ombros/gatilhos (LB/RB/LT/RT, L1/R1/L2/R2, L/R/ZL/ZR), Menu/Exibir,
      Options/Create, +/−. Nintendo: A à direita e B embaixo.
- [ ] Glifos nítidos e alinhados ao texto do rodapé em 1080p e 4K, com escala 100%, 150%, 200% e 300%.
- [ ] Narrador lê o nome do botão (ex.: "Botão cruz", "Botão A", "Botão Menu").
