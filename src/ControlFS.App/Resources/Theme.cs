using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ControlFS.Core.Layout;
using Windows.Foundation;
using Windows.UI;

namespace ControlFS.App.Resources;

/// <summary>
/// Tokens visuais do redesenho (docs/ui-redesign.md): escuro azul-marinho, destaque ciano. Perigo nunca é indicado só por
/// cor: textos levam "⚠". Tema claro e cor de destaque configurável: pendentes (#37).
/// </summary>
public static class Theme
{
    public static readonly Color BackgroundColor = ColorHelper.FromArgb(255, 0x06, 0x10, 0x1A);
    public static readonly SolidColorBrush Background = new(BackgroundColor);

    /// <summary>Faixas de cabeçalho, barra superior e rodapé.</summary>
    public static readonly SolidColorBrush Surface = new(ColorHelper.FromArgb(255, 0x0A, 0x16, 0x23));

    /// <summary>Cartões, menus, diálogos e teclas.</summary>
    public static readonly SolidColorBrush SurfaceRaised = new(ColorHelper.FromArgb(255, 0x0F, 0x1D, 0x2A));
    public static readonly SolidColorBrush Border = new(ColorHelper.FromArgb(255, 0x17, 0x36, 0x4D));
    public static readonly SolidColorBrush Text = new(ColorHelper.FromArgb(255, 0xF5, 0xF8, 0xFC));

    /// <summary>Texto secundário (detalhes, legendas).</summary>
    public static readonly SolidColorBrush TextMuted = new(ColorHelper.FromArgb(255, 0xA4, 0xB4, 0xC8));
    public static readonly SolidColorBrush TextDisabled = new(ColorHelper.FromArgb(255, 0x60, 0x72, 0x86));

    /// <summary>Cor do foco (borda do anel, cursor, símbolos em destaque).</summary>
    public static readonly SolidColorBrush Accent = new(ColorHelper.FromArgb(255, 0x11, 0xC7, 0xFF));

    /// <summary>Azul principal (barras de uso, local ativo).</summary>
    public static readonly SolidColorBrush Primary = new(ColorHelper.FromArgb(255, 0x07, 0x9C, 0xFF));

    /// <summary>Fundo do item focado (cartão focado).</summary>
    public static readonly SolidColorBrush AccentSoft = new(ColorHelper.FromArgb(255, 0x12, 0x3B, 0x5A));

    /// <summary>Halo discreto em volta do anel de foco.</summary>
    public static readonly SolidColorBrush FocusGlow = new(ColorHelper.FromArgb(0x55, 0x11, 0xC7, 0xFF));
    public static readonly SolidColorBrush Selected = new(ColorHelper.FromArgb(255, 0xF2, 0xC1, 0x4E));
    public static readonly SolidColorBrush Danger = new(ColorHelper.FromArgb(255, 0xFF, 0x7A, 0x6E));
    public static readonly SolidColorBrush Scrim = new(ColorHelper.FromArgb(200, 0x02, 0x07, 0x0C));
    public static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    /// <summary>Durações das animações curtas (foco, troca de exibição, painéis). Nada passa de 200 ms.</summary>
    public static readonly TimeSpan MotionFast = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan MotionFocus = TimeSpan.FromMilliseconds(160);
    public static readonly TimeSpan MotionPanel = TimeSpan.FromMilliseconds(200);

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

    /// <summary>Espaço na escala da faixa de layout (padding, margens, espaçamentos).</summary>
    public static double Space(double value) => Layout.Snap(value * Layout.SpaceScale);

    /// <summary>Tamanho de fonte na escala da faixa de layout.</summary>
    public static double Font(double value) => Math.Round(value * Layout.FontScale * SimulatedTextScale);

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

    public static readonly CornerRadius Radius = new(8);

    /// <summary>Anel de foco: 2 px a 1080p (mais o halo), mais grosso em telas grandes (visível a distância).</summary>
    public static Thickness FocusRing => new(Layout.Snap(2 * Math.Max(1, Layout.SpaceScale)));

    public static Thickness Hairline => new(Layout.Snap(1));

    /// <summary>Espessura do halo do foco (fora do anel; sempre reservada, então o foco não muda o layout).</summary>
    public static Thickness GlowRing => new(Layout.Snap(2 * Math.Max(1, Layout.SpaceScale)));

    /// <summary>
    /// Anel de foco único para lista, barra superior, menus e diálogos: borda ciano + fundo azul e, quando o anel está
    /// dentro de um <see cref="WithGlow"/>, um halo ciano translúcido. A espessura não muda entre focado e não focado (sem
    /// "pulo" de layout); o foco nunca depende só da cor de fundo.
    /// </summary>
    public static void ApplyFocus(Microsoft.UI.Xaml.Controls.Border border, bool focused)
    {
        border.BorderThickness = FocusRing;
        border.BorderBrush = focused ? Accent : Transparent;
        border.Background = focused ? AccentSoft : Transparent;
        if (border.Parent is Microsoft.UI.Xaml.Controls.Border { Tag: GlowTag } glow) glow.BorderBrush = focused ? FocusGlow : Transparent;
    }

    private const string GlowTag = "glow";

    /// <summary>Aumento do cartão focado (discreto: 1%).</summary>
    public const float CardFocusScale = 1.01f;

    /// <summary>
    /// Cartão (início, grade): sem foco, fundo de cartão com borda discreta; focado, a borda vira ciano, o fundo fica azul
    /// mais claro, o halo aparece e o cartão cresce 1% (as duas mudanças animadas em <see cref="MotionFocus"/>). A
    /// espessura da borda é a mesma nos dois estados (sem pulo de layout).
    /// </summary>
    public static void ApplyCardFocus(Microsoft.UI.Xaml.Controls.Border card, bool focused)
    {
        card.BorderThickness = FocusRing;
        card.BorderBrush = focused ? Accent : Border;
        card.Background = focused ? AccentSoft : SurfaceRaised;
        if (card.Parent is Microsoft.UI.Xaml.Controls.Border { Tag: GlowTag } glow)
        {
            glow.BorderBrush = focused ? FocusGlow : Transparent;
            glow.Scale = focused ? new System.Numerics.Vector3(CardFocusScale, CardFocusScale, 1) : System.Numerics.Vector3.One;
        }
    }

    /// <summary>Halo de um cartão, com o aumento do foco animado a partir do centro.</summary>
    public static Microsoft.UI.Xaml.Controls.Border CardWithGlow(Microsoft.UI.Xaml.Controls.Border card)
    {
        var glow = WithGlow(card);
        glow.ScaleTransition = new Vector3Transition { Duration = MotionFocus };
        glow.SizeChanged += (_, e) => glow.CenterPoint = new System.Numerics.Vector3((float)(e.NewSize.Width / 2), (float)(e.NewSize.Height / 2), 0);
        return glow;
    }

    /// <summary>Envolve um anel num halo (ver <see cref="ApplyFocus"/>) e anima a troca de fundo em <see cref="MotionFocus"/>.</summary>
    public static Microsoft.UI.Xaml.Controls.Border WithGlow(Microsoft.UI.Xaml.Controls.Border ring)
    {
        ring.BackgroundTransition = new BrushTransition { Duration = MotionFocus };
        return new Microsoft.UI.Xaml.Controls.Border
        {
            Tag = GlowTag,
            Child = ring,
            BorderThickness = GlowRing,
            BorderBrush = Transparent,
            CornerRadius = new CornerRadius(ring.CornerRadius.TopLeft + GlowRing.Left),
        };
    }

    /// <summary>Cores das faces do Xbox nas legendas (A verde, B vermelho, X azul, Y amarelo) e a cor da letra por cima.</summary>
    public static (Color Body, Color Letter) XboxFace(Core.Actions.ControllerButton button) => button switch
    {
        Core.Actions.ControllerButton.FaceSouth => (ColorHelper.FromArgb(255, 0x2E, 0xB3, 0x4A), Colors.White),
        Core.Actions.ControllerButton.FaceEast => (ColorHelper.FromArgb(255, 0xE5, 0x39, 0x3F), Colors.White),
        Core.Actions.ControllerButton.FaceWest => (ColorHelper.FromArgb(255, 0x2C, 0x7F, 0xE8), Colors.White),
        _ => (ColorHelper.FromArgb(255, 0xF4, 0xC4, 0x2F), ColorHelper.FromArgb(255, 0x1A, 0x1A, 0x1A)),
    };
}
