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
- [ ] Trocar "Confirmar com" no menu: o glifo de Abrir passa para o botão direito e o de Voltar para o inferior.
- [ ] Segurar o direcional no controle ativo e apertar um botão no outro: o outro **não** assume até o primeiro ser solto.
- [ ] Menu → Legendas fixada em "PlayStation" com um Xbox ativo: legendas continuam PlayStation.

## Glifos dos botões

- [ ] Com Legendas fixadas em cada estilo (genéricas, Xbox, PlayStation, Nintendo) e um controle ativo, o rodapé mostra
      glifos desenhados (não texto): faces, ombros/gatilhos (LB/RB/LT/RT, L1/R1/L2/R2, L/R/ZL/ZR), Menu/Exibir,
      Options/Create, +/−. Nintendo: A à direita e B embaixo.
- [ ] Glifos nítidos e alinhados ao texto do rodapé em 1080p e 4K, com escala 100%, 150%, 200% e 300%.
- [ ] Narrador lê o nome do botão (ex.: "Botão cruz", "Botão A", "Botão Menu").
