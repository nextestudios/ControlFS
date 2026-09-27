using ControlFS.Core.Contracts;

namespace ControlFS.Core.Input;

/// <summary>Por que um dispositivo parece ser uma cópia virtual criada por um remapeador.</summary>
public enum VirtualSource
{
    None,

    /// <summary>Joystick virtual do próprio SDL (<c>SDL_IsJoystickVirtual</c>).</summary>
    Sdl,

    /// <summary>"Steam Virtual Gamepad" (Valve 28DE:11FF), criado pelo Steam Input.</summary>
    SteamInput,

    /// <summary>
    /// Controle Xbox 360 com fio (045E:028E) ao lado de um controle PlayStation ou Nintendo: é o que DS4Windows,
    /// BetterJoy e afins criam pelo ViGEm. Pode ser um Xbox 360 de verdade, por isso só "provável".
    /// </summary>
    LikelyEmulatedXbox360,
}

/// <summary>Um dispositivo que provavelmente repete a entrada de outro (físico + cópia virtual).</summary>
public sealed record ControllerDuplicate(InputDeviceInfo Virtual, VirtualSource Source, IReadOnlyList<InputDeviceInfo> Others);

/// <summary>
/// Diagnóstico de controles duplicados (#80): remapeadores como Steam Input e DS4Windows expõem o controle físico e
/// uma cópia virtual ao mesmo tempo, e cada pressão chega pelos dois. Usa só vendor/product e a marca de virtual do
/// SDL, nunca o nome. Não decide nada sozinho: a UI mostra o aviso e o usuário escolhe o controle ativo.
/// </summary>
public static class ControllerDuplicates
{
    private const ushort ValveVendor = 0x28DE;
    private const ushort SteamVirtualGamepad = 0x11FF;
    private const ushort MicrosoftVendor = 0x045E;
    private const ushort Xbox360Wired = 0x028E;

    public static VirtualSource SourceOf(InputDeviceInfo device, IReadOnlyCollection<InputDeviceInfo> connected)
    {
        if (device.IsVirtual) return VirtualSource.Sdl;
        if (device.VendorId == ValveVendor && device.ProductId == SteamVirtualGamepad) return VirtualSource.SteamInput;
        if (device.VendorId == MicrosoftVendor && device.ProductId == Xbox360Wired
            && connected.Any(d => d.IsGamepad && d.Family is ControllerFamily.PlayStation or ControllerFamily.Nintendo))
            return VirtualSource.LikelyEmulatedXbox360;
        return VirtualSource.None;
    }

    /// <summary>Cópias virtuais prováveis entre os conectados, cada uma com os outros controles que ela pode estar repetindo.</summary>
    public static IReadOnlyList<ControllerDuplicate> Find(IReadOnlyCollection<InputDeviceInfo> connected)
    {
        var result = new List<ControllerDuplicate>();
        foreach (var device in connected)
        {
            var source = SourceOf(device, connected);
            if (source == VirtualSource.None) continue;
            var others = connected.Where(d => !ReferenceEquals(d, device) && SourceOf(d, connected) == VirtualSource.None).ToList();
            if (others.Count > 0) result.Add(new ControllerDuplicate(device, source, others));
        }
        return result;
    }
}
