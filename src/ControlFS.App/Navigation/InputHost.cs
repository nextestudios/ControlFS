using System.Diagnostics;
using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Core.Input.Mapping;
using ControlFS.Infrastructure.Input.Sdl3;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.Core;

namespace ControlFS.App.Navigation;

/// <summary>
/// Liga as fontes físicas (SDL3 e teclado) ao AppController por meio de ações semânticas.
/// O SDL é inicializado e bombeado na thread de UI por um DispatcherQueueTimer curto; em
/// segundo plano a cadência cai e o roteamento para a UI é suspenso (sem capturar comandos globais).
/// Joysticks sem perfil de gamepad: eventos crus vão ao assistente (AppController) ou, com perfil salvo, viram
/// controles físicos pelo <see cref="ControllerProfileTranslator"/> e seguem o mesmo caminho dos gamepads.
/// </summary>
public sealed class InputHost : IInputSink, IRawControllerSource, IControllerDiagnostics, IDisposable
{
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan BackgroundInterval = TimeSpan.FromMilliseconds(120);
    private readonly AppController _app;
    private readonly Sdl3InputBackend _backend = new(InputSettings.Default);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherQueueTimer _timer;
    private readonly Dictionary<string, InputDeviceInfo> _devices = [];
    private readonly Dictionary<string, ControllerProfileTranslator> _translators = [];
    private readonly Dictionary<string, LongPressTranslator> _longPress = [];

    public InputHost(AppController app, DispatcherQueue queue)
    {
        _app = app;
        Router = new InputRouter(new ActionMap(app.Settings.Convention), InputSettings.Default, app.Handle);
        Router.RepeatPolicy = app.IsRepeatableInContext;
        Router.ActiveDeviceChanged += _ =>
        {
            app.SetActiveController(ActiveDevice?.Family); // troca de controle muda as legendas na hora
            PublishLongPress();
            StatusChanged?.Invoke();
        };
        app.ModalContextChanged += () =>
        {
            Router.LatchHeld();
            Router.AllowAutomaticActivation = app.TopModal?.IsSensitive != true;
        };
        app.SettingsChanged += s =>
        {
            Router.UpdateMap(new ActionMap(s.Convention));
            foreach (var device in _devices.Values.Where(d => !d.IsGamepad).ToList()) ApplyProfile(device); // Confirmar/Voltar do substituto
        };
        app.ControllerProfilesChanged += () =>
        {
            foreach (var device in _devices.Values.Where(d => !d.IsGamepad).ToList()) ApplyProfile(device);
        };
        app.AttachRawControllers(this);
        app.AttachControllerDiagnostics(this);

        BackendReady = _backend.Initialize(this, out var error);
        BackendError = error;
        _timer = queue.CreateTimer();
        _timer.Interval = ActiveInterval;
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Pump();
        _timer.Start();
    }

    public InputRouter Router { get; }
    public bool BackendReady { get; }
    public string? BackendError { get; }
    public string BackendDescription => BackendReady ? _backend.BackendDescription : $"{_backend.BackendDescription}: {BackendError}";
    public string? ActiveDeviceKey => Router.ActiveDeviceKey;
    public bool IsActiveDeviceLocked => Router.IsActiveDeviceLocked;
    public IReadOnlyCollection<InputDeviceInfo> Devices => _devices.Values;
    public IReadOnlyList<InputDeviceInfo> RawDevices => [.. _devices.Values.Where(d => !d.IsGamepad)];
    public InputDeviceInfo? ActiveDevice => Router.ActiveDeviceKey is { } key && _devices.TryGetValue(key, out var d) ? d : null;

    public event Action? StatusChanged;

    public void SelectActiveDevice(string? deviceKey)
    {
        if (deviceKey is not null && !_devices.ContainsKey(deviceKey)) return; // desconectou enquanto o menu estava aberto
        Router.SelectActiveDevice(deviceKey);
        StatusChanged?.Invoke();
    }

    public void OnWindowActivated(bool active)
    {
        if (active)
        {
            Router.Resume();
            _timer.Interval = ActiveInterval;
        }
        else
        {
            Router.Suspend();
            _timer.Interval = BackgroundInterval; // continua detectando conexão/desconexão
        }
    }

    private void Pump()
    {
        _backend.Pump();
        if (!Router.IsSuspended)
        {
            _app.TickControllers();
            foreach (var (key, longPress) in _longPress.ToArray())
                longPress.Tick(_clock.Elapsed, (control, pressed) => OnControl(key, control, pressed, _clock.Elapsed));
        }
        Router.Tick(_clock.Elapsed);
    }

    public void OnControl(string deviceKey, PhysicalControl control, bool pressed, TimeSpan timestamp) => Route(deviceKey, control, pressed, null);

    /// <summary><paramref name="raw"/>: a entrada crua que um perfil traduziu neste controle (só para a tela de teste).</summary>
    private void Route(string deviceKey, PhysicalControl control, bool pressed, RawInputEvent? raw)
    {
        _devices.TryGetValue(deviceKey, out var device);
        // Volta às legendas do controle quando ele é usado de novo depois do teclado (antes da ação, para o
        // Render dela já sair com os glifos certos).
        if (pressed && Router.ActiveDeviceKey == deviceKey && device is not null) _app.SetActiveController(device.Family);
        var testing = device is not null && _app.IsTestingControllers;
        if (testing) _app.BeginTestInput(device!, control, pressed, raw); // a ação emitida abaixo fica associada a esta pressão
        Router.OnControl(deviceKey, control, pressed, _clock.Elapsed);
        if (testing) _app.EndTestInput();
    }

    public void OnDeviceAdded(InputDeviceInfo device)
    {
        _devices[device.SessionKey] = device;
        if (!device.IsGamepad) ApplyProfile(device);
        _app.OnControllersChanged();
        StatusChanged?.Invoke();
    }

    public void OnRawInput(string deviceKey, RawInputEvent input, TimeSpan timestamp)
    {
        if (Router.IsSuspended || !_devices.TryGetValue(deviceKey, out var device)) return;
        if (_app.IsTestingControllers)
        {
            // Tela de teste: com perfil, a entrada crua segue traduzida (e aparece junto do controle); sem perfil, só é registrada.
            if (_translators.TryGetValue(deviceKey, out var profiled)) profiled.Apply(input, (control, pressed) => Route(deviceKey, control, pressed, input));
            else _app.RecordTestRaw(device, input);
            return;
        }
        if (_app.OnRawInput(device, input)) return; // assistente em andamento ou joystick ainda sem perfil
        if (_translators.TryGetValue(deviceKey, out var translator))
            translator.Apply(input, (control, pressed) => EmitProfiled(deviceKey, control, pressed, timestamp));
    }

    public RawJoystickState? GetState(string deviceKey) => _backend.GetRawState(deviceKey);

    /// <summary>Perfil sem Ações/Menu: Confirmar/Voltar mantidos viram Norte/Start (<see cref="LongPressTranslator"/>).</summary>
    private void EmitProfiled(string deviceKey, PhysicalControl control, bool pressed, TimeSpan timestamp)
    {
        if (_longPress.TryGetValue(deviceKey, out var longPress))
            longPress.Apply(control, pressed, _clock.Elapsed, (c, p) => OnControl(deviceKey, c, p, timestamp));
        else
            OnControl(deviceKey, control, pressed, timestamp);
    }

    /// <summary>O rodapé mostra "segure" em Ações/Menu quando o controle ativo usa o substituto.</summary>
    private void PublishLongPress() =>
        _app.SetLongPressFallback(Router.ActiveDeviceKey is { } key && _longPress.TryGetValue(key, out var longPress) ? longPress.Fallback : null);

    /// <summary>Aplica (ou troca) o perfil salvo do joystick cru, soltando o que o perfil anterior mantinha pressionado.</summary>
    private void ApplyProfile(InputDeviceInfo device)
    {
        var key = device.SessionKey;
        if (_translators.Remove(key, out var old)) old.ReleaseAll((control, _) => Router.OnControl(key, control, false, _clock.Elapsed));
        _longPress.Remove(key);
        if (_app.ProfileFor(device) is { } profile)
        {
            _translators[key] = new ControllerProfileTranslator(profile);
            if (LongPressFallback.For(profile) is { } fallback) _longPress[key] = new LongPressTranslator(fallback, _app.Settings.Convention);
        }
        PublishLongPress();
    }

    public void OnDeviceRemoved(string deviceKey)
    {
        _devices.Remove(deviceKey);
        _translators.Remove(deviceKey);
        _longPress.Remove(deviceKey);
        _app.OnRawDeviceRemoved(deviceKey);
        Router.OnDeviceRemoved(deviceKey); // operações em andamento NÃO são afetadas
        _app.OnControllersChanged();
        StatusChanged?.Invoke();
    }

    /// <summary>Teclado físico: mesmo modelo semântico. Repetição do sistema só vale para navegação.</summary>
    public void OnKeyDown(KeyRoutedEventArgs e)
    {
        var key = e.Key;
        // O WinUI pode expor controles Xbox como teclas Gamepad*; o SDL já trata controles.
        if (key >= VirtualKey.GamepadA && key <= VirtualKey.GamepadRightThumbstickLeft)
        {
            e.Handled = true;
            return;
        }
        var ctrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        var typing = _app.TopModal is Application.State.KeyboardModal;
        InputAction? action = key switch
        {
            VirtualKey.Up => InputAction.NavigateUp,
            VirtualKey.Down => InputAction.NavigateDown,
            VirtualKey.Left when typing => InputAction.PreviousRegion,
            VirtualKey.Right when typing => InputAction.NextRegion,
            VirtualKey.Left when ctrl => InputAction.PreviousRegion,
            VirtualKey.Right when ctrl => InputAction.NextRegion,
            VirtualKey.Left => InputAction.NavigateLeft,
            VirtualKey.Right => InputAction.NavigateRight,
            VirtualKey.Enter when typing => InputAction.OpenAppMenu, // OK do teclado virtual
            VirtualKey.Enter => InputAction.Confirm,
            VirtualKey.Escape => InputAction.Back,
            VirtualKey.Back when !typing => InputAction.Back,
            VirtualKey.Space when !typing => InputAction.ToggleSelection,
            VirtualKey.F2 or VirtualKey.Application => InputAction.OpenContextMenu,
            VirtualKey.F10 => InputAction.OpenAppMenu,
            VirtualKey.Home when typing => InputAction.PageUp, // início do texto
            VirtualKey.End when typing => InputAction.PageDown, // fim do texto
            VirtualKey.PageUp => InputAction.PageUp,
            VirtualKey.PageDown => InputAction.PageDown,
            VirtualKey.F when ctrl => InputAction.Search,
            VirtualKey.G when ctrl && !typing => InputAction.ChangeView,
            _ => null,
        };
        if (action is not null || (typing && key == VirtualKey.Back)) _app.SetActiveController(null); // teclado em uso: legendas de teclado
        if (typing && key == VirtualKey.Back)
        {
            _app.TypeBackspace();
            e.Handled = true;
            return;
        }
        if (typing && ctrl && key == VirtualKey.A)
        {
            _app.SetActiveController(null);
            _app.TypeSelectAll();
            e.Handled = true;
            return;
        }
        if (action is null) return;
        e.Handled = true;
        if (e.KeyStatus.WasKeyDown && !_app.IsRepeatableInContext(action.Value)) return;
        _app.Handle(action.Value);
    }

    public void OnCharacter(char c)
    {
        if (_app.TopModal is Application.State.KeyboardModal && !char.IsControl(c)) _app.TypeText(c.ToString());
    }

    public void Dispose()
    {
        _timer.Stop();
        _backend.Dispose();
    }
}
