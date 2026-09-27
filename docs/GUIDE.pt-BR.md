# Guia do ControlFS

🇺🇸 [Guide in English](GUIDE.md)

## Controles

Os botões seguem a **posição física** (convenção do SDL3), não as letras impressas.

| Posição | Ação | Teclado virtual |
|---|---|---|
| Direcional / analógico esquerdo | Mover | Mover entre as teclas |
| Sul | Abrir / confirmar | Pressionar tecla |
| Leste | Voltar / fechar | Cancelar sem aplicar |
| Oeste | Marcar item | Apagar |
| Norte | Ações do item | Maiúsculas |
| LB / RB | Barra de caminho (LB) / abas (RB) | Mover cursor |
| LT / RT | Página anterior / próxima | Cursor no início / fim |
| Start | Menu do app | Concluir |
| Select / View | Buscar | Símbolos |

**Voltar** fecha primeiro o menu aberto, depois limpa a seleção, depois volta no histórico e por fim vai à tela inicial. Sair do app sempre pede confirmação, começando em "Cancelar".

Só um controle comanda o app por vez: o primeiro a apertar um botão. Apertar um botão em outro controle (com o ativo solto) passa o comando para ele. Com a janela em segundo plano, a entrada é ignorada. O menu tem "Confirmar com: botão inferior/direito" e o estilo das legendas: **automáticas** (padrão: seguem a família do controle em uso: Xbox, PlayStation, Nintendo ou genérico) ou fixas em genérico, Xbox, PlayStation ou Nintendo.

O rodapé mostra os botões do controle em uso (por exemplo `A Abrir` no Xbox, `✕ Abrir` no PlayStation) e troca assim que você usa outro controle. Ao usar o teclado, mostra as teclas (`Enter Abrir`, `Esc Voltar`) até você apertar um botão do controle de novo.

O teclado virtual tem o campo de texto em cima, quatro linhas de caracteres, uma linha de funções (`⇧` Maiúsculas · `ABC` letras · `@#:` símbolos · espaço · `⌫`) e uma linha inferior (cursor `◀ ▶` · `…` mais · Cancelar · **Concluir**). `…` abre os acentos, **Selecionar tudo** (`Sel. tudo`), **Limpar** e a troca PT-BR/EN. O texto selecionado fica destacado e sublinhado: digitar o substitui, `⌫` o apaga e `◀ ▶` só desfazem a seleção (Ctrl+A no teclado físico seleciona tudo). Renomear já abre com o nome antes da extensão selecionado: digitar `novo` em `example-file.zip` resulta em `novo.zip`. Maiúsculas: um toque deixa a próxima letra maiúscula, o segundo trava (`⇪`), o terceiro desliga. Teclas que o campo não aceita (ex.: `\ / : * ? " < > |` em nomes de arquivo) ficam apagadas. Segurar Oeste, LB/RB ou Sul sobre `⌫ ◀ ▶` repete (e acelera quanto mais tempo segurar); Concluir e as demais teclas nunca repetem. O cursor é a barra na cor de destaque no campo de texto; LT/RT (ou Home/End) levam ao início ou ao fim, e movê-lo nunca muda o texto, a página nem as maiúsculas.

Toda ação essencial está nos menus (Start / Norte). Num joystick mapeado só com direcional, Confirmar e Voltar, **segure Confirmar** (0,6 s) para abrir Ações e **segure Voltar** para abrir o Menu; pressões curtas continuam confirmando e voltando, e o rodapé mostra "(segure)" nesses botões.

### Teste de controles

Menu → **Teste de controles…** lista os controles conectados (nome, tipo, família, VID:PID, se é gamepad ou joystick sem perfil e qual está ativo) e mostra, a cada botão apertado, o controle físico e a ação que ele faz no ControlFS. Nada é executado nessa tela: segure Confirmar 1 s para copiar um relatório (sem dados pessoais) e segure Voltar 1 s para sair; no teclado, Enter e Esc.

### Controle ativo

Por padrão, qualquer controle assume o comando ao apertar um botão nele (exceto dentro de confirmações sensíveis). Menu → **Controle ativo…** lista os controles conectados com família, tipo, VID:PID e se parecem físicos ou virtuais; aperte Sul/A num deles e só ele comanda o ControlFS até você escolher **Automático** de novo ou ele desconectar. O teclado sempre funciona.

Remapeadores como o Steam Input e o DS4Windows expõem o controle físico *e* uma cópia virtual (ex.: "Steam Virtual Gamepad" ou um controle Xbox 360 emulado), então cada botão poderia chegar duas vezes. O ControlFS avisa no rodapé quando vê isso e marca a provável cópia no menu: escolha um deles ali.

### Joysticks sem perfil

Alguns controles USB genéricos, arcades e adaptadores não são reconhecidos como gamepad. O ControlFS os detecta, mas eles só navegam depois de mapeados:

1. Segure qualquer botão do joystick por 2 segundos (ou, pelo teclado ou outro controle, Menu → **Controles sem perfil…** → Configurar).
2. Solte tudo por um segundo enquanto o ControlFS mede cada eixo em repouso.
3. Aperte o que quiser para **Cima, Baixo, Esquerda, Direita, Confirmar e Voltar**, um por vez, soltando entre os passos. Direcionais, alavancas (inclusive eixos invertidos) e botões funcionam; uma entrada já usada é recusada.
4. Depois vêm os botões opcionais (Ações, Menu, Marcar, regiões, páginas, Buscar). Recomenda-se mapear **Ações** e **Menu**; sem eles, segurar Confirmar abre Ações e segurar Voltar abre o Menu. Aperte o Voltar do joystick (ou Enter) para pular um deles.
5. **Teste** o mapeamento novo: o joystick já comanda a tela; escolha **Salvar perfil**, **Refazer um passo…** ou **Cancelar sem salvar**.

Teclado: Esc cancela sem salvar, ← refaz o passo anterior, Enter pula um passo opcional. Sem nenhuma entrada por 20 segundos o assistente se cancela. Se o joystick já tem perfil, salvar pergunta antes de substituir (começando em "Cancelar"); cancelar em qualquer momento mantém o perfil salvo. O perfil volta a valer sempre que esse joystick é conectado, inclusive ao reabrir o ControlFS. Menu → Controles sem perfil também exporta um perfil para uma pasta e importa um (`.json`, até 64 KB, validado; qualquer coisa inesperada é recusada).

## Barra de caminho

O caminho atual aparece em segmentos no topo. **LB** leva o foco da lista para a barra (na pasta acima da atual); **esquerda/direita** escolhem o segmento e **Sul** vai até ele, com o foco na pasta de onde você veio. **RB**, **baixo** ou **Leste** voltam para a lista. Dentro de um compactado, o próprio arquivo é um segmento depois de um `▸`, para separar a parte do disco do conteúdo do compactado. Caminhos longos recolhem o meio em `…`, que abre as pastas escondidas. Teclado: Ctrl+← / Ctrl+→. A mesma lista está em Menu → **Ir para pasta acima…**.

Para pular direto para qualquer lugar, use Menu → **Ir para caminho…** (também no menu Start do seletor de pastas): o teclado virtual abre com a pasta atual selecionada, então digitar a substitui. Os caracteres de caminho (`\ / :`) ficam na página de símbolos (Select/View); com teclado físico dá para digitar ou colar (Ctrl+V) um caminho, com ou sem aspas, e variáveis como `%USERPROFILE%` funcionam. **Concluir** vai até lá; o caminho de um arquivo abre a pasta dele com o foco no arquivo. Caminho inexistente ou inválido mostra o erro e mantém o teclado aberto para corrigir.

## Abas

A faixa de abas fica acima do caminho. Cada aba guarda a própria pasta, histórico, itens marcados e foco. **RB** leva o foco da lista para a faixa; nela, **LB/RB** (ou esquerda/direita) trocam de aba e **Norte** oferece **Nova aba** (a pasta atual numa aba nova) e **Fechar aba**. **Sul**, **baixo** ou **Leste** voltam para a lista. Norte numa pasta também tem **Abrir em nova aba**. Até 8 abas; clicar numa aba troca para ela.

## A lista

O item focado tem um anel de destaque e mostra o nome inteiro (até três linhas); os outros nomes longos terminam em "…". Itens marcados ganham uma faixa à esquerda, uma caixa marcada e "Marcado"; recortados ganham uma tesoura e "Recortado" e ficam esmaecidos até serem colados; entradas de compactados com senha mostram um cadeado. Nada disso depende só de cor.

Norte → **Marcar todos (N)** marca todos os itens da pasta ou do compactado (nunca unidades, pastas especiais ou entradas bloqueadas); **Limpar marcação (N)** desmarca, assim como o Leste.

Menu → **Densidade da lista** alterna entre **confortável** (duas linhas por item, para a TV) e **compacta** (uma linha com colunas de tipo, tamanho e data). A escolha fica salva.

Menu → **Exibição** (ou **Ctrl+G** no teclado) alterna entre **lista** e **grade** de ícones grandes, nas pastas e na tela inicial. Na grade, o direcional e o analógico andam para cima, baixo, esquerda e direita entre os blocos: esquerda/direita continuam na linha anterior/seguinte nas pontas, e descer para uma última linha mais curta vai ao último item. Os gatilhos paginam uma tela de linhas. Como a esquerda não sobe de pasta na grade, use Voltar ou a barra de caminho (LB). A densidade vale também para a grade (compacta = blocos menores), e trocar de exibição mantém o item focado.

**Tamanho da pasta:** Norte numa pasta ou unidade → **Propriedades** → **Calcular tamanho** soma todos os arquivos dentro dela (inclusive os ocultos, como o "Tamanho" do Explorador), mostrando o total parcial enquanto calcula. **Leste** cancela na hora e mantém o valor parcial. Junções e links nunca são seguidos (aparecem contados à parte) e pastas que não puderam ser lidas são listadas em vez de puladas em silêncio.

## Busca

Aperte **Select/View** (ou Ctrl+F) dentro de uma pasta, digite parte do nome no teclado virtual e aperte **Concluir**. Maiúsculas e acentos não importam ("relatorio" acha "Relatório"). Os resultados aparecem enquanto são encontrados; o rodapé diz se a lista é **parcial** (ainda buscando ou cancelada), **concluída** ou parou no limite de 10.000 resultados, e quantas pastas não puderam ser lidas (sem permissão). Norte → **Outras ações da busca** → **Pastas puladas** mostra quais. Nada é indexado: só a pasta em que você está é lida, na hora da busca.

- **Subpastas:** incluídas por padrão. Troque em Menu → "Busca em subpastas" (vale para a próxima busca) ou em Norte → **Outras ações da busca** → "Subpastas" nos resultados (busca de novo).
- **Leste/B** durante a busca para a busca e mantém os resultados parciais; Leste/B de novo volta para a pasta.
- **Sul/A** num resultado abre a pasta dele com o foco no item; Voltar retorna aos resultados.
- **Filtros:** Norte nos resultados abre os filtros. **Sul** liga/desliga um tipo (Pastas, Imagens, Vídeos, Músicas e áudio, Documentos, Compactados, Executáveis; vários tipos se somam) ou avança as faixas de **Tamanho** e **Modificado**; o menu fica aberto para escolher vários. A lista muda na hora, sem buscar de novo, o rodapé mostra "N de M resultados (filtros: …)" e os filtros continuam valendo nas próximas buscas da sessão até **Limpar filtros**.
- A busca nunca entra em junções, links simbólicos ou outros pontos de nova análise (o próprio link pode aparecer como resultado). Pastas do OneDrive com arquivos sob demanda são pesquisadas como pastas comuns: só os nomes são lidos, então nada é baixado (o OneDrive pode buscar a lista de uma pasta que nunca foi aberta).

## Pastas e arquivos recentes

A tela inicial mostra **Recentes** assim que você abre algo: as últimas 10 pastas visitadas e os últimos 10 arquivos ou compactados abertos, do mais novo ao mais antigo. Escolha um item para voltar a ele (um arquivo abre como se você apertasse Sul nele, na pasta dele). Norte em **Recentes** oferece **Limpar recentes** e **Desligar recentes**; Menu → **Recentes: lembrar/não lembrar** religa. As listas ficam só nas preferências locais e nunca são enviadas; desligar também as apaga.

## Favoritos

Aperte **Norte** numa pasta (ou em qualquer lugar dentro dela, para "esta pasta") e escolha **Adicionar aos favoritos**. Os favoritos aparecem primeiro na tela inicial e no seletor de pastas (Start → "Ir para outro local"), a um botão de distância ao copiar, mover ou extrair. Na tela inicial, Norte num favorito oferece **Mover favorito para cima/baixo** e **Remover dos favoritos**. Um favorito cuja pasta sumiu (por exemplo, pendrive desconectado) aparece como indisponível e continua na lista até você removê-lo.

A tela inicial mostra as unidades com o tipo (local, USB, óptica, rede), o rótulo, a letra e o espaço livre. Conectar ou remover um pendrive atualiza a lista em um ou dois segundos, sem reiniciar; um favorito nesse pendrive volta a ficar disponível.

## Lixeira

Início → **Lixeira** lista o que foi excluído para a Lixeira do Windows, com a pasta de origem e a data da exclusão (a data mostrada em cada item é a da exclusão). **Sul/A** ou **Norte** num item oferece **Restaurar** (volta para a pasta de onde saiu, recriada se preciso; se já houver algo com o mesmo nome lá, nada é sobrescrito) e **Excluir permanentemente…**, que sempre pergunta antes com o foco em **Cancelar**. Marque vários itens com **Oeste/X** para restaurar ou excluir juntos. Um item cujo local original registrado é inválido só pode ser excluído de vez.

## Extraindo

Num compactado o rodapé mostra **Sul Explorar** (abre somente leitura), **Oeste Marcar** e **Norte Extrair…**: Norte abre o menu de ações já em **Extrair para "nome"**, então Norte e depois Sul extraem.

Dentro de um compactado, o cabeçalho mostra formato, número de arquivos, tamanho descompactado e quantas entradas têm senha ou estão bloqueadas. Cada arquivo mostra o tamanho e quanto ocupa compactado (ex.: "compactado: 540 KB (45%)"); entradas com senha têm um cadeado e as bloqueadas dizem o motivo (Sul/A mostra o motivo inteiro). Sul/A explora pastas, Oeste marca para extrair e, com entradas marcadas, o rodapé mostra **Norte Extrair seleção (N)**: o menu abre já nessa opção, então Norte e depois Sul extraem só o que foi marcado.

- **Extrair para "nome"**: cria uma pasta nova ao lado do arquivo (nunca reaproveita uma existente: "nome (2)").
- **Extrair aqui**: na pasta do arquivo; conflitos de nome perguntam.
- **Extrair para…**: escolha uma pasta dentro do app (dá para criar uma ali).
- Dentro de um compactado aberto, marque entradas com Oeste e use **Extrair seleção**.
- **Vários compactados de uma vez**: marque-os com Oeste e aperte Norte → **Extrair cada um para a própria pasta (N)** (a primeira opção, então Norte e depois Sul). Cada um vai para uma pasta nova com o nome dele (nunca mistura conteúdos; nomes repetidos ganham "(2)"), entra na fila como operação própria e, ao final, um resumo mostra o resultado de cada um. Itens marcados que não são compactados ficam de fora; os protegidos pedem a senha na vez deles.
- **Testar integridade** (Norte num compactado, ou dentro dele): lê todas as entradas e confere o CRC sem gravar nada, com progresso e cancelamento em Menu → Operações. O resultado mostra quantas entradas conferiram, quais falharam (com o nome) e quantas não têm checksum para conferir (TAR, GZ, ZIP AES AE-2). Não é uma verificação de vírus.

Antes de começar você vê origem, destino, entradas e política de conflitos. Conflitos começam em **Pular (manter existente)**; **Substituir** pergunta de novo. O compactado nunca é apagado e nada extraído é aberto ou executado.

Entradas bloqueadas (nomes inseguros como `../`, links, nomes reservados do Windows) aparecem com ⚠ e o motivo.

## Compactando

Marque itens com Oeste (ou foque um) → Norte → **Compactar…**. Escolha o nome (teclado virtual), ZIP ou TAR.GZ e o nível de compressão, e depois **Compactar**. O arquivo é gravado num temporário e só aparece quando termina; um arquivo existente nunca é sobrescrito (o nome ganha "(2)"). Links e junctions dentro das pastas são ignorados e listados no resultado. RAR não pode ser criado (formato proprietário).

## Central de operações

Menu → **Operações** lista cada cópia, movimentação, exclusão, extração e compactação da sessão. Selecione uma para ver o progresso ou o resultado, ou para cancelá-la enquanto roda. Uma operação que **falhou**, **terminou com avisos** ou foi **cancelada** oferece **Tentar de novo**: o ControlFS planeja o pedido original do zero (itens que não existem mais na origem ficam de fora; o que já chegou ao destino passa pelas perguntas de conflito de sempre). Compactados com senha pedem a senha outra vez.

Quando só alguns itens falharam ou não foram processados (por exemplo, depois de cancelar ou com um arquivo em uso), o diálogo de resultado e os detalhes da operação também oferecem **Tentar de novo só as falhas (N)**: só esses itens rodam de novo, cada um para a pasta aonde deveria chegar; o que já deu certo nunca é copiado, movido ou extraído outra vez, e o novo resultado lista apenas os itens refeitos. Entradas bloqueadas por segurança nunca são refeitas.

**Pausar e continuar:** cópias, movimentações e exclusões mostram **Pausar** nos detalhes enquanto rodam; a operação para no próximo ponto seguro (entre itens e, na cópia de um arquivo, entre blocos), então a atividade de disco para em cerca de um segundo. **Continuar** retoma do item atual; **Cancelar** também funciona durante a pausa e remove a cópia parcial. Uma operação pausada segura a fila: as próximas esperam ela continuar ou ser cancelada. A pausa só vale enquanto o ControlFS está aberto. Extração e compactação ainda não sabem pausar com segurança, então não oferecem a opção. Mover dentro da mesma unidade e excluir uma pasta inteira (para a Lixeira ou permanentemente) são passos únicos e terminam antes de a pausa valer.

**Desfazer e refazer:** Menu → **Desfazer: …** reverte a operação reversível mais recente desta sessão, e o diálogo de resultado de uma cópia ou movimentação também oferece **Desfazer**. Só entram operações com inverso seguro: **renomear** volta ao nome anterior, **mover** devolve os itens para onde estavam, **copiar** remove a cópia (para a Lixeira, quando a unidade tem uma) só se ela continuar idêntica e o original ainda existir, e itens mandados para a **Lixeira** são restaurados. Antes de mexer em qualquer coisa, o ControlFS confere cada item; se algo mudou desde então (o nome ou o local antigo está ocupado, a cópia foi editada, o item saiu da Lixeira), nada é feito e o motivo aparece. Exclusão permanente, substituições, mesclagem de pastas e operações que terminaram com avisos nunca são oferecidas. **Refazer: …** repete a operação desfeita. O desfazer vale só para as operações desta sessão.

**Histórico:** operações concluídas (e renomeações) ficam em `history.json` na pasta de dados (`%LOCALAPPDATA%\ControlFS`, ou `ControlFS_Data` no modo portátil), então Menu → Operações lista também as operações de aberturas anteriores, das mais recentes para as mais antigas, com data, origem, destino e contagem de desfechos por item. Ficam as 200 operações mais recentes e até 100 itens de cada (problemas primeiro). Ele registra o que aconteceu, nunca conteúdo de arquivos nem senhas de compactados, e por si só não significa que a operação pode ser desfeita. **Limpar histórico…** no fim da lista apaga o registro (nenhum arquivo é alterado).

Se o ControlFS for fechado no meio de uma operação (travamento, falta de energia), a próxima abertura remove os temporários ocultos que ele tinha criado (`.controlfs-staging-*`, `.controlfs-copy-*.part`) e avisa no rodapé. Só é removido o que o ControlFS registrou antes de criar; nada é apagado só por causa do nome.

## Visualizar imagens

**Sul** numa imagem JPG, PNG, GIF, BMP ou WebP abre a imagem dentro do ControlFS (Norte → **Abrir com o aplicativo padrão** continua abrindo no Windows). **Esquerda/Direita** ou **LB/RB** vão para a imagem anterior/próxima na ordem da lista; **RT** aumenta o zoom e **LT** diminui; com zoom, o direcional percorre a imagem e **Sul** volta a ajustar à tela; **Leste/B** fecha, com o foco na última imagem vista. A decodificação acontece em segundo plano (a tela nunca trava) e reduz a imagem a no máximo 4096 px no lado maior; de GIFs animados aparece só o primeiro quadro, e fotos de celular aparecem em pé. Antes de decodificar, o ControlFS confere o formato real pelo conteúdo (não pela extensão) e recusa arquivos acima de 100 MB ou de 80 megapixels com uma mensagem clara. Nada é executado. Imagens dentro de compactados não são visualizadas: extraia antes. WebP depende do codec WebP do Windows (incluído no Windows 11 e no Windows 10 recente).

## Visualizar texto

**Sul** num arquivo de texto (.txt, .md, .log, .json, .xml, .csv, .ini, .yaml, código-fonte…) abre o arquivo dentro do ControlFS, somente leitura; para qualquer outro arquivo, Norte → **Visualizar como texto**. Isso vale também para scripts como .ps1 ou .bat, que o Sul pediria para executar: ler nunca executa nada. **Cima/Baixo** rolam uma linha, **LT/RT** uma página, **LB/RB** vão ao início/fim, **Esquerda/Direita** deslocam linhas longas para o lado, **Sul** alterna entre fonte fixa e proporcional e **Leste/B** fecha. A codificação é detectada (UTF-8 com ou sem BOM, UTF-16 ou, se não for nenhuma, a página de código ANSI do Windows) e aparece com o número de linhas. Só os primeiros 2 MB e 10.000 linhas são lidos; um arquivo maior mostra um aviso claro de "prévia parcial". Arquivos binários são recusados com uma mensagem. Arquivos dentro de compactados não são visualizados.

## Abrindo arquivos com o Windows

**Sul** num arquivo que não é compactado, imagem visualizável nem texto abre no programa padrão do Windows. Norte num arquivo oferece também **Abrir com…** e **Mostrar no Explorador de Arquivos**. O outro programa pode não funcionar com o controle: volte com Alt+Tab ou o botão do sistema. Programas e scripts (.exe, .msi, .bat, .ps1, .lnk…) perguntam antes, começando em **Cancelar**. Nada é aberto automaticamente depois de extrair.

## Atualizações

A versão **instalada** se atualiza sozinha:

1. No máximo uma vez por dia, pergunta ao GitHub qual é a última release de `nextestudios/ControlFS` (só estáveis, ou também pré-lançamentos se você estiver num).
2. Baixa o instalador em segundo plano e só o aceita se o **manifesto da release estiver assinado com a chave do projeto** e o **SHA-256 e o tamanho** do arquivo conferirem. Versões iguais ou anteriores são recusadas.
3. Oferece **Instalar e reiniciar**. Se você adiar, instala em silêncio ao sair. Nada acontece enquanto uma cópia ou extração estiver em andamento.

Menu → **Atualizações**: verificar agora, verificação automática sim/não, instalar ao sair sim/não, pré-lançamentos (automático / sim / não).
A versão **portátil** só avisa que existe versão nova; baixe-a na página da release.

## Leitores de tela

Com o Narrador (ou outro leitor de tela com UI Automation) ligado, o app anuncia onde está o foco e o item focado enquanto você anda com o controle ou o teclado: a tela inicial, a pasta, o menu, o diálogo ou o teclado virtual ao entrar, depois só o item a cada movimento (nome, tipo, tamanho e posição, como "3 de 20"). Estados são ditos por extenso: marcado, recortado, bloqueado (com o motivo), com senha, indisponível (com o motivo). Mensagens do rodapé são lidas sem mover o foco. Nada depende só de som, vibração ou cor.

## Privacidade

Tudo fica no seu PC. Preferências e logs ficam em `%LOCALAPPDATA%\ControlFS` (instalado) ou em `ControlFS_Data` ao lado do `ControlFS-Portable-x64.exe` (portátil). Senhas nunca são gravadas nem registradas. As funções centrais nunca usam a rede. O único acesso à rede é a verificação de atualizações, que envia ao GitHub apenas o User-Agent `ControlFS/<versão>` e pode ser desligada.

## Solução de problemas

- **O app não abre:** envie `logs\startup.log` e `logs\crash.log` da pasta de dados acima (não contêm senhas nem conteúdo de arquivos).
- **Controle não detectado:** conecte e aperte um botão; o cabeçalho mostra o controle ativo. Teclado e mouse sempre funcionam.
- **Aviso do SmartScreen:** o build ainda não tem assinatura de código ([política](CODE_SIGNING.md)).
- **"Formato reconhecido, mas não suportado":** por enquanto só ZIP.
- **Falha na atualização / "assinatura inválida":** o app recusou um arquivo que não conseguiu verificar. Tente mais tarde ou baixe o instalador na página da release.

## Compilando

Requer o .NET SDK indicado em `global.json`.

```bash
dotnet build ControlFS.slnx
dotnet test ControlFS.slnx
```

O app roda só no Windows 11 x64. Instalador + portátil (requer Inno Setup 6): `.\build\Publish-ControlFS.ps1 -Version 0.1.0-alpha.3`.
Mais em [build-and-release.md](build-and-release.md).
