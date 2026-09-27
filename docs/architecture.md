# Arquitetura

Monólito modular desktop. O núcleo não depende de WinUI, SDL3 nem SharpCompress.

```text
            ┌──────────────────────── ControlFS.App (WinUI 3) ────────────────────────┐
 SDL3 ────► │ InputHost ──► InputRouter ──► AppController.Handle(InputAction) ◄── teclado/mouse │
            │                     (Core)            (Application)            MainWindow/ModalView│
            └──────────────────────────────────────────────┬────────────────────────────────────┘
                                                           │ contratos (Core/Contracts)
        ┌──────────────────────────┬───────────────────────┴───────────┬───────────────────────┐
 Infrastructure.Windows   Infrastructure.Archives            Infrastructure.Input.Sdl3
 (filesystem, pastas       (detecção por conteúdo,           (backend SDL3, normalização
  conhecidas, settings)     SharpCompress, SafeExtractor)     de analógico/gatilho)
```

| Projeto | Responsabilidade | Depende de |
|---|---|---|
| `Core` | Modelos (`Location`, `FileEntry`, `ArchiveEntry`, `OperationResult`…), ações semânticas, mapeamento físico→ação, normalização de analógico, roteador de entrada, teclado virtual, políticas (nomes do Windows, contenção de caminhos, limites), contratos. | nada |
| `Application` | `AppController` (estado de apresentação e regras de interação), `FileListState` (foco por identidade), `PaneState` (histórico, geração), `ArchiveTree`, modais, `OperationQueue`. | Core |
| `Infrastructure.Windows` | `LocalFileSystemProvider`, `KnownFolders` (SHGetKnownFolderPath), `JsonSettingsStore`, `FileOperationService`, `PinnedDirectory` (destino preso por handle). | Core |
| `Infrastructure.Archives` | `FormatDetector`, `SharpCompressZipEngine`, `SafeExtractor`, `DestinationGuard`, `Crc32`, `MarkOfTheWeb`, `ArchiveService`. | Core, Infrastructure.Windows (`PinnedDirectory`), SharpCompress |
| `Infrastructure.Input.Sdl3` | `Sdl3InputBackend` (sem janela SDL). | Core, ppy.SDL3-CS |
| `App` | Janela WinUI, `InputHost`, views em C#. | todos |

## Princípios aplicados

- **Um modelo de entrada.** Controle, teclado e mouse chegam ao `AppController` como `InputAction`. Nenhuma tela testa botões Xbox/PlayStation.
- **Locais distintos.** `PhysicalLocation`, `ArchiveLocation` (caminho lógico, não confiável) e `HomeLocation` são tipos diferentes; um caminho interno de compactado nunca é combinado com caminho de disco.
- **Foco ≠ seleção ≠ rolagem.** `FileListState` mantém foco por `Id`; a rolagem é derivada na view (`ScrollIntoView`).
- **Geração de navegação.** Cada navegação incrementa `PaneState.Generation`; respostas atrasadas de outra geração são descartadas (testado).
- **Modais com escopo exclusivo.** Só o topo da pilha recebe ações; ao mudar a pilha, o roteador trava botões mantidos até serem soltos.
- **Regras na aplicação, não na UI.** Validação de nomes, conflitos, limites e contenção estão em Core/Infrastructure; a view só apresenta.
- **Thread de UI.** `AppController` é criado na thread de UI e captura seu `SynchronizationContext`; trabalho de disco e extração roda fora dela.

## Onde está cada contrato pedido

`IFileSystemProvider`, `IArchiveService`, `IInputBackend`, `ISettingsStore`, `IIconProvider` em `Core/Contracts`
(ícones do Shell: `Infrastructure.Windows/Shell/ShellIconProvider`, numa thread STA própria; cache LRU por tipo e
tamanho em `App/Controls/IconLoader`); `IArchiveEngine` em
`Infrastructure.Archives/Engines` (detalhe de adaptador). `IFileOperationService`, `IControllerProfileStore`,
`IThumbnailService` e uma interface pública para a fila **ainda não existem**: serão criados quando houver implementação
real (Etapas 2–3), não como classes vazias.
