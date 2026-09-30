using ControlFS.Core.Actions;

namespace ControlFS.Core.Input;

/// <summary>Família visual do controle: decide só as legendas, nunca o comportamento (que segue a posição física).</summary>
public enum ControllerFamily
{
    Generic,
    Xbox,
    PlayStation,
    Nintendo,
}

public static class ControllerFamilies
{
    private const ushort MicrosoftVendor = 0x045E;
    private const ushort SonyVendor = 0x054C;
    private const ushort NintendoVendor = 0x057E;

    /// <summary>
    /// Família a partir do tipo de gamepad do SDL (<c>SDL_GetGamepadStringForType</c>) e, quando o SDL não sabe
    /// (<c>standard</c>/<c>unknown</c>), do vendor ID. Joysticks sem perfil de gamepad são sempre genéricos.
    /// Nunca usa o nome do dispositivo.
    /// </summary>
    public static ControllerFamily Detect(bool isGamepad, string? sdlGamepadType, ushort vendorId)
    {
        if (!isGamepad) return ControllerFamily.Generic;
        return sdlGamepadType switch
        {
            "xbox360" or "xboxone" => ControllerFamily.Xbox,
            "ps3" or "ps4" or "ps5" => ControllerFamily.PlayStation,
            "switchpro" or "joyconleft" or "joyconright" or "joyconpair" or "gamecube" => ControllerFamily.Nintendo,
            _ => vendorId switch
            {
                MicrosoftVendor => ControllerFamily.Xbox,
                SonyVendor => ControllerFamily.PlayStation,
                NintendoVendor => ControllerFamily.Nintendo,
                _ => ControllerFamily.Generic,
            },
        };
    }

    /// <summary>
    /// O dispositivo é um controle Xbox (fabricante Microsoft, ou "Xbox"/"XInput" no nome)? Só para não incomodar: um Xbox que
    /// aparece também como joystick cru (segunda instância do mesmo controle, receptor, driver genérico) nunca deve pedir
    /// "configure seu controle". Não decide legendas (isso segue <see cref="Detect"/>, que nunca usa o nome).
    /// </summary>
    public static bool IsXboxLike(ushort vendorId, string? name) =>
        vendorId == MicrosoftVendor
        || (name is not null && (name.Contains("xbox", StringComparison.OrdinalIgnoreCase) || name.Contains("xinput", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Família usada nas legendas: a escolha manual vence; no automático, a do controle ativo.</summary>
    public static ControllerFamily Resolve(ButtonLabelStyle style, ControllerFamily? active) => style switch
    {
        ButtonLabelStyle.Xbox => ControllerFamily.Xbox,
        ButtonLabelStyle.PlayStation => ControllerFamily.PlayStation,
        ButtonLabelStyle.Nintendo => ControllerFamily.Nintendo,
        ButtonLabelStyle.Generic => ControllerFamily.Generic,
        _ => active ?? ControllerFamily.Generic,
    };
}
