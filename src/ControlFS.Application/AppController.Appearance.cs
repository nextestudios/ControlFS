using ControlFS.Core.Appearance;

namespace ControlFS.Application;

/// <summary>Tema claro/escuro e cor de destaque (#37): preferências salvas; a janela aplica ao vivo (SettingsChanged).</summary>
public sealed partial class AppController
{
    internal void CycleTheme()
    {
        UpdateSettings(s => s with { Theme = (ThemeMode)(((int)s.Theme + 1) % Enum.GetValues<ThemeMode>().Length) });
        StatusMessage = $"Tema {ThemeName(Settings.Theme)}.";
    }

    internal void CycleAccent()
    {
        UpdateSettings(s => s with { Accent = (AccentColor)(((int)s.Accent + 1) % ThemePalettes.All.Count) });
        StatusMessage = $"Cor de destaque: {AccentName(Settings.Accent)}.";
    }

    internal static string ThemeName(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => "escuro",
        ThemeMode.Light => "claro",
        _ => "automático (Windows)",
    };

    internal static string AccentName(AccentColor accent) => accent switch
    {
        AccentColor.Blue => "azul",
        AccentColor.Green => "verde",
        AccentColor.Amber => "âmbar",
        AccentColor.Magenta => "magenta",
        AccentColor.Orange => "laranja",
        _ => "ciano",
    };
}
