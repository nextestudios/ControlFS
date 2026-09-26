using System.Diagnostics;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Infrastructure.Input.Sdl3.Devices;
using ControlFS.Infrastructure.Input.Sdl3.Normalization;
using SDL;
using static SDL.SDL3;

namespace ControlFS.Infrastructure.Input.Sdl3;

/// <summary>
/// Backend único de controles (SDL3). Não cria janelas SDL. <see cref="Initialize"/> e <see cref="Pump"/>
/// devem ocorrer na MESMA thread (a thread de UI do WinUI), por um temporizador curto — nunca em Task.Run.
/// Joysticks sem perfil de gamepad são registrados, mas não comandam a UI até existir o assistente (Etapa 3).
/// </summary>
public sealed unsafe class Sdl3InputBackend(InputSettings settings) : IInputBackend
{
    private const double TriggerRelease = 0.35;
    private readonly Dictionary<SDL_JoystickID, SdlDevice> _devices = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IInputSink? _sink;
    private bool _initialized;
    private int? _threadId;

    public IReadOnlyList<InputDeviceInfo> Devices => _devices.Values.Select(d => d.Info).ToList();

    public string BackendDescription { get; private set; } = "SDL3 (não inicializado)";

    public bool Initialize(IInputSink sink, out string? error)
    {
        _sink = sink;
        _threadId = Environment.CurrentManagedThreadId;
        // Sem janelas SDL, eventos chegariam também em segundo plano; o roteamento para a UI é
        // suspenso pela própria aplicação ao perder o foco (InputRouter.Suspend). Tornamos explícito.
        SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        // Windows: processa mensagens de joystick em thread própria do SDL (não dependemos do laço do WinUI).
        SDL_SetHint("SDL_JOYSTICK_THREAD", "1");
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD))
        {
            error = SDL_GetError();
            BackendDescription = "SDL3 indisponível";
            return false;
        }
        _initialized = true;
        var v = SDL_GetVersion();
        BackendDescription = $"SDL {v / 1000000}.{v / 1000 % 1000}.{v % 1000}";
        error = null;
        return true;
    }

    public void Pump()
    {
        if (!_initialized) return;
        Debug.Assert(_threadId == Environment.CurrentManagedThreadId, "SDL deve ser bombeado na thread que o inicializou.");
        SDL_Event e;
        var guard = 0;
        while (guard++ < 512 && SDL_PollEvent(&e))
            Handle(ref e);
    }

    private void Handle(ref SDL_Event e)
    {
        var now = _clock.Elapsed;
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                OpenGamepad(e.gdevice.which);
                break;
            case SDL_EventType.SDL_EVENT_JOYSTICK_ADDED:
                if (!SDL_IsGamepad(e.jdevice.which)) OpenRawJoystick(e.jdevice.which);
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
            case SDL_EventType.SDL_EVENT_JOYSTICK_REMOVED:
                Close(e.jdevice.which);
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
                if (_devices.TryGetValue(e.gbutton.which, out var dev) && SdlControlMapping.FromButton((SDL_GamepadButton)e.gbutton.button) is { } control)
                    _sink?.OnControl(dev.Info.SessionKey, control, e.gbutton.down, now);
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
                if (_devices.TryGetValue(e.gaxis.which, out var device)) OnAxis(device, (SDL_GamepadAxis)e.gaxis.axis, e.gaxis.value / 32767.0, now);
                break;
        }
    }

    private void OnAxis(SdlDevice device, SDL_GamepadAxis axis, double value, TimeSpan now)
    {
        switch (axis)
        {
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX:
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY:
                if (axis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX) device.StickX = value; else device.StickY = value;
                var before = device.Stick.Current;
                var after = device.Stick.Update(device.StickX, device.StickY);
                if (before == after) return;
                if (before != StickDirection.None) _sink?.OnControl(device.Info.SessionKey, StickNormalizer.ToControl(before), false, now);
                if (after != StickDirection.None) _sink?.OnControl(device.Info.SessionKey, StickNormalizer.ToControl(after), true, now);
                break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER:
                device.LeftTriggerDown = Trigger(device, device.LeftTriggerDown, value, PhysicalControl.LeftTrigger, now);
                break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER:
                device.RightTriggerDown = Trigger(device, device.RightTriggerDown, value, PhysicalControl.RightTrigger, now);
                break;
        }
    }

    private bool Trigger(SdlDevice device, bool wasDown, double value, PhysicalControl control, TimeSpan now)
    {
        var isDown = wasDown ? value > TriggerRelease : value > settings.TriggerThreshold;
        if (isDown != wasDown) _sink?.OnControl(device.Info.SessionKey, control, isDown, now);
        return isDown;
    }

    private void OpenGamepad(SDL_JoystickID id)
    {
        if (_devices.ContainsKey(id)) return;
        var pad = SDL_OpenGamepad(id);
        if (pad == null) return;
        var info = new InputDeviceInfo(
            SessionKey: $"sdl:{(uint)id}",
            Name: SDL_GetGamepadName(pad) ?? "Controle",
            StableId: StableId(id),
            VendorId: SDL_GetGamepadVendor(pad),
            ProductId: SDL_GetGamepadProduct(pad),
            IsGamepad: true,
            IsVirtual: SDL_IsJoystickVirtual(id),
            TypeName: SDL_GetGamepadStringForType(SDL_GetGamepadType(pad)) ?? "unknown",
            Path: SDL_GetGamepadPath(pad));
        _devices[id] = new SdlDevice(info, pad, null, settings);
        _sink?.OnDeviceAdded(info);
    }

    private void OpenRawJoystick(SDL_JoystickID id)
    {
        if (_devices.ContainsKey(id)) return;
        var joy = SDL_OpenJoystick(id);
        if (joy == null) return;
        var info = new InputDeviceInfo(
            SessionKey: $"sdl:{(uint)id}",
            Name: SDL_GetJoystickName(joy) ?? "Joystick",
            StableId: StableId(id),
            VendorId: SDL_GetJoystickVendor(joy),
            ProductId: SDL_GetJoystickProduct(joy),
            IsGamepad: false,
            IsVirtual: SDL_IsJoystickVirtual(id),
            TypeName: "joystick sem perfil",
            Path: SDL_GetJoystickPath(joy));
        _devices[id] = new SdlDevice(info, null, joy, settings);
        _sink?.OnDeviceAdded(info);
    }

    private void Close(SDL_JoystickID id)
    {
        if (!_devices.Remove(id, out var device)) return;
        if (device.Gamepad != null) SDL_CloseGamepad(device.Gamepad);
        if (device.Joystick != null) SDL_CloseJoystick(device.Joystick);
        _sink?.OnDeviceRemoved(device.Info.SessionKey);
    }

    private static string StableId(SDL_JoystickID id)
    {
        var guid = SDL_GetJoystickGUIDForID(id);
        var bytes = new ReadOnlySpan<byte>(&guid, sizeof(SDL_GUID));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (!_initialized) return;
        foreach (var id in _devices.Keys.ToArray()) Close(id);
        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_GAMEPAD);
        SDL_Quit();
        _initialized = false;
    }
}
