# 0009 — Terminal opcional: externo, não embutido (#76)

- **Data:** 2026-09-27
- **Estado:** aceito

## Contexto

O #76 pede abrir um terminal na pasta atual, "embutido ou como Windows Terminal", como ação explícita, sem nunca
executar nada sozinho e sem concatenar caminhos numa linha de comando.

## Decisão

**Terminal externo**: Norte → **Abrir terminal aqui…** abre o **Windows Terminal** (alias `wt.exe` em
`%LOCALAPPDATA%\Microsoft\WindowsApps`) ou, sem ele, o **Windows PowerShell** de `System32`, depois de um aviso que começa
em "Cancelar".

- A pasta vai **só como diretório de trabalho** do processo. O Windows Terminal recebe os argumentos fixos `-d .`
  (ele trataria um `;` no caminho como separador de comandos); o PowerShell, só `-NoLogo`. Nenhum comando é passado.
  Provado em `TerminalJourneyTests::The_folder_only_goes_as_working_directory_never_inside_the_arguments`.
- Executáveis de locais fixos do sistema, nunca do PATH nem da pasta aberta (uma pasta com um `wt.exe` ou
  `powershell.exe` falso não é usada).
- **Controle:** um terminal externo não entende o controle nem o teclado virtual do ControlFS. O aviso diz isso e oferece
  **Abrir terminal e o teclado virtual do Windows** (`osk.exe`), para digitar com o ponteiro/toque.

## Por que não embutido (agora)

Um terminal embutido exigiria um host ConPTY (pseudoconsole), um emulador VT100/xterm completo (cores, cursor, telas
alternativas, redimensionamento) e a digitação pelo teclado virtual do ControlFS com teclas especiais (Ctrl, Tab, setas,
Esc). É um projeto próprio, com risco de segurança e de manutenção maior que o valor para o público principal (uso ocasional
de usuários avançados). Fica como evolução possível sobre esta mesma ação ("embed a ConPTY host later", nas notas do #76).
