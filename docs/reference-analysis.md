# Análise de referências

Consultadas **como documentação** (metadados e textos públicos via GitHub API) em 2026-09-26. Nenhum código, XAML, estilo,
recurso gráfico, som ou marca foi copiado. Nenhum instalador ou script das referências foi executado. As observações
funcionais da tabela original da especificação **não foram reauditadas** nos códigos-fonte nesta sessão.

| Ref. | Fonte | Commit consultado | Licença | Observação (documental) | Decisão adotada | Diferença | Limitações |
|---|---|---|---|---|---|---|---|
| R1 | MustardOS/vtree | `a6d0e016` (2026-05-29) | **GPL-3.0** | "gamepad-driven twin-panel file manager for handheld devices" | Jornadas sem mouse, dois painéis e teclado virtual como requisitos. | Implementação própria em C#/WinUI para Windows; modelo semântico de ações. | GPL: nenhuma linha pode ser reaproveitada. Código não lido. |
| R2 | JosefNemec/Playnite | `6fbebc4e` (2026-09-13) | MIT | Launcher com aplicação fullscreen separada. | Foco previsível e modais em escopo exclusivo. | Não é launcher; uma janela única que também funciona em tela cheia (F11). | Código não auditado. |
| R3 | Mike-Aniki/Aniki-ReMake | `82824f9a` (2026-09-22) | MIT | Tema fullscreen para Playnite. | Leitura à distância: fontes 20–26 px, foco com anel de 3 px. | Identidade visual própria (grafite, destaque azul). | Tema, não framework. |
| R4 | Coolaid003/OmniConsole | `ca71dcf9` (2026-04-27) | **GPL-3.0** | Launcher WinUI 3 com seletor de arquivos para gamepad. | Seletor de pasta **interno** (`FolderPicker` do AppController). | Sem integração com shell/Game Bar; sem restrição a XInput (SDL3). | GPL: nada reaproveitado. |
| R5 | Darkvinx88/TvLauncher | `24a53143` (2026-09-16) | MIT | Launcher leanback com mapeamentos. | Rodapé de comandos contextuais; remapeamento planejado (Etapa 3). | Sem funções de smart TV. | Código não auditado. |
| R6/R8 | libsdl-org/SDL (via ppy/SDL3-CS) | SDL 3.5.0 nativo no pacote 2026.722.0 | zlib | Gamepads por posição física; exigências de thread. | Backend único; bombeamento na thread de UI. | — | Ver `decisions/0002`. |
| R7 | mdqinc/SDL_GameControllerDB | `c6d6e7ec` (2026-09-24) | zlib | Banco comunitário de mapeamentos. | Será versionado junto aos perfis (Etapa 3). | Ainda não incluído; SDL usa o banco embutido. | — |
| R9–R11 | adamhathcock/sharpcompress | pacote 1.0.0 = `b6cc95af` | MIT | `FORMATS.md` no commit do pacote. | Motor encapsulado por `IArchiveEngine`. | Políticas de segurança próprias. | Ver `decisions/0004`. |
