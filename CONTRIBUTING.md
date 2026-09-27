# Contribuindo

> **Licença das contribuições:** o ControlFS é licenciado sob **AGPL-3.0-only**. Ao enviar uma contribuição (código,
> documentação, testes, recursos), você concorda que ela seja licenciada sob os mesmos termos. Veja `docs/LICENSING.md`.
> *Contributions are accepted under AGPL-3.0-only.*

1. Instale o .NET SDK indicado em `global.json` (10.0.401 ou patch mais novo da mesma banda).
2. `dotnet build ControlFS.slnx` e `dotnet test ControlFS.slnx` precisam passar.
3. Versões de pacotes ficam em `Directory.Packages.props`; ao mudar, atualize os lock files (`dotnet restore`) e registre
   uma decisão em `docs/decisions/` se for mudança de stack.

## Fluxo de branches (Gitflow)

Seguimos o [Gitflow](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow), com uma regra
temporária:

> **Antes da 1.0 (fase atual):** não existe `develop`. PRs de `feature/*` e `hotfix/*` vão direto para `main`; as
> versões são marcadas com tag na `main`. O modelo completo abaixo (com `develop` e `release/*`) começa na primeira versão
> estável.


- `main` guarda só versões publicadas (cada commit tem tag `vX.Y.Z`). `develop` integra o que vai para a próxima versão.
- **Nova funcionalidade/correção comum:** `feature/<issue>-<nome>` a partir de `develop` → pull request para `develop`.
- **Preparar versão:** `release/X.Y.Z` a partir de `develop` (só correções, changelog e docs) → pull request para `main` →
  tag no merge em `main` (publica a release) → merge de `main` de volta em `develop`.
- **Correção urgente:** `hotfix/X.Y.Z` a partir de `main` → pull request para `main` → tag → merge de volta em `develop`.
- CI roda só nesses pontos: PR para `develop` (build + testes), PR para `main` (build + testes + abrir o app), tag em
  `main` (release) e CodeQL em `main`/semanal.

## Regras do projeto

- Código, telas, estilos e recursos originais. **Não copie** de vTree/OmniConsole (GPL-3.0), Playnite, Aniki ReMake,
  TvLauncher ou outros aplicativos — estude comportamentos, implemente do zero.
- Telas usam apenas `InputAction`; nada de checar botões de fabricante na UI.
- Regras de segurança (nomes, contenção, conflitos, limites) ficam em Core/Infrastructure, nunca na view.
- Testes destrutivos só em diretórios temporários (`TempDir`) — nunca em dados reais.
- Não marque como suportado o que não tem teste. Atualize `docs/archive-support.md`, `docs/controller-compatibility.md`
  e `PROGRESS.md` com resultados **observados**.
- Senhas nunca em logs, `ToString()`, arquivos ou argumentos de processo.
- Warnings são erros (`TreatWarningsAsErrors`).
