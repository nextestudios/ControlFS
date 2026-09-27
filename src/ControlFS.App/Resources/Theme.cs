using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ControlFS.Core.Layout;
using Windows.Foundation;
using Windows.UI;

namespace ControlFS.App.Resources;

/// <summary>
/// Tokens visuais (tema escuro grafite). Perigo nunca é indicado só por cor: textos levam "⚠".
/// Tema claro e cor de destaque configurável: pendentes (ver PROGRESS.md).
/// </summary>
public static class Theme
{
    public static readonly Color BackgroundColor = ColorHelper.FromArgb(255, 0x17, 0x19, 0x1D);
    public static readonly SolidColorBrush Background = new(BackgroundColor);
    public static readonly SolidColorBrush Surface = new(ColorHelper.FromArgb(255, 0x21, 0x24, 0x29));
    public static readonly SolidColorBrush SurfaceRaised = new(ColorHelper.FromArgb(255, 0x2B, 0x2F, 0x35));
    public static readonly SolidColorBrush Border = new(ColorHelper.FromArgb(255, 0x3A, 0x3F, 0x47));
    public static readonly SolidColorBrush Text = new(ColorHelper.FromArgb(255, 0xEC, 0xEE, 0xF1));
    public static readonly SolidColorBrush TextMuted = new(ColorHelper.FromArgb(255, 0xA3, 0xA9, 0xB3));
    public static readonly SolidColorBrush TextDisabled = new(ColorHelper.FromArgb(255, 0x6C, 0x72, 0x7B));
    public static readonly SolidColorBrush Accent = new(ColorHelper.FromArgb(255, 0x5B, 0xC0, 0xEB));
    public static readonly SolidColorBrush AccentSoft = new(ColorHelper.FromArgb(255, 0x1E, 0x3A, 0x48));
    public static readonly SolidColorBrush Selected = new(ColorHelper.FromArgb(255, 0xF2, 0xC1, 0x4E));
    public static readonly SolidColorBrush Danger = new(ColorHelper.FromArgb(255, 0xFF, 0x7A, 0x6E));
    public static readonly SolidColorBrush Scrim = new(ColorHelper.FromArgb(200, 0x08, 0x09, 0x0B));
    public static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    /// <summary>
    /// Faixa de layout atual (portátil, desktop, TV grande), escolhida pela janela a partir do tamanho efetivo, do DPI e
    /// do fator de texto do Windows (ver <see cref="LayoutBreakpoints"/>). Os tokens abaixo derivam dela; as telas são
    /// reconstruídas quando ela muda.
    /// </summary>
    public static LayoutProfile Layout { get; private set; } = LayoutProfile.Default;

    /// <summary>Área do app em pixels efetivos (para limitar a altura de menus e diálogos ao que cabe na tela).</summary>
    public static Size Viewport { get; private set; } = new(1920, 1080);

    /// <summary>
    /// Multiplicador extra das fontes. Só o gerador de capturas (--render-screens) usa, para simular o fator de texto do
    /// Windows; no app normal é 1 porque o WinUI já aplica o fator de texto em cada texto.
    /// </summary>
    public static double SimulatedTextScale { get; private set; } = 1;

    /// <summary>Troca a faixa de layout; devolve true se algo mudou (a janela então refaz as telas).</summary>
    public static bool SetLayout(LayoutProfile profile, Size viewport, double simulatedTextScale = 1)
    {
        var changed = profile != Layout || simulatedTextScale != SimulatedTextScale;
        Layout = profile;
        Viewport = viewport;
        SimulatedTextScale = simulatedTextScale;
        return changed;
    }

    private static double Space(double value) => Layout.Snap(value * Layout.SpaceScale);

    private static double Font(double value) => Math.Round(value * Layout.FontScale * SimulatedTextScale);

    /// <summary>Medida fixa (largura máxima, altura de linha, ícone) na escala da faixa atual.</summary>
    public static double Scaled(double value) => Layout.Snap(value * Math.Max(Layout.SpaceScale, Layout.FontScale * SimulatedTextScale));

    public static double SpaceXs => Space(4);
    public static double SpaceS => Space(8);
    public static double SpaceM => Space(16);
    public static double SpaceL => Space(24);
    public static double SpaceXl => Space(40);

    public static double FontCaption => Font(14);
    public static double FontBody => Font(18);
    public static double FontItem => Font(20);
    public static double FontTitle => Font(26);

    public static readonly CornerRadius Radius = new(6);
    /// <summary>Anel de foco: 3 px a 1080p, mais grosso em telas grandes (visível a distância).</summary>
    public static Thickness FocusRing => new(Layout.Snap(3 * Math.Max(1, Layout.SpaceScale)));

    public static Thickness Hairline => new(Layout.Snap(1));

    /// <summary>
    /// Anel de foco único para lista, menus e diálogos: borda de destaque + fundo suave. A espessura não muda entre
    /// focado e não focado (sem "pulo" de layout); o foco nunca depende só da cor de fundo.
    /// </summary>
    public static void ApplyFocus(Microsoft.UI.Xaml.Controls.Border border, bool focused)
    {
        border.BorderThickness = FocusRing;
        border.BorderBrush = focused ? Accent : Transparent;
        border.Background = focused ? AccentSoft : Transparent;
    }
}
