using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;

namespace ControlFS.Core.Input.Mapping;

/// <summary>
/// Identidade de um joystick para aplicar um perfil salvo: GUID do SDL (inclui barramento, vendor, product e versão) e,
/// quando o GUID muda (outra porta, outro driver), vendor/product.
/// </summary>
public sealed record ControllerMatch(string DeviceGuid, ushort VendorId, ushort ProductId)
{
    public static ControllerMatch From(InputDeviceInfo device) => new(device.StableId, device.VendorId, device.ProductId);

    /// <summary>2: mesmo GUID; 1: mesmo vendor/product (ambos conhecidos); 0: não se aplica.</summary>
    public int Score(InputDeviceInfo device)
    {
        if (DeviceGuid.Length > 0 && string.Equals(DeviceGuid, device.StableId, StringComparison.OrdinalIgnoreCase)) return 2;
        if (VendorId != 0 && ProductId != 0 && VendorId == device.VendorId && ProductId == device.ProductId) return 1;
        return 0;
    }
}

/// <summary>
/// Calibração de um eixo: posição neutra medida com tudo solto e zona morta em torno dela. Um eixo que repousa em -1
/// (gatilho analógico) funciona como botão porque tudo é medido a partir do neutro.
/// </summary>
public sealed record AxisCalibration(int Index, double Neutral, double Deadzone)
{
    public const double DefaultDeadzone = 0.3;

    /// <summary>Distância do neutro para pressionar; soltar ocorre abaixo da zona morta (histerese).</summary>
    public double ActivationThreshold => Math.Min(0.95, Deadzone + 0.25);
}

/// <summary>
/// Perfil de um joystick sem mapeamento de gamepad: cada controle do produto (por posição, como nos gamepads) ligado a
/// uma entrada crua. Somente dados; nunca há caminhos, comandos ou scripts.
/// </summary>
public sealed record ControllerProfile(
    string Name,
    ControllerMatch Match,
    IReadOnlyDictionary<PhysicalControl, RawBinding> Bindings,
    IReadOnlyList<AxisCalibration> Axes)
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public AxisCalibration AxisFor(int index)
    {
        foreach (var axis in Axes)
            if (axis.Index == index) return axis;
        return new(index, 0, AxisCalibration.DefaultDeadzone);
    }
}

/// <summary>Um passo do assistente: qual controle do produto está sendo ligado e como é chamado para o usuário.</summary>
public sealed record MappingTarget(PhysicalControl Control, string Label, bool Optional);

public static class MappingTargets
{
    /// <summary>Direções, confirmar e voltar: sem eles o controle não navega.</summary>
    public static IReadOnlySet<PhysicalControl> Required { get; } = new HashSet<PhysicalControl>
    {
        PhysicalControl.DPadUp, PhysicalControl.DPadDown, PhysicalControl.DPadLeft, PhysicalControl.DPadRight,
        PhysicalControl.South, PhysicalControl.East,
    };

    /// <summary>Controles que um perfil pode ligar (os analógicos do gamepad não: as direções já cobrem a navegação).</summary>
    public static IReadOnlySet<PhysicalControl> Allowed { get; } = new HashSet<PhysicalControl>
    {
        PhysicalControl.DPadUp, PhysicalControl.DPadDown, PhysicalControl.DPadLeft, PhysicalControl.DPadRight,
        PhysicalControl.South, PhysicalControl.East, PhysicalControl.West, PhysicalControl.North,
        PhysicalControl.LeftShoulder, PhysicalControl.RightShoulder, PhysicalControl.LeftTrigger, PhysicalControl.RightTrigger,
        PhysicalControl.Start, PhysicalControl.Select,
    };

    /// <summary>
    /// Ordem do assistente. Confirmar/voltar seguem a convenção atual ("Confirmar com"), como nos gamepads; os opcionais
    /// vêm depois, quando voltar já está ligado (e serve para pular).
    /// </summary>
    public static IReadOnlyList<MappingTarget> For(ConfirmBackConvention convention)
    {
        var confirm = convention == ConfirmBackConvention.SouthConfirms ? PhysicalControl.South : PhysicalControl.East;
        var back = convention == ConfirmBackConvention.SouthConfirms ? PhysicalControl.East : PhysicalControl.South;
        return
        [
            new(PhysicalControl.DPadUp, "Cima", false),
            new(PhysicalControl.DPadDown, "Baixo", false),
            new(PhysicalControl.DPadLeft, "Esquerda", false),
            new(PhysicalControl.DPadRight, "Direita", false),
            new(confirm, "Confirmar (abrir)", false),
            new(back, "Voltar", false),
            new(PhysicalControl.North, "Ações", true),
            new(PhysicalControl.Start, "Menu", true),
            new(PhysicalControl.West, "Marcar", true),
            new(PhysicalControl.LeftShoulder, "Região anterior", true),
            new(PhysicalControl.RightShoulder, "Próxima região", true),
            new(PhysicalControl.LeftTrigger, "Página acima", true),
            new(PhysicalControl.RightTrigger, "Página abaixo", true),
            new(PhysicalControl.Select, "Buscar", true),
        ];
    }
}
