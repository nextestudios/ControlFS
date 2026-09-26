using ControlFS.Core.Actions;

namespace ControlFS.Core.Contracts;

/// <summary>
/// Identidade de um dispositivo. <see cref="SessionKey"/> vale só na sessão; <see cref="StableId"/>
/// (GUID SDL + vendor/product) serve para perfis persistentes e NÃO é tratado como único
/// (dois controles iguais têm o mesmo StableId).
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
    string? Path);

public interface IInputSink
{
    void OnControl(string deviceKey, PhysicalControl control, bool pressed, TimeSpan timestamp);

    void OnDeviceAdded(InputDeviceInfo device);

    void OnDeviceRemoved(string deviceKey);
}

public interface IInputBackend : IDisposable
{
    /// <summary>Deve ser chamado na thread que também chamará <see cref="Pump"/>.</summary>
    bool Initialize(IInputSink sink, out string? error);

    /// <summary>Processa eventos pendentes sem bloquear.</summary>
    void Pump();

    IReadOnlyList<InputDeviceInfo> Devices { get; }

    string BackendDescription { get; }
}
