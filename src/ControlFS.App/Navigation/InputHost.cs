using System.Diagnostics;
using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
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
/// </summary>
public sealed class InputHost : IInputSink, IDisposable
{
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan BackgroundInterval = TimeSpan.FromMilliseconds(120);
    private readonly AppController _app;
    private readonly Sdl3InputBackend _backend = new(InputSettings.Default);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherQueueTimer _timer;
    private readonly Dictionary<string, InputDeviceInfo> _devices = [];

    public InputHost(AppController app, DispatcherQueue queue)
    {
        _app = app;
        Router = new InputRouter(new ActionMap(app.Settings.Convention), InputSettings.Default, app.Handle);
        Router.RepeatPolicy = app.IsRepeatableInContext;
        Router.ActiveDeviceChanged += _ =>
        {
            app.SetActiveController(ActiveDevice?.Family); // troca de controle muda as legendas na hora
            StatusChanged?.Invoke();
        };
        app.ModalContextChanged += () =>
        {
            Router.LatchHeld();
            Router.AllowAutomaticActivation = app.TopModal?.IsSensitive != true;
        };
        app.SettingsChanged += s => Router.UpdateMap(new ActionMap(s.Convention));

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
    public string BackendDescription => _backend.BackendDescription;
    public IReadOnlyCollection<InputDeviceInfo> Devices => _devices.Values;
    public InputDeviceInfo? ActiveDevice => Router.ActiveDeviceKey is { } key && _devices.TryGetValue(key, out var d) ? d : null;

    public event Action? StatusChanged;

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
        Router.Tick(_clock.Elapsed);
    }

    public void OnControl(string deviceKey, PhysicalControl control, bool pressed, TimeSpan timestamp)
    {
        // Volta às legendas do controle quando ele é usado de novo depois do teclado (antes da ação, para o
        // Render dela já sair com os glifos certos).
        if (pressed && Router.ActiveDeviceKey == deviceKey && _devices.TryGetValue(deviceKey, out var device)) _app.SetActiveController(device.Family);
        Router.OnControl(deviceKey, control, pressed, _clock.Elapsed);
    }

    public void OnDeviceAdded(InputDeviceInfo device)
    {
        _devices[device.SessionKey] = device;
        StatusChanged?.Invoke();
    }

    public void OnDeviceRemoved(string deviceKey)
    {
        _devices.Remove(deviceKey);
        Router.OnDeviceRemoved(deviceKey); // operações em andamento NÃO são afetadas
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
            _ => null,
        };
        if (action is not null || (typing && key == VirtualKey.Back)) _app.SetActiveController(null); // teclado em uso: legendas de teclado
        if (typing && key == VirtualKey.Back)
        {
            _app.TypeBackspace();
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
