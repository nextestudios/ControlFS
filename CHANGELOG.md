# Histórico de alterações

Notas em português do Brasil; a versão em inglês (Estados Unidos) fica em `CHANGELOG.en-US.md`. Antes de publicar uma versão, adicione uma seção `## [VERSÃO]` **nos dois arquivos**. O workflow de release usa a seção da tag e falha se faltar alguma.

## [Unreleased]
### Novidades
- **Novo layout do teclado virtual**: campo em cima, quatro linhas de caracteres, linha de funções (`⇧`, `ABC`, `@#:`, espaço, `⌫`) e linha inferior com cursor, `…` (acentos, idioma e limpar) e **Concluir** largo. (#41)

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
