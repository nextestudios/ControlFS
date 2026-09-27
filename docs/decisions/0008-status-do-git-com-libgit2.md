# 0008 — Status do Git com LibGit2Sharp (#75)

- **Data:** 2026-09-27
- **Estado:** aceito

## Contexto

O #75 pede marcas de status do Git (ramo, modificado, não rastreado) ao navegar num repositório: somente leitura,
opcional (desligado por padrão) e, de preferência, sem exigir o Git instalado.

## Opções

1. **Git instalado (`git status --porcelain=v2 -z`)** num processo com tempo limite e sem shell. Exige o Git no PATH e,
   pior, `git status` executa comandos configurados no próprio repositório: `core.fsmonitor`, filtros `clean`/`process`
   do `.gitattributes`. Abrir uma pasta de um repositório baixado da internet poderia rodar código dele. Neutralizar
   tudo por `-c` não é confiável (os nomes dos filtros são livres).
2. **Leitor próprio** do índice e dos objetos: índice v2–v4, árvores em packfiles com deltas e as regras do
   `.gitignore`. Grande demais para o benefício.
3. **LibGit2Sharp (libgit2)**: lê o repositório no processo, não executa nada configurado no repositório (sem fsmonitor,
   hooks nem filtros externos) e não precisa do Git instalado.

## Decisão

Opção 3, isolada em `ControlFS.Infrastructure.Git` atrás de `IGitStatusReader` (Core).

- **Licenças:** LibGit2Sharp é MIT. A libgit2 nativa é GPL v2 **com exceção de linking**, que permite distribuí-la
  ligada a outro programa sem que a GPL se aplique a ele; compatível com a distribuição AGPL-3.0-only do ControlFS.
  Avisos em `THIRD_PARTY_NOTICES.md`.
- **Tamanho:** ~0,5 MB (gerenciado) + ~2 MB (`git2-*.dll` win-x64).
- **Limites:** só a pasta mostrada é consultada (pathspec literal), sem descer em pastas não rastreadas, sem submódulos e
  sem detectar renomeações. Leitura fora da thread de UI; mais de 10 s e o resultado é descartado (a pasta fica sem
  marcas; a libgit2 não tem cancelamento no meio do status). Repositórios que a libgit2 recusa (dono diferente,
  corrompidos) ficam sem marcas.
- **Nada muda no repositório:** nenhuma operação do Git é oferecida. A libgit2 pode atualizar o cache de stat do índice
  como o próprio `git status`; o conteúdo rastreado nunca é alterado.
