using System.Diagnostics;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Core.Input.Mapping;
using ControlFS.Infrastructure.Input.Sdl3.Devices;
using ControlFS.Infrastructure.Input.Sdl3.Normalization;
using SDL;
using static SDL.SDL3;

namespace ControlFS.Infrastructure.Input.Sdl3;

/// <summary>
/// Backend único de controles (SDL3). Não cria janelas SDL. <see cref="Initialize"/> e <see cref="Pump"/>
/// devem ocorrer na MESMA thread (a thread de UI do WinUI), por um temporizador curto — nunca em Task.Run.
/// Joysticks sem perfil de gamepad publicam entradas cruas (botões, hats, eixos) para o assistente de mapeamento e
/// para os perfis salvos; gamepads conhecidos publicam só os controles normalizados.
/// </summary>
public sealed unsafe class Sdl3InputBackend(InputSettings settings) : IInputBackend
{
    private const double TriggerRelease = 0.35;
    private readonly Dictionary<SDL_JoystickID, SdlDevice> _devices = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IInputSink? _sink;
    private bool _initialized;
    private bool _gyroWanted;
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
            // O SDL também envia eventos de joystick para gamepads; só os joysticks crus (sem perfil de gamepad) os usam.
            case SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_JOYSTICK_BUTTON_UP:
                if (IsRaw(e.jbutton.which, out var rawButton)) _sink?.OnRawInput(rawButton.Info.SessionKey, RawInputEvent.Button(e.jbutton.button, e.jbutton.down), now);
                break;
            case SDL_EventType.SDL_EVENT_JOYSTICK_HAT_MOTION:
                if (IsRaw(e.jhat.which, out var rawHat)) _sink?.OnRawInput(rawHat.Info.SessionKey, RawInputEvent.Hat(e.jhat.hat, e.jhat.value), now);
                break;
            case SDL_EventType.SDL_EVENT_JOYSTICK_AXIS_MOTION:
                if (IsRaw(e.jaxis.which, out var rawAxis)) _sink?.OnRawInput(rawAxis.Info.SessionKey, RawInputEvent.Axis(e.jaxis.axis, e.jaxis.value / 32767.0), now);
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_SENSOR_UPDATE:
                if (e.gsensor.sensor == (int)SDL_SensorType.SDL_SENSOR_GYRO && _devices.TryGetValue(e.gsensor.which, out var gyro) && gyro.GyroOn)
                {
                    // Relógio do próprio sensor quando existe (Bluetooth entrega em rajadas); senão, o do evento. Ambos em ns.
                    var ns = e.gsensor.sensor_timestamp != 0 ? e.gsensor.sensor_timestamp : e.gsensor.timestamp;
                    _sink?.OnGyro(gyro.Info.SessionKey, e.gsensor.data[0], e.gsensor.data[1], TimeSpan.FromTicks((long)(ns / 100)));
                }
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
                if (_devices.TryGetValue(e.gaxis.which, out var device)) OnAxis(device, (SDL_GamepadAxis)e.gaxis.axis, e.gaxis.value / 32767.0, now);
                break;
        }
    }

    private bool IsRaw(SDL_JoystickID id, out SdlDevice device) =>
        _devices.TryGetValue(id, out device!) && device.Gamepad == null && device.Joystick != null;

    public RawJoystickState? GetRawState(string deviceKey)
    {
        var device = _devices.Values.FirstOrDefault(d => d.Info.SessionKey == deviceKey);
        if (device is null || device.Gamepad != null || device.Joystick == null) return null;
        var joy = device.Joystick;
        var axes = new double[Math.Clamp(SDL_GetNumJoystickAxes(joy), 0, ControllerProfileSerializer.MaxRawIndex + 1)];
        for (var i = 0; i < axes.Length; i++) axes[i] = Math.Clamp(SDL_GetJoystickAxis(joy, i) / 32767.0, -1, 1);
        var buttons = new bool[Math.Clamp(SDL_GetNumJoystickButtons(joy), 0, ControllerProfileSerializer.MaxRawIndex + 1)];
        for (var i = 0; i < buttons.Length; i++) buttons[i] = SDL_GetJoystickButton(joy, i);
        var hats = new int[Math.Clamp(SDL_GetNumJoystickHats(joy), 0, ControllerProfileSerializer.MaxRawIndex + 1)];
        for (var i = 0; i < hats.Length; i++) hats[i] = SDL_GetJoystickHat(joy, i);
        return new RawJoystickState(axes, buttons, hats);
    }

    /// <summary>
    /// Liga/desliga o giroscópio dos gamepads que têm um (mira experimental, #77). Desligado, o sensor não é ligado (poupa
    /// bateria e banda no Bluetooth). Vale também para os controles conectados depois.
    /// </summary>
    public void SetGyroEnabled(bool enabled)
    {
        _gyroWanted = enabled;
        if (!_initialized) return;
        foreach (var device in _devices.Values) ApplyGyro(device);
    }

    /// <summary>O gamepad tem giroscópio e ele está ligado.</summary>
    public bool HasGyro(string deviceKey) => _devices.Values.Any(d => d.GyroOn && d.Info.SessionKey == deviceKey);

    private void ApplyGyro(SdlDevice device)
    {
        if (device.Gamepad == null || !SDL_GamepadHasSensor(device.Gamepad, SDL_SensorType.SDL_SENSOR_GYRO)) return;
        if (device.GyroOn == _gyroWanted) return;
        device.GyroOn = SDL_SetGamepadSensorEnabled(device.Gamepad, SDL_SensorType.SDL_SENSOR_GYRO, _gyroWanted) && _gyroWanted;
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
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX:
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY:
                // Analógico direito: rolagem contínua (#175). A normalização (zona morta, taxa) fica no AnalogScroller.
                if (axis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX) device.RightX = value; else device.RightY = value;
                _sink?.OnScrollStick(device.Info.SessionKey, device.RightX, device.RightY, now);
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
        var vendor = SDL_GetGamepadVendor(pad);
        var type = SDL_GetGamepadStringForType(SDL_GetGamepadType(pad)) ?? "unknown";
        var info = new InputDeviceInfo(
            SessionKey: $"sdl:{(uint)id}",
            Name: SDL_GetGamepadName(pad) ?? "Controle",
            StableId: StableId(id),
            VendorId: vendor,
            ProductId: SDL_GetGamepadProduct(pad),
            IsGamepad: true,
            IsVirtual: SDL_IsJoystickVirtual(id),
            TypeName: type,
            Path: SDL_GetGamepadPath(pad),
            Family: ControllerFamilies.Detect(isGamepad: true, type, vendor));
        var device = new SdlDevice(info, pad, null, settings);
        _devices[id] = device;
        ApplyGyro(device);
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
