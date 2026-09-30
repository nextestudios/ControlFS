using System.Diagnostics;
using System.Globalization;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Core.Input.Mapping;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Joysticks sem perfil de gamepad: assistente de mapeamento, perfis salvos (aplicados ao reconectar ou reabrir),
/// importação e exportação. A camada de entrada entrega eventos crus aqui e aplica os perfis publicados.
/// </summary>
public sealed partial class AppController
{
    /// <summary>Segurar qualquer botão de um joystick sem perfil por este tempo abre o assistente pelo próprio joystick.</summary>
    public static readonly TimeSpan HoldToConfigure = TimeSpan.FromSeconds(2);

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly List<ControllerProfile> _controllerProfiles = [];
    private readonly Dictionary<string, (InputDeviceInfo Device, TimeSpan Since)> _holding = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unmappedNoticeShown = new(StringComparer.Ordinal);
    private IControllerProfileStore? _profileStore;
    private IRawControllerSource? _rawControllers;

    /// <summary>Relógio do assistente (substituível nos testes).</summary>
    internal Func<TimeSpan> Clock { get; set; }

    public IReadOnlyList<ControllerProfile> ControllerProfiles => _controllerProfiles;

    /// <summary>Perfis mudaram (salvo, importado, carregado ao abrir): a camada de entrada reaplica os perfis.</summary>
    public event Action? ControllerProfilesChanged;

    public MappingWizardModal? MappingWizard => _modals.OfType<MappingWizardModal>().FirstOrDefault();

    public void AttachRawControllers(IRawControllerSource source) => _rawControllers = source;

    /// <summary>O perfil salvo que vale para o dispositivo: o mesmo GUID vence o mesmo vendor/product.</summary>
    public ControllerProfile? ProfileFor(InputDeviceInfo device)
    {
        ControllerProfile? best = null;
        var bestScore = 0;
        foreach (var profile in _controllerProfiles)
        {
            var score = profile.Match.Score(device);
            if (score > bestScore) (best, bestScore) = (profile, score);
        }
        return best;
    }

    private void LoadControllerProfiles()
    {
        if (_profileStore is null) return;
        var loaded = _profileStore.Load();
        _controllerProfiles.Clear();
        _controllerProfiles.AddRange(loaded.Profiles);
        if (loaded.Problems.Count > 0)
            StatusMessage ??= $"{Plural.Of(loaded.Problems.Count, "perfil de controle inválido ignorado", "perfis de controle inválidos ignorados")}: {loaded.Problems[0]}";
        ControllerProfilesChanged?.Invoke();
    }

    /// <summary>
    /// Evento cru de um joystick sem perfil de gamepad. Retorna true quando a aplicação o consumiu (assistente em
    /// andamento ou joystick ainda sem perfil); false quando a camada de entrada deve aplicar o perfil salvo.
    /// </summary>
    public bool OnRawInput(InputDeviceInfo device, RawInputEvent input)
    {
        var now = Clock();
        if (MappingWizard is { } modal)
        {
            if (!string.Equals(modal.Device.SessionKey, device.SessionKey, StringComparison.Ordinal)) return ProfileFor(device) is null;
            var before = (modal.Wizard.Phase, modal.Wizard.StepIndex, modal.Wizard.Feedback, modal.Wizard.LastTested);
            var actions = new List<InputAction>();
            var map = new ActionMap(Settings.Convention);
            modal.Wizard.OnRaw(input, now, (control, pressed) =>
            {
                if (pressed && map.Resolve(control) is { } action) actions.Add(action);
            });
            if (actions.Count > 0) SetActiveController(ControllerFamily.Generic);
            foreach (var action in actions) Handle(action); // teste: o rascunho comanda a tela antes de salvar
            if (actions.Count == 0 && before != (modal.Wizard.Phase, modal.Wizard.StepIndex, modal.Wizard.Feedback, modal.Wizard.LastTested))
                RaiseChanged();
            return true;
        }
        if (ProfileFor(device) is not null) return false;
        // Controle Xbox que aparece como joystick cru (segunda instância, receptor, driver genérico): nunca pede configuração.
        // O controle de verdade já comanda o app como gamepad; o assistente é só para quem não é reconhecido.
        if (ControllerFamilies.IsXboxLike(device.VendorId, device.Name)) return true;

        // Sem perfil: segurar um botão do próprio joystick abre o assistente (ele ainda não navega).
        if (input.Kind == RawInputKind.Button && input.Value >= 0.5)
        {
            _holding.TryAdd(device.SessionKey, (device, now));
            if (_unmappedNoticeShown.Add(device.SessionKey))
                SetStatus($"Controle sem perfil ({device.Name}): segure qualquer botão dele por {HoldToConfigure.TotalSeconds:0} s para configurar.");
        }
        else if (input.Kind == RawInputKind.Button)
        {
            _holding.Remove(device.SessionKey);
        }
        return true;
    }

    /// <summary>Chamado pelo laço de entrada: prazo do assistente, medição do neutro e "segurar para configurar".</summary>
    public void TickControllers()
    {
        TickPreviews();
        if (TickControllerTest()) return;
        var now = Clock();
        if (MappingWizard is { } modal)
        {
            if (!modal.Wizard.Tick(now)) return;
            if (!modal.Wizard.IsActive) EndMapping(modal, "Configuração cancelada por inatividade; nada foi salvo.");
            else RaiseChanged();
            return;
        }
        foreach (var (key, (device, since)) in _holding)
        {
            if (now - since < HoldToConfigure) continue;
            _holding.Remove(key);
            if (TopModal?.IsSensitive == true) return;
            StartMapping(device);
            return;
        }
    }

    public void OnRawDeviceRemoved(string deviceKey)
    {
        _holding.Remove(deviceKey);
        if (MappingWizard is { } modal && string.Equals(modal.Device.SessionKey, deviceKey, StringComparison.Ordinal))
        {
            modal.Wizard.Cancel(MappingEndReason.Disconnected);
            EndMapping(modal, "Controle desconectado; a configuração foi cancelada e nada foi salvo.");
        }
    }

    internal void StartMapping(InputDeviceInfo device)
    {
        if (MappingWizard is not null) return;
        var wizard = new ControllerMappingWizard(device.Name, ControllerMatch.From(device), MappingTargets.For(Settings.Convention));
        wizard.Start(_rawControllers?.GetState(device.SessionKey) ?? RawJoystickState.Empty, Clock());
        _holding.Clear();
        PushModal(new MappingWizardModal(device, wizard));
    }

    /// <summary>Fecha o assistente (e o que estiver sobre ele) sem tocar em perfis salvos.</summary>
    private void EndMapping(MappingWizardModal modal, string? status)
    {
        if (modal.Wizard.IsActive) modal.Wizard.Cancel();
        var index = _modals.IndexOf(modal);
        if (index >= 0)
        {
            _modals.RemoveRange(index, _modals.Count - index);
            ModalContextChanged?.Invoke();
        }
        if (status is not null) StatusMessage = status;
        RaiseChanged();
    }

    private void HandleMappingWizard(MappingWizardModal modal, InputAction action)
    {
        var wizard = modal.Wizard;
        var now = Clock();
        wizard.Touch(now);
        if (wizard.Phase != MappingPhase.Review)
        {
            switch (action)
            {
                case InputAction.Back: EndMapping(modal, "Configuração cancelada; nada foi salvo."); break;
                case InputAction.Confirm: wizard.Skip(now); break;
                case InputAction.NavigateLeft: wizard.RedoPrevious(now); break;
            }
            return;
        }
        var count = MappingWizardModal.ReviewOptions.Count;
        switch (action)
        {
            case InputAction.NavigateUp:
            case InputAction.NavigateLeft:
                modal.ReviewFocus = (modal.ReviewFocus - 1 + count) % count;
                break;
            case InputAction.NavigateDown:
            case InputAction.NavigateRight:
                modal.ReviewFocus = (modal.ReviewFocus + 1) % count;
                break;
            case InputAction.Confirm:
                switch (modal.ReviewFocus)
                {
                    case 0: SaveMapping(modal); break;
                    case 1: ShowRedoMenu(modal); break;
                    default: EndMapping(modal, "Configuração cancelada; nada foi salvo."); break;
                }
                break;
            case InputAction.Back:
                ConfirmDiscardMapping(modal);
                break;
        }
    }

    private void ShowRedoMenu(MappingWizardModal modal)
    {
        var wizard = modal.Wizard;
        var items = wizard.Targets.Select(t => new MenuItem(t.Label, () => wizard.Redo(t.Control, Clock()),
            Detail: wizard.Bindings.TryGetValue(t.Control, out var b) ? b.Describe() : "sem botão", Icon: ActionIcon.Retry)).ToList();
        PushModal(new MenuModal("Refazer qual passo?", items) { Icon = ActionIcon.Retry });
    }

    private void ConfirmDiscardMapping(MappingWizardModal modal)
    {
        var dialog = new DialogModal("Descartar este mapeamento?", [("Controle", modal.Device.Name)], sensitive: true)
        {
            Message = "Nada foi salvo ainda. O perfil atual (se houver) continua valendo.",
        };
        var keep = new DialogOption("Continuar testando", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.ControllerTest);
        dialog.Options.Add(keep);
        dialog.Options.Add(new DialogOption("Descartar", DialogOptionKind.Danger, () => EndMapping(modal, "Configuração cancelada; nada foi salvo."), icon: ActionIcon.Erase));
        dialog.BackOption = keep;
        PushModal(dialog);
    }

    private void SaveMapping(MappingWizardModal modal)
    {
        var profile = modal.Wizard.BuildProfile();
        ConfirmReplaceThen(profile, modal.Device.Name, () =>
        {
            if (!CommitProfile(profile)) return;
            modal.Wizard.MarkSaved();
            EndMapping(modal, $"Perfil salvo para {profile.Name}. O controle já navega e o perfil vale ao reabrir o ControlFS.");
            ControllerProfilesChanged?.Invoke();
        });
    }

    /// <summary>
    /// Nunca substitui um perfil que funciona sem confirmação: com perfil existente para o mesmo controle, pergunta
    /// antes (foco em Cancelar); sem perfil, segue direto.
    /// </summary>
    private void ConfirmReplaceThen(ControllerProfile profile, string deviceName, Action commit)
    {
        var existing = _controllerProfiles.FirstOrDefault(p => SameDevice(p.Match, profile.Match));
        if (existing is null)
        {
            commit();
            return;
        }
        var dialog = new DialogModal("Substituir o perfil salvo?", [("Controle", deviceName), ("Perfil atual", existing.Name)], sensitive: true)
        {
            Message = "Se cancelar, o perfil atual continua valendo.",
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(cancel);
        dialog.Options.Add(new DialogOption("Substituir", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            commit();
        }, icon: ActionIcon.Replace));
        dialog.BackOption = cancel;
        PushModal(dialog);
    }

    private bool CommitProfile(ControllerProfile profile)
    {
        try
        {
            _profileStore?.Save(profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ControllerProfileException)
        {
            SetStatus($"Não foi possível salvar o perfil: {ErrorText(ex, "Salvar perfil de controle")}");
            return false;
        }
        _controllerProfiles.RemoveAll(p => SameDevice(p.Match, profile.Match));
        _controllerProfiles.Add(profile);
        return true;
    }

    private static bool SameDevice(ControllerMatch a, ControllerMatch b) => a.DeviceGuid.Length > 0 || b.DeviceGuid.Length > 0
        ? string.Equals(a.DeviceGuid, b.DeviceGuid, StringComparison.OrdinalIgnoreCase)
        : a.VendorId == b.VendorId && a.ProductId == b.ProductId;

    // ---------- Menu: controles sem perfil ----------

    private void ShowControllersMenu()
    {
        var devices = _rawControllers?.RawDevices ?? [];
        var items = new List<MenuItem>();
        foreach (var device in devices)
        {
            var profile = ProfileFor(device);
            items.Add(new MenuItem($"Configurar {device.Name}", () => StartMapping(device),
                Detail: profile is null ? "Sem perfil: ainda não navega." : $"Perfil salvo: {profile.Name}", Icon: ActionIcon.ControllerSetup));
        }
        if (devices.Count == 0)
            items.Add(new MenuItem("Nenhum joystick sem perfil conectado", null, "Conecte o controle; gamepads conhecidos não precisam de configuração.", Icon: ActionIcon.Controller));
        var noStore = _profileStore is null ? "Perfis indisponíveis nesta compilação." : null;
        items.Add(new MenuItem("Importar perfil…", BeginImportProfile, noStore, "Arquivo .json exportado pelo ControlFS", Icon: ActionIcon.Import));
        foreach (var profile in _controllerProfiles)
            items.Add(new MenuItem($"Exportar \"{profile.Name}\"…", () => BeginExportProfile(profile), noStore, Icon: ActionIcon.Export));
        PushModal(new MenuModal("Controles sem perfil", items) { Icon = ActionIcon.ControllerSetup });
    }

    private string StartFolder() =>
        Browser.Location is PhysicalLocation here ? here.FullPath : _fs.GetPlaces().FirstOrDefault(p => p.FullPath is not null)?.FullPath ?? Path.GetTempPath();

    private void BeginExportProfile(ControllerProfile profile) => OpenFolderPicker("Exportar perfil de controle", StartFolder(), folder =>
    {
        try
        {
            var path = _profileStore!.Export(profile, folder);
            SetStatus($"Perfil exportado: {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ControllerProfileException)
        {
            SetStatus($"Não foi possível exportar o perfil: {ErrorText(ex, "Exportar perfil de controle")}");
        }
    });

    private void BeginImportProfile() => OpenFolderPicker("Importar perfil de controle", StartFolder(), folder =>
    {
        IReadOnlyList<string> files;
        try { files = _profileStore!.ListImportable(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Não foi possível ler a pasta: {ErrorText(ex, "Importar perfil de controle")}");
            return;
        }
        var items = files.Select(f => new MenuItem(Path.GetFileName(f), () => ImportProfile(f), Icon: ActionIcon.File)).ToList();
        if (items.Count == 0) items.Add(new MenuItem("Nenhum arquivo .json nesta pasta", null, "Escolha a pasta onde o perfil foi exportado.", Icon: ActionIcon.File));
        PushModal(new MenuModal("Escolha o perfil", items) { Icon = ActionIcon.Import });
    });

    internal void ImportProfile(string path)
    {
        ControllerProfile profile;
        try
        {
            profile = _profileStore!.Import(path);
        }
        catch (Exception ex) when (ex is ControllerProfileException or IOException or UnauthorizedAccessException)
        {
            ShowError("Perfil não importado", [("Arquivo", Path.GetFileName(path))], ex, "Importar perfil de controle");
            return;
        }
        var match = profile.Match;
        var dialog = new DialogModal("Importar perfil de controle?",
        [
            ("Nome", profile.Name),
            ("Controle", match.DeviceGuid.Length > 0 ? $"GUID {match.DeviceGuid}" : string.Create(CultureInfo.InvariantCulture, $"VID {match.VendorId:X4} · PID {match.ProductId:X4}")),
            ("Controles ligados", profile.Bindings.Count.ToString(CultureInfo.InvariantCulture)),
        ])
        { Icon = ActionIcon.Import };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(new DialogOption("Importar", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            ConfirmReplaceThen(profile, profile.Name, () =>
            {
                if (!CommitProfile(profile)) return;
                SetStatus($"Perfil importado: {profile.Name}");
                ControllerProfilesChanged?.Invoke();
            });
        }, icon: ActionIcon.Import));
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        PushModal(dialog);
    }
}
