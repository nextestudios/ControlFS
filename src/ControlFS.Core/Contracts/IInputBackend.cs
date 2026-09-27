using ControlFS.Core.Actions;
using ControlFS.Core.Input;
using ControlFS.Core.Input.Mapping;

namespace ControlFS.Core.Contracts;

/// <summary>
/// Identidade de um dispositivo. <see cref="SessionKey"/> vale só na sessão; <see cref="StableId"/>
/// (GUID SDL + vendor/product) serve para perfis persistentes e NÃO é tratado como único
/// (dois controles iguais têm o mesmo StableId). <see cref="Family"/> vem de <see cref="ControllerFamilies.Detect"/>.
/// </summary>
public sealed record InputDeviceInfo(
    string SessionKey,
    string Name,
    string StableId,
    ushort VendorId,
    ushort ProductId,
    bool IsGamepad,
    bool IsVirtual,
    string TypeName,
    string? Path,
    ControllerFamily Family = ControllerFamily.Generic);

public interface IInputSink
{
    void OnControl(string deviceKey, PhysicalControl control, bool pressed, TimeSpan timestamp);

    void OnDeviceAdded(InputDeviceInfo device);

    void OnDeviceRemoved(string deviceKey);

    /// <summary>
    /// Posição do analógico direito de um gamepad (x, y em -1..1; y negativo = para cima), para a rolagem contínua (#175).
    /// Enviada a cada mudança; a zona morta e a taxa ficam com o <see cref="AnalogScroller"/>.
    /// </summary>
    void OnScrollStick(string deviceKey, double x, double y, TimeSpan timestamp)
    {
    }

    /// <summary>
    /// Giroscópio de um gamepad (rad/s; convenção SDL), só enquanto a mira experimental está ligada (#77). Filtrado pelo
    /// <see cref="GyroPointer"/>.
    /// </summary>
    void OnGyro(string deviceKey, double pitchRadiansPerSecond, double yawRadiansPerSecond, TimeSpan timestamp)
    {
    }

    /// <summary>Entrada crua de um joystick sem perfil de gamepad (botões, hats e eixos), para o assistente e perfis salvos.</summary>
    void OnRawInput(string deviceKey, RawInputEvent input, TimeSpan timestamp)
    {
    }
}

public interface IInputBackend : IDisposable
{
    /// <summary>Deve ser chamado na thread que também chamará <see cref="Pump"/>.</summary>
    bool Initialize(IInputSink sink, out string? error);

    /// <summary>Processa eventos pendentes sem bloquear.</summary>
    void Pump();

    IReadOnlyList<InputDeviceInfo> Devices { get; }

    /// <summary>Estado instantâneo de um joystick cru (null: não é joystick cru ou não está conectado).</summary>
    RawJoystickState? GetRawState(string deviceKey);

    string BackendDescription { get; }
}
