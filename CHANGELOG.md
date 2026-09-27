# Histórico de alterações

Notas em português do Brasil; a versão em inglês (Estados Unidos) fica em `CHANGELOG.en-US.md`. Antes de publicar uma versão, adicione uma seção `## [VERSÃO]` **nos dois arquivos**. O workflow de release usa a seção da tag e falha se faltar alguma.

## [Unreleased]

## [0.5.0-alpha.1]
### Novidades
- **Narrador**: o foco lógico agora é anunciado ao andar com o controle ou o teclado — contexto ao entrar (início, pasta, menu, diálogo, teclado virtual, barra de caminho, abas) e depois só o item, com tipo, tamanho, posição e estados por extenso (marcado, recortado, bloqueado, com senha, indisponível com o motivo). Avisos do rodapé são uma região viva lida sem mover o foco. (#40)
- **Lixeira** no início: lista os itens excluídos com a pasta de origem e a data da exclusão; Sul/A num item oferece **Restaurar** (volta à pasta original, sem nunca sobrescrever) e **Excluir permanentemente**, que sempre pergunta antes com o foco em Cancelar. Marcar vários funciona. Um local original inválido registrado na Lixeira é recusado. (#26)
- **Visualização de texto**: Sul em .txt, .md, .log, .json, .xml, .csv, .ini, código etc. (ou Norte → Visualizar como texto em qualquer arquivo, inclusive scripts, sem executar) abre o arquivo somente leitura. Cima/Baixo rolam, LT/RT paginam, LB/RB vão ao início/fim, Esquerda/Direita deslocam linhas longas e Sul alterna fonte fixa/proporcional. Codificação detectada (UTF-8, UTF-16, ANSI); só os primeiros 2 MB e 10.000 linhas, com aviso de prévia parcial; binários são recusados. (#58)
- **Downloads menores**: o portátil caiu de 97 MB para 72 MB e o instalador de 65 MB para 48 MB, sem os componentes de IA/ML do Windows App SDK que o app não usa. (#85)
- **Exibição em grade**: Menu → Exibição (ou Ctrl+G) alterna lista e grade de ícones grandes nas pastas e no início. Na grade o direcional anda em 2D, sem pular itens e passando de linha nas pontas; os gatilhos paginam. A densidade vale para a grade (blocos menores na compacta), a escolha fica salva e trocar de exibição mantém o item focado. (#29)
- **Visualização de imagens** (JPG, PNG, GIF, BMP, WebP): Sul abre a imagem dentro do app; Esquerda/Direita ou LB/RB passam pelas imagens da pasta, RT/LT dão zoom, o direcional move a imagem ampliada e Leste/B fecha. A decodificação é em segundo plano e reduzida a 4096 px; formato conferido pelo conteúdo e limites de 100 MB e 80 megapixels antes de decodificar. Nada é executado; imagens dentro de compactados não são visualizadas. (#57)
- O Narrador lê o campo do teclado virtual como "Texto: …" com a posição do cursor, e o workflow Smoke passou a conferir pela UI Automation o foco do menu, a opção segura das confirmações, o escopo dos modais e o teclado virtual no app real. (#83)
- **Seleção de texto** no teclado virtual: **Selecionar tudo** na página `…` (ou Ctrl+A), trecho selecionado destacado e sublinhado, digitar substitui, `⌫` apaga e `◀ ▶` desfazem a seleção. Renomear já abre com o nome sem a extensão selecionado (`example-file.zip` → digitar `novo` → `novo.zip`). (#44)
- **Tipos de unidade**: disco local, pendrive/USB, leitor óptico e unidade de rede têm símbolo e texto próprios no início e no seletor de pasta (com rótulo, letra e espaço livre/total), e o Narrador lê o tipo. Conectar ou remover um pendrive com o app aberto atualiza a lista sem reiniciar, mantendo o foco no mesmo local. (#25)
- **Controle ativo**: Menu → Controle ativo lista os controles conectados (nome, família, tipo, VID:PID, físico ou virtual) e Sul/A no escolhido faz só ele comandar o ControlFS até você voltar ao automático. Quando o Steam Input ou o DS4Windows expõem o controle físico e uma cópia virtual ao mesmo tempo, o rodapé avisa da duplicata (cada botão poderia agir duas vezes) e o menu mostra qual parece a cópia. O teste de controles também marca os dispositivos virtuais. (#80)
- **Recentes** no início: as últimas pastas visitadas e os últimos arquivos/compactados abertos (até 10 de cada), para voltar com Início → Recentes → Sul/A. Ficam só neste computador, nas preferências; Norte em "Recentes" limpa as listas, e Menu → "Recentes" desliga (o que também apaga o que estava guardado). (#49)
- **Histórico de operações**: Menu → Operações lista também o que foi feito em aberturas anteriores (cópias, movimentações, exclusões, extrações, compactações e renomeações), com data, origem, destino e desfechos por item. Guardado em JSON versionado na pasta de dados, com gravação atômica, limitado às 200 operações mais recentes e sem senhas nem conteúdo de arquivos; **Limpar histórico…** apaga o registro. (#20)
- **Testar integridade** de um compactado sem extrair (Norte num compactado ou dentro dele): lê todas as entradas e confere o CRC, sem gravar nada, com progresso e cancelamento. O resultado aponta as entradas com falha pelo nome e conta as que não têm checksum (TAR, GZ, ZIP AES AE-2). Não é antivírus. (#66)
- **Extrair vários compactados de uma vez**: marque os compactados e escolha Norte → "Extrair cada um para a própria pasta". Cada um vai para uma pasta nova (conteúdos nunca se misturam), roda como operação própria na fila e um resumo final mostra o resultado de cada um. (#69)
- **Tamanho de pasta sob demanda**: Propriedades → "Calcular tamanho" soma tudo dentro da pasta ou unidade fora da thread de interface, mostrando o parcial; Leste/B cancela na hora e mantém o parcial. Nunca segue junções ou links (contados à parte) e relata as pastas sem acesso. A busca e o cálculo usam o mesmo percurso seguro. (#55)
- **Filtros da busca**: Norte nos resultados abre os filtros de tipo (pastas, imagens, vídeos, áudio, documentos, compactados, executáveis — combináveis), tamanho e data de modificação; Sul liga/desliga sem fechar o menu. A lista é refiltrada na hora, sem refazer a busca, e os filtros valem para as próximas buscas da sessão. As demais ações da busca ficam em "Outras ações da busca…". (#47)
- **Pausar e continuar** cópias, movimentações e exclusões: Menu → Operações → a operação → Pausar/Continuar. Para no próximo ponto seguro (entre itens e entre blocos de um arquivo), sem deixar nada pela metade no destino; Cancelar funciona também durante a pausa. Extração e compactação não oferecem pausa. (#21)
- A busca (e o cálculo de tamanho) agora entram nas pastas do **OneDrive com arquivos sob demanda**, tratadas como pastas comuns: só os nomes são lidos, nada é baixado. Junções, links simbólicos e pontos de montagem continuam recusados. (#126)
- **Desfazer e refazer**: Menu → Desfazer/Refazer (e "Desfazer" no resultado de cópias e movimentações) para renomear, mover, copiar e mandar para a Lixeira. Tudo é conferido antes: se o disco mudou desde a operação (nome ocupado, cópia editada, item fora da Lixeira), nada é feito e o motivo aparece. Uma cópia só é removida se continuar idêntica e o original existir. Exclusão permanente e substituições nunca são desfeitas. (#22)
### Melhorias
- **ZIP64 validado**: compactados ZIP com entradas acima de 4 GB ou mais de 65.535 entradas extraem com tamanho e CRC conferidos; os limites de segurança continuam valendo. (#63)
- **ZIP com criptografia AES (WinZip AE-1/AE-2, 128/192/256 bits) validado**: sem senha o app pede, com a senha certa o conteúdo sai idêntico e a senha errada agora é reconhecida como "Senha incorreta" (pede de novo) em vez de "senha ou dados corrompidos". (#64)
- **Abas** no navegador: cada aba guarda a própria pasta, histórico, marcação e foco. RB leva à faixa de abas, LB/RB trocam de aba e Norte cria ou fecha; Norte numa pasta tem "Abrir em nova aba". Uma cópia ou movimentação atualiza todas as abas que mostram a origem ou o destino. (#50)
- **Ir para caminho…** (Menu, e no seletor de pastas): o teclado virtual abre com a pasta atual selecionada; digite ou cole (Ctrl+V) um caminho, com ou sem aspas e com variáveis como `%USERPROFILE%`, e Concluir vai até lá. Caminho inexistente ou inválido mostra o erro sem fechar o teclado; o caminho de um arquivo abre a pasta dele com o foco no arquivo. (#54)
- **Navegador de compactados mais informativo**: o cabeçalho mostra formato, arquivos, tamanho descompactado e quantas entradas têm senha ou estão bloqueadas; cada arquivo mostra quanto ocupa compactado (%); pastas mostram **Explorar** e, com entradas marcadas, Norte vira **Extrair seleção (N)** e o menu já abre nessa opção. (#68)

### Limitações conhecidas
- Ainda não validado com controles físicos (issue #78: Menu → Teste de controles…).
- Executáveis ainda sem assinatura de código (#84): o Windows SmartScreen pode avisar na primeira execução.

## [0.4.0-alpha.1]
### Novidades
- **Novo layout do teclado virtual**: campo em cima, quatro linhas de caracteres, linha de funções (`⇧`, `ABC`, `@#:`, espaço, `⌫`) e linha inferior com cursor, `…` (acentos, idioma e limpar) e **Concluir** largo. (#41)
- As legendas dos botões seguem automaticamente a família do controle em uso (Xbox, PlayStation, Nintendo ou genérico), detectada pelo tipo informado pelo SDL e pelo fabricante. Apertar um botão em outro controle passa o comando para ele e troca as legendas sem reiniciar. O menu ainda permite fixar um estilo. (#32)
- **Tentar de novo** uma operação que falhou, terminou com avisos ou foi cancelada: Menu → Operações → escolha a operação → Tentar de novo. O pedido original é planejado outra vez do zero. (#18)
- Legendas de controle com **glifos vetoriais originais** (faces, ombros, gatilhos, Menu/Options/+/−, direcional e analógicos) no lugar de letras soltas, nítidos em qualquer escala. Nenhuma imagem ou logotipo de terceiros. (#33)
- O rodapé mostra os **botões do controle em uso** (ex.: `A Abrir` no Xbox, `✕ Abrir` no PlayStation) e troca na hora ao mudar de controle ou de convenção confirmar/voltar; ao usar o teclado, mostra as teclas. O Narrador lê o botão e a ação. (#34)
- Rodapé **por contexto**: num compactado mostra Explorar, Marcar e Extrair… (Norte abre o menu já em "Extrair para"), com itens marcados mostra Operações (N) e Cancelar seleção, no teclado virtual Selecionar/Apagar/Concluir/Cancelar e, nos diálogos, o nome da opção em foco. Ações que não funcionam no momento não aparecem. (#35)
- **Segurar para repetir** no teclado virtual: manter Oeste (apagar), LB/RB (cursor) ou Sul sobre `⌫ ◀ ▶` repete com aceleração; Concluir nunca repete. (#42)
- **Favoritos**: fixe pastas pelo menu de ações (Norte → "Adicionar aos favoritos"). Elas aparecem primeiro na tela inicial e em "Ir para outro local" do seletor de pastas, podem ser reordenadas no início e ficam salvas nas preferências (inclusive no modo portátil). Uma favorita que sumiu aparece como indisponível e só sai da lista se você mandar. (#48)
- **Tentar de novo só as falhas**: no resultado de uma cópia, movimentação, exclusão ou extração, refaz apenas os itens que falharam ou não foram processados; o que já deu certo nunca é refeito. Itens interrompidos por cancelamento agora aparecem como "não processados". (#19)
- **Ícones nativos do Windows** na lista (início, pastas, seletor e compactados): tipos de arquivo, pastas, pastas especiais e unidades. Os ícones carregam em segundo plano, sem travar a rolagem, e acompanham a escala da tela; nenhum emoji na lista. (#24)
- **Busca por nome** na pasta atual: Select/View (ou Ctrl+F) abre o teclado virtual e Concluir inicia a busca, com ou sem subpastas (escolha explícita no menu; sem índice do PC). Os resultados aparecem enquanto são encontrados, o rodapé diz se são parciais, concluídos ou cancelados e quantas pastas sem permissão foram puladas. Leste/B cancela mantendo o que já foi achado; abrir um resultado leva à pasta dele com o foco no item. A busca nunca entra em junções ou links. (#46)
- **Cursor visível** no teclado virtual (barra fixa na cor de destaque, posição lida pelo Narrador) e **LT/RT** (ou Home/End) para ir ao início ou ao fim do texto. (#43)
- **Assistente para joysticks sem perfil** (controles que o Windows/SDL não reconhece como gamepad): segure qualquer botão do joystick por 2 s (ou Menu → Controles sem perfil) e aperte, um por vez, cima, baixo, esquerda, direita, confirmar, voltar e, se quiser, os botões opcionais. Mede o repouso de cada eixo (zona morta e eixos invertidos), permite refazer passos, cancela sozinho após 20 s sem uso e tem um modo de teste antes de salvar. O perfil vale ao reconectar e ao reabrir o app; substituir um perfil existente sempre pede confirmação. Perfis podem ser exportados e importados (JSON versionado, validado, sem conteúdo executável). (#79)
- **Limpeza após queda**: temporários ocultos deixados por uma extração, cópia ou compactação interrompida (travamento, falta de energia) são removidos na próxima abertura, com aviso no rodapé. Só sai o que o ControlFS registrou antes de criar; nunca por padrão de nome, e nunca de outra janela do ControlFS ainda aberta. (#82)
- **Barra de caminho navegável**: LB leva o foco para os segmentos do caminho, esquerda/direita escolhem e Sul vai direto para uma pasta de cima, com o foco na pasta de origem. Em compactados, o arquivo aparece como um segmento próprio depois de `▸`; caminhos longos recolhem o meio em `…`. Também em Menu → Ir para pasta acima…. (#30)
- **Teste de controles** (Menu → Teste de controles…): lista os controles conectados (nome, tipo do SDL, família, VID:PID, gamepad ou joystick sem perfil, qual está ativo) e mostra ao vivo cada botão/eixo e a ação que ele produz. Segurar Confirmar copia um relatório em texto (versões do app, Windows e SDL, controles e resultados; sem dados pessoais) para colar na issue; segurar Voltar ou Esc sai. Testar controles não exige mais o SDK do .NET. (#78)
- Controles de **direcional e dois botões** mapeados pelo assistente sem Ações/Menu: **segurar Confirmar** (0,6 s) abre Ações e **segurar Voltar** abre o Menu; pressões curtas não mudam e o rodapé mostra "(segure)" nesses botões. Joysticks com Ações e Menu mapeados e gamepads não mudam. (#79)
- **Marcar todos** e **Limpar marcação** no menu de ações (Norte), com as contagens; unidades, pastas especiais e entradas bloqueadas nunca são marcadas. (#23)

### Segurança
- Destinos de extração, cópia e movimentação são conferidos **por handle** no Windows: as pastas do destino ficam presas enquanto cada arquivo é colocado, então outro programa não consegue trocá-las por uma junction para gravar fora do destino. Limites restantes em `docs/security-model.md`. (#81)

### Melhorias
- **Layout responsivo** para portáteis (1280×720/800), desktop e TVs 1080p/4K: a faixa de layout segue a resolução efetiva, o DPI e o tamanho do texto do Windows. Em telas pequenas o espaçamento fica enxuto, as legendas do rodapé quebram linha em vez de sumir e menus e diálogos nunca passam da altura da tela (o item focado sempre aparece); numa TV 4K a 100% texto, ícones e foco crescem para ler a ~3 m. (#36)
- **Foco mais claro e sempre num lugar válido:** a lista usa o mesmo anel de destaque dos menus e diálogos; ao excluir ou mover itens o foco vai para o próximo item que sobrou (ou o anterior); ao abrir uma pasta o item focado já aparece rolado na tela; e o teclado físico volta a responder logo depois de fechar um menu ou diálogo. (#31)
- **Lista mais clara:** focado, marcado e recortado têm formas próprias (anel; faixa + caixa marcada + "Marcado"; tesoura + "Recortado"), legíveis sem depender de cor. Tipo, tamanho e data aparecem sempre na mesma ordem, e o item focado mostra o nome inteiro. Nova **densidade da lista** no menu (confortável ou compacta com colunas), salva entre sessões. (#28)

### Limitações conhecidas
- Ainda não validado com controles físicos: rode Menu → Teste de controles… e envie o relatório na issue #78.

## [0.3.0-alpha.1]
### Novidades
- **Renomear** arquivos e pastas pelo teclado virtual: o cursor começa antes da extensão, mudar a extensão pede confirmação e nomes inválidos ou repetidos são recusados sem tocar no disco. (#103)
- **Copiar, recortar e colar** com uma área de transferência própria, e **Copiar para… / Mover para…** com o seletor de pastas do app. Itens recortados ficam esmaecidos até serem colados. (#101, #102)
- **Mover** no mesmo disco é instantâneo; entre discos, o ControlFS copia e só remove cada original depois que a cópia deu certo. (#101)
- **Excluir para a Lixeira do Windows**, com "Excluir permanentemente" separado e sempre confirmado. Em locais sem Lixeira o app avisa que a exclusão será permanente; nunca exclui de vez em silêncio. (#104)
- **Conflitos em todas as operações**: pular, manter ambos, substituir (com confirmação) ou, entre pastas, mesclar; "aplicar aos demais" vale só para a operação atual. (#101)
- Resultado por item de cada operação, na central de operações. (#101)
- Ícone e logo oficiais do ControlFS no app, na barra de tarefas, nos atalhos, no instalador e no README. (#100)

### Licença
- A licença do projeto mudou de MIT para GNU AGPL v3.0 only (`AGPL-3.0-only`). Versões até 0.2.0-alpha.1 continuam sob MIT; a partir desta, AGPL-3.0-only. Detalhes em `docs/LICENSING.md`. (#9)

### Atualizando
- Da 0.2.0-alpha.1: o app instalado mostra o aviso; clique em **Instalar e reiniciar**. As configurações são mantidas.
- Das versões 0.1.0-alpha.1 a alpha.4 (que não abriam): baixe o instalador abaixo uma vez.

## [0.2.0-alpha.1]
### Novidades
- Extrair **7z, RAR (RAR4 e RAR5, inclusive sólidos), TAR, TAR.GZ e GZ**, além de ZIP, com as mesmas proteções (caminhos contidos, links bloqueados, conflitos, limites). Compactados com senha nos arquivos ou na lista (RAR/7z) pedem a senha no teclado virtual. (#5)
- **Compactar** itens marcados (ou o item focado) em **ZIP ou TAR.GZ**: nome pelo teclado virtual, compressão rápida/normal/máxima, progresso na central de operações. Links e junctions não são seguidos e nada é sobrescrito. (#5)
- **Abrir com o Windows:** Confirmar num arquivo que não é compactado abre no programa padrão; no menu de ações há "Abrir com…" e "Mostrar no Explorador de Arquivos". Programas e scripts (.exe, .msi, .bat, .ps1, .lnk…) pedem confirmação, que começa em "Cancelar". (#5)

### Limitações
- RAR não pode ser **criado** (formato proprietário); criar 7z ainda não está disponível. Volumes divididos ainda não são suportados.


## [0.1.0-alpha.4]
### Correções
- O portátil `ControlFS-Portable-x64.exe` da 0.1.0-alpha.3 fechava ao abrir: procurava o arquivo de recursos pelo nome do executável. Agora o arquivo se chama `resources.pri` e é encontrado com qualquer nome. (#4)
- Os testes da CI passam a abrir o portátil com o nome exato publicado (antes ele era renomeado, o que escondeu o erro). (#4)


## [0.1.0-alpha.3]
### Correções
- O app fechava logo ao abrir (instalado e portátil) nas versões 0.1.0-alpha.1 e 0.1.0-alpha.2: faltava o arquivo de recursos do app (`ControlFS.pri`) e o WinUI não encontrava seus estilos. Corrigido.

### Novidades
- Versão portátil em um único arquivo, `ControlFS-Portable-x64.exe`: guarda preferências e logs na pasta `ControlFS_Data` ao lado dele (se a pasta não aceitar gravação, usa `%LOCALAPPDATA%\ControlFS` e avisa).
- Log local de inicialização e falhas em `logs` dentro da pasta de dados, para diagnosticar problemas (sem senhas nem conteúdo de arquivos).
- Cada release agora só é publicada depois que a CI abre de verdade o portátil e a versão instalada num Windows e confirma que a janela aparece.

### Atualizando
- Da 0.1.0-alpha.1 ou 0.1.0-alpha.2: essas versões não abriam, então não conseguem se atualizar sozinhas. Baixe e rode o `ControlFS-Setup-x64.exe` uma vez.


## [0.1.0-alpha.2]
### Novidades
- Instalador para Windows (`ControlFS-Setup-x64.exe`): instala por usuário, sem administrador, com atalho no menu Iniciar e opção de atalho na Área de Trabalho.
- Atualizações automáticas na versão instalada: o app verifica novas versões uma vez por dia, baixa em segundo plano e oferece "Instalar e reiniciar"; se você adiar, instala ao sair. Tudo desligável em Menu → Atualizações.
- Atualizações verificadas: o app só aceita um instalador cujo manifesto esteja assinado pela chave do projeto e cujo SHA-256 e tamanho confiram; versões iguais ou anteriores são recusadas.
- A versão portátil avisa quando existe uma versão nova (a troca é manual).

### Correções
- `SHA256SUMS.txt` agora usa quebra de linha LF, então `sha256sum -c` funciona no Linux e no macOS.

### Atualizando da 0.1.0-alpha.1
- A 0.1.0-alpha.1 não tinha atualizador: baixe e rode o `ControlFS-Setup-x64.exe` uma vez. A partir daí as atualizações são automáticas.


## [0.1.0-alpha.1]
### Novidades
- Primeira versão pública (pré-alfa): gerenciador de arquivos para controle com extrator ZIP integrado.
- Navegação em pastas conhecidas (via API do Windows) e unidades, histórico, ordenação, itens ocultos, marcação e propriedades.
- Teclado virtual próprio (PT-BR/EN, acentos, símbolos, cursor, senha mascarada) operável só com direções, confirmar e voltar.
- Criar pasta com validação das regras de nomes do Windows.
- ZIP: navegar sem extrair, extrair tudo ou seleção (pasta dedicada, aqui ou "para…" com seletor interno), senha ZipCrypto, conflitos, progresso e resultado por item.
- Extração segura: contenção de caminhos, links bloqueados, colisões recusadas, limites, staging e verificação CRC.
- Entrada por SDL3 com mapeamento por posição física e convenção confirmar/voltar alternável; teclado e mouse pelo mesmo modelo.

### Limitações conhecidas
- Ainda não validado em Windows com controles físicos; sem assinatura de código.
- Somente ZIP (sem AES e ZIP64 por enquanto). Copiar, mover, renomear, excluir, busca e dois painéis ainda não existem.
