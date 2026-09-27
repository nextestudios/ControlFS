using ControlFS.Application.Operations;
using ControlFS.Application.Prompts;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;

namespace ControlFS.Application;

public enum Screen
{
    Home,
    Browser,
    FolderPicker,
}

/// <summary>
/// Estado de apresentação e regras de interação, dirigidos exclusivamente por <see cref="InputAction"/>.
/// Teclado, mouse e controle chegam aqui pelo mesmo modelo semântico. Deve ser criado e usado na
/// thread de UI (o <see cref="SynchronizationContext"/> atual é capturado para retornar resultados).
/// </summary>
public sealed partial class AppController
{
    private const int PageSize = 10;
    private readonly IFileSystemProvider _fs;
    private readonly IShellService? _shell;
    private readonly IArchiveService _archives;
    private readonly ISettingsStore? _settingsStore;
    private readonly ITemporaryJournal? _temporaries;
    private readonly IImageDecoder? _imageDecoder;
    private readonly SynchronizationContext _ui;
    private readonly List<Modal> _modals = [];
    private readonly List<Task> _pending = [];
    private readonly List<PaneState> _tabs = [new(PaneMode.Browse)];
    private Action<string>? _pickerCallback;
    private Action? _pickerCancel;

    public AppController(IFileSystemProvider fileSystem, IArchiveService archives, ISettingsStore? settingsStore = null, IUpdateService? updates = null,
        IShellService? shell = null, IFileOperationService? fileOperations = null, IControllerProfileStore? controllerProfiles = null,
        ITemporaryJournal? temporaries = null, IOperationHistoryStore? history = null, IImageDecoder? imageDecoder = null, IRecycleBin? recycleBin = null)
    {
        _historyStore = history;
        _imageDecoder = imageDecoder;
        _recycleBin = recycleBin;
        _profileStore = controllerProfiles;
        _temporaries = temporaries;
        Clock = () => _stopwatch.Elapsed;
        _shell = shell;
        _fileOps = fileOperations;
        _fs = fileSystem;
        _archives = archives;
        _settingsStore = settingsStore;
        _updates = updates;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("AppController precisa de um SynchronizationContext de UI.");
        Operations.Completed += OnOperationCompleted;
        Operations.Changed += RaiseChanged;
        PromptProvider = new ControllerPromptProvider(() => ActiveController is null ? null : PromptFamily, () => Settings.Convention, () => TopModal is KeyboardModal);
    }

    public Screen Screen { get; private set; } = Screen.Home;
    public IReadOnlyList<FileEntry> Places { get; private set; } = [];
    public int PlacesFocus { get; private set; }
    /// <summary>Aba ativa do navegador (cada aba é um <see cref="PaneState"/> independente).</summary>
    public PaneState Browser => _tabs[ActiveTab];
    public PaneState Picker { get; } = new(PaneMode.PickFolder);
    public string PickerTitle { get; private set; } = string.Empty;
    public IReadOnlyList<Modal> Modals => _modals;
    public Modal? TopModal => _modals.Count > 0 ? _modals[^1] : null;
    public OperationQueue Operations { get; } = new();
    public AppSettings Settings { get; private set; } = new();
    public ExtractionLimits Limits { get; set; } = ExtractionLimits.Default;
    public string? StatusMessage { get; private set; }

    /// <summary>Cabeçalho do compactado aberto (formato, arquivos, tamanho, com senha, bloqueadas); null fora de um compactado.</summary>
    public string? ArchiveSummary => ActivePane is { Location: ArchiveLocation, Archive: { } tree } ? tree.Summary : null;

    /// <summary>Família do controle ativo (null: nenhum controle ativo; legendas de teclado).</summary>
    public ControllerFamily? ActiveController { get; private set; }

    /// <summary>Família usada nas legendas: a escolha manual do menu vence; no automático, a do controle ativo.</summary>
    public ControllerFamily PromptFamily => ControllerFamilies.Resolve(Settings.LabelStyle, ActiveController);

    public PaneState ActivePane => Screen == Screen.FolderPicker ? Picker : Browser;

    public event Action? Changed;
    /// <summary>Pilha de modais mudou: a camada de entrada deve travar botões mantidos até serem soltos.</summary>
    public event Action? ModalContextChanged;
    public event Action? ExitRequested;
    public event Action<AppSettings>? SettingsChanged;

    public void Start()
    {
        if (_settingsStore is not null)
        {
            var loaded = _settingsStore.Load();
            Settings = loaded.Settings;
            StatusMessage = loaded.Notice;
        }
        LoadControllerProfiles();
        LoadHistory();
        Places = BuildPlaces();
        PlacesFocus = 0;
        Screen = Screen.Home;
        SettingsChanged?.Invoke(Settings);
        RaiseChanged();
        StartAutomaticUpdateCheck();
        if (_temporaries is not null) Track(CleanUpLeftoversAsync(_temporaries));
    }

    /// <summary>
    /// Na inicialização: remove temporários deixados por operações interrompidas (queda, falta de energia) que o
    /// registro prova serem do ControlFS, e avisa no rodapé o que foi limpo.
    /// </summary>
    private async Task CleanUpLeftoversAsync(ITemporaryJournal journal)
    {
        LeftoverCleanup cleanup;
        try
        {
            cleanup = await Task.Run(journal.CleanUpLeftovers);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (cleanup.Removed.Count == 0) return;
        var where = string.Join(", ", cleanup.Removed.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Take(2));
        StatusMessage = cleanup.Removed.Count == 1
            ? $"Limpeza: 1 temporário deixado por uma operação interrompida foi removido (em {where})."
            : $"Limpeza: {cleanup.Removed.Count} temporários deixados por operações interrompidas foram removidos (em {where}).";
        RaiseChanged();
    }

    /// <summary>Aguarda tarefas assíncronas iniciadas pela UI (usado em testes e no encerramento).</summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] snapshot;
            lock (_pending) snapshot = _pending.Where(t => !t.IsCompleted).ToArray();
            if (snapshot.Length == 0) return;
            await Task.WhenAll(snapshot);
        }
    }

    public void Handle(InputAction action)
    {
        StatusMessage = null;
        MappingWizard?.Wizard.Touch(Clock()); // quem está agindo não perde a configuração por inatividade
        if (TopModal is { } modal) HandleModal(modal, action);
        else if (Screen == Screen.Home) HandleHome(action);
        else HandlePane(ActivePane, action);
        RaiseChanged();
    }

    public IReadOnlyList<Hint> Hints => BuildHints();

    /// <summary>Legendas do rodapé para o dispositivo em uso (glifo do controle ativo ou tecla do teclado).</summary>
    public IReadOnlyList<ControllerPrompt> Prompts => [.. BuildHints().Select(h => LongPressPrompt(h) ?? PromptProvider.For(h.Action, h.Label))];

    /// <summary>
    /// Substituto de dois botões do controle ativo (perfil sem Ações e/ou Menu; null: nenhum). Publicado pela camada de
    /// entrada: o rodapé mostra Ações/Menu no botão de Confirmar/Voltar com "segure".
    /// </summary>
    public Core.Input.Mapping.LongPressFallback? LongPressFallback { get; private set; }

    public void SetLongPressFallback(Core.Input.Mapping.LongPressFallback? fallback)
    {
        if (LongPressFallback == fallback) return;
        LongPressFallback = fallback;
        RaiseChanged();
    }

    private ControllerPrompt? LongPressPrompt(Hint hint)
    {
        if (ActiveController is null || LongPressFallback is not { } fallback) return null;
        InputAction? button = hint.Action switch
        {
            InputAction.OpenContextMenu when fallback.ConfirmOpensActions => InputAction.Confirm,
            InputAction.OpenAppMenu when fallback.BackOpensMenu => InputAction.Back,
            _ => null,
        };
        return button is { } held ? PromptProvider.For(held, $"{hint.Label} (segure)") with { Action = hint.Action } : null;
    }

    public IControllerPromptProvider PromptProvider { get; }

    /// <summary>
    /// Publicado pela camada de entrada: a família do controle em uso (troca a quente incluída) ou null quando o
    /// usuário passou a usar o teclado. As legendas mudam no próximo Render.
    /// </summary>
    public void SetActiveController(ControllerFamily? family)
    {
        if (ActiveController == family) return;
        ActiveController = family;
        RaiseChanged();
    }

    // ---------- Mouse/toque: posicionam o foco e reutilizam as mesmas ações semânticas ----------

    public void PointerActivateListItem(int index)
    {
        if (TopModal is not null) return;
        if (Screen == Screen.Home)
        {
            if (index < 0 || index >= Places.Count) return;
            PlacesFocus = index;
        }
        else
        {
            var list = ActivePane.List;
            if (index < 0 || index >= list.Items.Count) return;
            list.FocusById(list.Items[index].Id);
        }
        Handle(InputAction.Confirm);
    }

    public void PointerChooseModalOption(int index)
    {
        switch (TopModal)
        {
            case MenuModal menu when index >= 0 && index < menu.Items.Count:
                menu.FocusIndex = index;
                break;
            case DialogModal dialog when index >= 0 && index < dialog.Options.Count:
                dialog.FocusIndex = index;
                break;
            case MappingWizardModal { Wizard.Phase: Core.Input.Mapping.MappingPhase.Review } wizard when index >= 0 && index < MappingWizardModal.ReviewOptions.Count:
                wizard.ReviewFocus = index;
                break;
            default:
                return;
        }
        Handle(InputAction.Confirm);
    }

    public void PointerPressKey(int row, int column)
    {
        if (TopModal is not KeyboardModal modal) return;
        modal.Keyboard.FocusKey(row, column);
        Handle(InputAction.Confirm);
    }

    // ---------- Home ----------

    private void HandleHome(InputAction action)
    {
        if (_homeRegion != PaneRegion.List)
        {
            HandleTopBar(action);
            return;
        }
        if (action == InputAction.PreviousRegion)
        {
            EnterHomeTopBar();
            return;
        }
        if (IsGrid && GridNavigation.Move(PlacesFocus, Places.Count, GridColumns, GridRowsPerPage, action) is { } cell)
        {
            PlacesFocus = cell;
            return;
        }
        switch (action)
        {
            case InputAction.NavigateUp: PlacesFocus = Math.Max(0, PlacesFocus - 1); break;
            case InputAction.NavigateDown: PlacesFocus = Math.Min(Places.Count - 1, PlacesFocus + 1); break;
            case InputAction.PageUp: PlacesFocus = Math.Max(0, PlacesFocus - PageSize); break;
            case InputAction.PageDown: PlacesFocus = Math.Min(Places.Count - 1, PlacesFocus + PageSize); break;
            case InputAction.Confirm:
            case InputAction.NavigateRight:
                if (PlacesFocus >= 0 && PlacesFocus < Places.Count) OpenPlace(Places[PlacesFocus]);
                break;
            case InputAction.OpenContextMenu: ShowHomeMenu(); break;
            case InputAction.Back: ShowExitDialog(); break;
            case InputAction.OpenAppMenu: ShowAppMenu(); break;
            case InputAction.ChangeView: ToggleView(); break;
        }
    }

    public void OpenPhysical(string path)
    {
        Browser.Back.Clear();
        Browser.Forward.Clear();
        Browser.Location = null;
        Screen = Screen.Browser;
        Track(NavigateAsync(Browser, new PhysicalLocation(path), pushHistory: false));
    }

    public void GoHome()
    {
        Browser.LoadCts?.Cancel();
        Browser.Generation++;
        Browser.IsLoading = false;
        Browser.Region = PaneRegion.List;
        _homeRegion = PaneRegion.List;
        Places = BuildPlaces();
        PlacesFocus = Math.Clamp(PlacesFocus, 0, Math.Max(0, Places.Count - 1));
        Screen = Screen.Home;
        RaiseChanged(); // chamado também de fora de Handle (gerador de capturas, Meu computador)
    }

    // ---------- Modais ----------

    internal void PushModal(Modal modal)
    {
        _modals.Add(modal);
        ModalContextChanged?.Invoke();
        RaiseChanged();
    }

    internal void CloseModal(Modal modal)
    {
        if (_modals.Remove(modal))
        {
            ModalContextChanged?.Invoke();
            RaiseChanged();
        }
    }

    private void HandleModal(Modal modal, InputAction action)
    {
        switch (modal)
        {
            case MenuModal menu: HandleMenu(menu, action); break;
            case KeyboardModal keyboard: HandleKeyboard(keyboard, action); break;
            case DialogModal dialog: HandleDialog(dialog, action); break;
            case AboutModal about: HandleAbout(about, action); break;
            case MappingWizardModal wizard: HandleMappingWizard(wizard, action); break;
            case ControllerTestModal test: HandleControllerTest(test, action); break;
            case ImagePreviewModal preview: HandleImagePreview(preview, action); break;
            case TextPreviewModal text: HandleTextPreview(text, action); break;
        }
    }

    private void HandleMenu(MenuModal menu, InputAction action)
    {
        var count = menu.Items.Count;
        if (count == 0)
        {
            // Menu sem itens (ex.: lista de locais vazia): só fechar faz sentido; nunca há foco "em nada".
            if (action is InputAction.Back or InputAction.NavigateLeft or InputAction.OpenAppMenu or InputAction.OpenContextMenu) CloseModal(menu);
            return;
        }
        menu.FocusIndex = Math.Clamp(menu.FocusIndex, 0, count - 1);
        switch (action)
        {
            case InputAction.NavigateUp: menu.FocusIndex = (menu.FocusIndex - 1 + count) % count; break;
            case InputAction.NavigateDown: menu.FocusIndex = (menu.FocusIndex + 1) % count; break;
            case InputAction.PageUp: menu.FocusIndex = 0; break;
            case InputAction.PageDown: menu.FocusIndex = count - 1; break;
            case InputAction.Confirm:
            case InputAction.NavigateRight:
                var item = menu.Items[menu.FocusIndex];
                if (!item.IsEnabled)
                {
                    StatusMessage = item.DisabledReason ?? "Ação indisponível.";
                    break;
                }
                CloseModal(menu);
                item.Execute!();
                break;
            case InputAction.Back:
            case InputAction.NavigateLeft:
            case InputAction.OpenAppMenu:
            case InputAction.OpenContextMenu:
                CloseModal(menu);
                break;
        }
    }

    private void HandleKeyboard(KeyboardModal modal, InputAction action)
    {
        if (modal.IsBusy) return;
        var keyboard = modal.Keyboard;
        keyboard.Handle(action);
        if (keyboard.Outcome == Core.Text.KeyboardOutcome.Cancelled)
        {
            CloseModal(modal);
            modal.OnCancel?.Invoke();
        }
        else if (keyboard.Outcome == Core.Text.KeyboardOutcome.Submitted)
        {
            modal.IsBusy = true;
            Track(SubmitKeyboardAsync(modal));
        }
    }

    /// <summary>
    /// Ações que repetem quando mantidas no contexto atual: navegação em qualquer tela e, no teclado virtual,
    /// apagar e mover o cursor. Confirmar/Concluir fora de ⌫ ◀ ▶ nunca repetem. Na tela de teste de controles nada
    /// repete (cada linha é uma pressão real).
    /// </summary>
    public bool IsRepeatableInContext(InputAction action) =>
        TopModal is not ControllerTestModal && action.IsRepeatable() || TopModal is KeyboardModal { IsBusy: false } modal && modal.Keyboard.IsRepeatable(action);

    /// <summary>Entrada de texto do teclado físico no teclado virtual ativo.</summary>
    public void TypeText(string text)
    {
        if (TopModal is KeyboardModal { IsBusy: false } modal)
        {
            modal.Keyboard.InsertText(text);
            RaiseChanged();
        }
    }

    /// <summary>Ctrl+A no teclado físico: seleciona todo o texto do teclado virtual ativo.</summary>
    public void TypeSelectAll()
    {
        if (TopModal is KeyboardModal { IsBusy: false } modal)
        {
            modal.Keyboard.SelectAll();
            RaiseChanged();
        }
    }

    public void TypeBackspace()
    {
        if (TopModal is KeyboardModal { IsBusy: false } modal)
        {
            modal.Keyboard.Backspace();
            RaiseChanged();
        }
    }

    private async Task SubmitKeyboardAsync(KeyboardModal modal)
    {
        try
        {
            await modal.OnSubmit(modal.Keyboard);
        }
        catch (Exception ex)
        {
            modal.Keyboard.Reopen($"Erro inesperado: {ex.GetType().Name}");
        }
        finally
        {
            modal.IsBusy = false;
            RaiseChanged();
        }
    }

    private void HandleDialog(DialogModal dialog, InputAction action)
    {
        var count = dialog.Options.Count;
        if (count == 0)
        {
            if (action == InputAction.Back) CloseModal(dialog);
            return;
        }
        switch (action)
        {
            case InputAction.NavigateUp:
            case InputAction.NavigateLeft:
                dialog.FocusIndex = (dialog.FocusIndex - 1 + count) % count;
                break;
            case InputAction.NavigateDown:
            case InputAction.NavigateRight:
                dialog.FocusIndex = (dialog.FocusIndex + 1) % count;
                break;
            case InputAction.Confirm:
                dialog.Options[dialog.FocusIndex].Execute();
                break;
            case InputAction.Back:
                if (dialog.BackOption is { } back) back.Execute();
                else CloseModal(dialog);
                break;
        }
    }

    internal DialogModal ShowMessage(string title, IReadOnlyList<(string, string)> lines, string? message = null)
    {
        var dialog = new DialogModal(title, lines) { Message = message };
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(close);
        dialog.BackOption = close;
        PushModal(dialog);
        return dialog;
    }

    private void ShowExitDialog()
    {
        var active = Operations.ActiveCount;
        var dialog = new DialogModal("Sair do ControlFS?", active > 0
            ? [("Operações em andamento", $"{active} — serão canceladas; arquivos concluídos permanecem.")]
            : [], sensitive: true);
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Sair", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            foreach (var op in Operations.Items.Where(o => o.IsActive).ToList()) Operations.Cancel(op);
            RequestExit();
        }));
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        PushModal(dialog);
    }

    // ---------- Infra interna ----------

    internal void Track(Task task)
    {
        lock (_pending)
        {
            _pending.RemoveAll(t => t.IsCompleted);
            _pending.Add(task);
        }
    }

    internal void Post(Action action) => _ui.Post(_ => { action(); RaiseChanged(); }, null);

    internal void RaiseChanged() => Changed?.Invoke();

    /// <summary>Aviso não modal no rodapé (ex.: pasta de dados alternativa no modo portátil).</summary>
    public void ShowNotice(string message) => SetStatus(message);

    internal void SetStatus(string message)
    {
        StatusMessage = message;
        RaiseChanged();
    }

    internal void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings);
        try { _settingsStore?.Save(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = "Não foi possível salvar as preferências."; }
        SettingsChanged?.Invoke(Settings);
    }
}
