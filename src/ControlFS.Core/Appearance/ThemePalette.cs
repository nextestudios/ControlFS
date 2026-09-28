namespace ControlFS.Core.Appearance;

/// <summary>Tema da interface (#37). <see cref="System"/> segue o modo de apps do Windows (claro/escuro), inclusive ao vivo.</summary>
public enum ThemeMode
{
    System,
    Dark,
    Light,
}

/// <summary>Cores de destaque (foco, cursor, símbolos). Só predefinições conferidas em contraste nos dois temas.</summary>
public enum AccentColor
{
    Cyan,
    Blue,
    Green,
    Amber,
    Magenta,
    Orange,
}

/// <summary>
/// Tokens de cor de um tema, em ARGB (0xAARRGGBB). O app só troca as cores dos pincéis compartilhados do <c>Theme</c>, então
/// a troca vale na hora em todas as telas. Os pares de contraste que importam (texto, foco preenchido, anel de foco,
/// perigo) são conferidos por <see cref="ThemeContrast"/> e pelos testes.
/// </summary>
public sealed record ThemePalette(
    bool IsDark,
    AccentColor AccentName,
    uint Background,
    uint Surface,
    uint SurfaceRaised,
    uint Border,
    uint Text,
    uint TextMuted,
    uint TextDisabled,
    uint Accent,
    uint AccentSoft,
    uint Primary,
    uint Selected,
    uint Danger,
    uint Success,
    uint Scrim,
    uint ModalTint,
    uint ModalSolid,
    uint ModalScrimGlass,
    uint ModalScrimSolid,
    uint ModalEdge,
    uint ModalDivider,
    uint ModalInset,
    uint FocusText,
    uint DangerFill,
    uint DisabledFill,
    uint DetailsGlow,
    uint KeyFill,
    uint KeyFunctionFill)
{
    // KeyFill/KeyFunctionFill: teclas de caractere e de função do teclado virtual, visíveis contra o painel do modal.

    /// <summary>Halo translúcido em volta do anel de foco: o destaque com 1/3 de opacidade.</summary>
    public uint FocusGlow => (Accent & 0x00FFFFFF) | 0x55000000;
}

public static class ThemePalettes
{
    /// <summary>Destaque em cada tema: brilhante no escuro (texto escuro por cima), profundo no claro (texto branco por cima).</summary>
    private static readonly Dictionary<AccentColor, (uint Dark, uint DarkSoft, uint Light, uint LightSoft)> Accents = new()
    {
        [AccentColor.Cyan] = (0xFF11C7FF, 0xFF123B5A, 0xFF00708F, 0xFFD6EEF7),
        [AccentColor.Blue] = (0xFF5AA9FF, 0xFF17365E, 0xFF0A5DC2, 0xFFDCE8F8),
        [AccentColor.Green] = (0xFF3DDC84, 0xFF133F2E, 0xFF137333, 0xFFDAF0E1),
        [AccentColor.Amber] = (0xFFFFC83D, 0xFF3F3415, 0xFF8A5300, 0xFFF6EAD2),
        [AccentColor.Magenta] = (0xFFF47AD6, 0xFF47203F, 0xFFA3218A, 0xFFF5DDF0),
        [AccentColor.Orange] = (0xFFFF9A57, 0xFF482B18, 0xFFB04A00, 0xFFF8E3D6),
    };

    public static IReadOnlyList<AccentColor> All { get; } = Enum.GetValues<AccentColor>();

    /// <summary>Paleta de um tema resolvido (claro/escuro) com um destaque.</summary>
    public static ThemePalette Build(bool dark, AccentColor accent)
    {
        var (darkAccent, darkSoft, lightAccent, lightSoft) = Accents.TryGetValue(accent, out var a) ? a : Accents[AccentColor.Cyan];
        return dark
            ? new ThemePalette(true, accent,
                Background: 0xFF06101A, Surface: 0xFF0A1623, SurfaceRaised: 0xFF0F1D2A, Border: 0xFF17364D,
                Text: 0xFFF5F8FC, TextMuted: 0xFFA4B4C8, TextDisabled: 0xFF607286,
                Accent: darkAccent, AccentSoft: darkSoft, Primary: 0xFF079CFF,
                Selected: 0xFFF2C14E, Danger: 0xFFFF7A6E, Success: 0xFF4CD98A, Scrim: 0xC802070C,
                ModalTint: 0xFF0C1B2A, ModalSolid: 0xFF0D1C2B, ModalScrimGlass: 0xB402070C, ModalScrimSolid: 0xE602070C,
                ModalEdge: 0x38FFFFFF, ModalDivider: 0x24FFFFFF, ModalInset: 0x1AFFFFFF,
                FocusText: 0xFF03101A, DangerFill: 0xFFFF8A7F, DisabledFill: 0xFF2A3E52, DetailsGlow: 0xFF0E2A45,
                KeyFill: 0x2EFFFFFF, KeyFunctionFill: 0x1AFFFFFF)
            : new ThemePalette(false, accent,
                Background: 0xFFF3F6FA, Surface: 0xFFE8EEF5, SurfaceRaised: 0xFFFFFFFF, Border: 0xFFC9D6E3,
                Text: 0xFF0B1724, TextMuted: 0xFF4A5A6C, TextDisabled: 0xFF8595A6,
                Accent: lightAccent, AccentSoft: lightSoft, Primary: 0xFF0067C0,
                Selected: 0xFF8A5A00, Danger: 0xFFB42318, Success: 0xFF1A7F37, Scrim: 0x990B1724,
                ModalTint: 0xFFF7FAFD, ModalSolid: 0xFFFFFFFF, ModalScrimGlass: 0x800B1724, ModalScrimSolid: 0xB00B1724,
                ModalEdge: 0x330B1724, ModalDivider: 0x1F0B1724, ModalInset: 0x0F0B1724,
                FocusText: 0xFFFFFFFF, DangerFill: 0xFFC42B1C, DisabledFill: 0xFFDCE3EA, DetailsGlow: 0xFFDCE8F5,
                KeyFill: 0xFFC8D3E0, KeyFunctionFill: 0xFFE6ECF2);
    }

    /// <summary>Tema efetivo: <see cref="ThemeMode.System"/> segue o Windows (<paramref name="systemIsDark"/>).</summary>
    public static bool IsDark(ThemeMode mode, bool systemIsDark) => mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => systemIsDark,
    };
}

/// <summary>Contraste WCAG 2.x entre duas cores ARGB (a translúcida é composta sobre <c>backdrop</c>).</summary>
public static class ThemeContrast
{
    public static double Ratio(uint foreground, uint background, uint backdrop = 0xFF000000)
    {
        var bg = Over(background, backdrop);
        var fg = Over(foreground, bg);
        var (light, dark) = (Math.Max(Luminance(fg), Luminance(bg)), Math.Min(Luminance(fg), Luminance(bg)));
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>Compõe uma cor translúcida sobre uma opaca.</summary>
    public static uint Over(uint color, uint opaque)
    {
        var alpha = (color >> 24) / 255.0;
        if (alpha >= 1) return color;
        byte Channel(int shift) => (byte)Math.Round((((color >> shift) & 0xFF) * alpha) + (((opaque >> shift) & 0xFF) * (1 - alpha)));
        return 0xFF000000 | ((uint)Channel(16) << 16) | ((uint)Channel(8) << 8) | Channel(0);
    }

    private static double Luminance(uint color)
    {
        static double Linear(uint channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Linear((color >> 16) & 0xFF)) + (0.7152 * Linear((color >> 8) & 0xFF)) + (0.0722 * Linear(color & 0xFF));
    }
}
