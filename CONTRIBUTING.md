# Contribuindo

1. Instale o .NET SDK indicado em `global.json` (10.0.401 ou patch mais novo da mesma banda).
2. `dotnet build ControlFS.slnx` e `dotnet test ControlFS.slnx` precisam passar.
3. Versões de pacotes ficam em `Directory.Packages.props`; ao mudar, atualize os lock files (`dotnet restore`) e registre
   uma decisão em `docs/decisions/` se for mudança de stack.

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
