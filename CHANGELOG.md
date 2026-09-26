# Histórico de alterações

Notas em português do Brasil; a versão em inglês (Estados Unidos) fica em `CHANGELOG.en-US.md`. Antes de publicar uma versão, adicione uma seção `## [VERSÃO]` **nos dois arquivos**. O workflow de release usa a seção da tag e falha se faltar alguma.

## [Unreleased]

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
