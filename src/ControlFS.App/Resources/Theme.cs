using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ControlFS.Core.Appearance;
using ControlFS.Core.Layout;
using Windows.Foundation;
using Windows.UI;

namespace ControlFS.App.Resources;

/// <summary>
/// Tokens visuais do redesenho (docs/ui-redesign.md). Tema escuro (azul-marinho) ou claro e cor de destaque configurável
/// (#37): os pincéis abaixo são compartilhados por todas as telas e <see cref="Apply"/> só troca a cor deles, então a troca
/// vale na hora, sem reconstruir nada. Perigo nunca é indicado só por cor: textos levam "⚠" (nos modais, o símbolo de
/// alerta). Contrastes conferidos em <see cref="ThemeContrast"/> (testes).
/// </summary>
public static class Theme
{
    /// <summary>Paleta em uso (tema resolvido + destaque). Declarada primeiro: os pincéis abaixo nascem dela.</summary>
    public static ThemePalette Palette { get; private set; } = ThemePalettes.Build(dark: true, AccentColor.Cyan);

    /// <summary>Muda a cada troca de paleta (modais mantidos na tela entram nesta chave para serem refeitos).</summary>
    public static int Revision { get; private set; }

    public static bool IsDark => Palette.IsDark;

    public static Color BackgroundColor => ToColor(Palette.Background);
    public static readonly SolidColorBrush Background = new(ToColor(Palette.Background));

    /// <summary>Faixas de cabeçalho, barra superior e rodapé.</summary>
    public static readonly SolidColorBrush Surface = new(ToColor(Palette.Surface));

    /// <summary>Cartões, menus, diálogos e teclas.</summary>
    public static readonly SolidColorBrush SurfaceRaised = new(ToColor(Palette.SurfaceRaised));
    public static readonly SolidColorBrush Border = new(ToColor(Palette.Border));
    public static readonly SolidColorBrush Text = new(ToColor(Palette.Text));

    /// <summary>Texto secundário (detalhes, legendas).</summary>
    public static readonly SolidColorBrush TextMuted = new(ToColor(Palette.TextMuted));
    public static readonly SolidColorBrush TextDisabled = new(ToColor(Palette.TextDisabled));

    /// <summary>Cor do foco (borda do anel, cursor, símbolos em destaque): a cor de destaque escolhida.</summary>
    public static readonly SolidColorBrush Accent = new(ToColor(Palette.Accent));

    /// <summary>Azul principal (barras de uso, local ativo).</summary>
    public static readonly SolidColorBrush Primary = new(ToColor(Palette.Primary));

    /// <summary>Fundo do item focado (cartão focado): o destaque misturado ao fundo.</summary>
    public static readonly SolidColorBrush AccentSoft = new(ToColor(Palette.AccentSoft));

    /// <summary>Halo discreto em volta do anel de foco.</summary>
    public static readonly SolidColorBrush FocusGlow = new(ToColor(Palette.FocusGlow));
    public static readonly SolidColorBrush Selected = new(ToColor(Palette.Selected));
    public static readonly SolidColorBrush Danger = new(ToColor(Palette.Danger));
    public static readonly SolidColorBrush Scrim = new(ToColor(Palette.Scrim));
    public static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    /// <summary>Brilho no alto do painel de detalhes (degradê).</summary>
    public static Color DetailsGlowColor => ToColor(Palette.DetailsGlow);

    public static Color ToColor(uint argb) => ColorHelper.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>
    /// Troca tema e destaque ao vivo: atualiza a cor de cada pincel compartilhado (tudo o que já está na tela muda junto).
    /// Devolve false se nada mudou. Deve rodar na thread de UI; quem chama refaz o que usa cores derivadas (degradês, tons).
    /// </summary>
    public static bool Apply(ThemePalette palette)
    {
        if (palette == Palette) return false;
        Palette = palette;
        Revision++;
        void Set(SolidColorBrush brush, uint argb) => brush.Color = ToColor(argb);
        Set(Background, palette.Background);
        Set(Surface, palette.Surface);
        Set(SurfaceRaised, palette.SurfaceRaised);
        Set(Border, palette.Border);
        Set(Text, palette.Text);
        Set(TextMuted, palette.TextMuted);
        Set(TextDisabled, palette.TextDisabled);
        Set(Accent, palette.Accent);
        Set(Primary, palette.Primary);
        Set(AccentSoft, palette.AccentSoft);
        Set(FocusGlow, palette.FocusGlow);
        Set(Selected, palette.Selected);
        Set(Danger, palette.Danger);
        Set(Scrim, palette.Scrim);
        Set(ModalScrimGlass, palette.ModalScrimGlass);
        Set(ModalScrimSolid, palette.ModalScrimSolid);
        Set(ModalEdge, palette.ModalEdge);
        Set(ModalDivider, palette.ModalDivider);
        Set(ModalInset, palette.ModalInset);
        Set(KeyFill, palette.KeyFill);
        Set(KeyFunctionFill, palette.KeyFunctionFill);
        Set(FocusText, palette.FocusText);
        Set(DangerFill, palette.DangerFill);
        Set(DisabledFill, palette.DisabledFill);
        Set(Success, palette.Success);
        return true;
    }

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

    /// <summary>Legendas e textos de apoio: 14 px, e nunca menos de 16 px nos portáteis (tela pequena, lida de longe).</summary>
    public static double FontCaption => Font(Layout.Tier == Core.Layout.LayoutTier.Compact ? 16 : 14);
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
            glow.CenterPoint = new System.Numerics.Vector3((float)(glow.ActualWidth / 2), (float)(glow.ActualHeight / 2), 0);
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

    // ---------- Modais (#172): painel fosco, linha focada preenchida, tons ----------

    /// <summary>
    /// Transparência reduzida (Configurações → Personalização → Cores → Efeitos de transparência desligados), alto
    /// contraste ou efeitos indisponíveis: o painel dos modais fica sólido e o fundo, mais escuro. Publicado pela janela.
    /// </summary>
    public static bool SolidSurfaces { get; set; }

    /// <summary>Animações do Windows desligadas (Acessibilidade → Efeitos visuais): modais aparecem sem transição.</summary>
    public static bool ReduceMotion { get; set; }

    /// <summary>Cor do vidro dos modais (a do tema) e a versão sólida usada sem transparência.</summary>
    public static Color ModalTintColor => ToColor(Palette.ModalTint);
    public static Color ModalSolidColor => ToColor(Palette.ModalSolid);

    /// <summary>Página por trás de um modal: escurecida (mais ainda sem transparência, para o painel se destacar).</summary>
    public static Brush ModalScrim => SolidSurfaces ? ModalScrimSolid : ModalScrimGlass;
    private static readonly SolidColorBrush ModalScrimGlass = new(ToColor(Palette.ModalScrimGlass));
    private static readonly SolidColorBrush ModalScrimSolid = new(ToColor(Palette.ModalScrimSolid));

    /// <summary>
    /// Material do painel: acrílico do WinUI (desfoca o que está atrás, dentro da janela) com tinta escura e opaca o
    /// bastante para ler a distância. O próprio WinUI troca pela cor de reserva quando o Windows desliga a transparência
    /// (economia de energia, área de trabalho remota); com <see cref="SolidSurfaces"/> a cor sólida é usada sempre.
    /// </summary>
    public static Brush ModalPanel() => SolidSurfaces
        ? new SolidColorBrush(ModalSolidColor)
        : new AcrylicBrush { TintColor = ModalTintColor, TintOpacity = 0.82, TintLuminosityOpacity = 0.9, FallbackColor = ModalSolidColor };

    /// <summary>Borda clara e fina do painel (o "fio de luz" que separa o vidro do fundo escurecido).</summary>
    public static readonly SolidColorBrush ModalEdge = new(ToColor(Palette.ModalEdge));

    /// <summary>Linhas divisórias e o fundo discreto das informações dentro do painel.</summary>
    public static readonly SolidColorBrush ModalDivider = new(ToColor(Palette.ModalDivider));
    public static readonly SolidColorBrush ModalInset = new(ToColor(Palette.ModalInset));

    /// <summary>Teclas do teclado virtual: de caractere (mais marcadas) e de função, sempre distintas do painel.</summary>
    public static readonly SolidColorBrush KeyFill = new(ToColor(Palette.KeyFill));
    public static readonly SolidColorBrush KeyFunctionFill = new(ToColor(Palette.KeyFunctionFill));

    /// <summary>
    /// Opção focada: preenchida com o destaque, negrito e um pouco maior; o texto por cima é escuro no tema escuro e
    /// branco no claro (contraste ≥ 4,5:1 em todas as cores de destaque). Perigosa focada: vermelho. Indisponível focada:
    /// cinza-azulado com o motivo por extenso.
    /// </summary>
    public static readonly SolidColorBrush FocusFill = Accent;
    public static readonly SolidColorBrush FocusText = new(ToColor(Palette.FocusText));
    public static readonly SolidColorBrush DangerFill = new(ToColor(Palette.DangerFill));
    public static readonly SolidColorBrush DisabledFill = new(ToColor(Palette.DisabledFill));

    /// <summary>Tons do cabeçalho: sucesso (verde), aviso (âmbar), erro (vermelho) e informação (ciano).</summary>
    public static readonly SolidColorBrush Success = new(ToColor(Palette.Success));
    public static readonly SolidColorBrush Warning = Selected;

    /// <summary>Aumento da opção focada (discreto, como nos menus de TV; não muda o layout).</summary>
    public const double ModalFocusScale = 1.025;

    /// <summary>Cantos do painel (~22 px a 1080p) e das linhas de opção.</summary>
    public static CornerRadius ModalRadius => new(Scaled(22));
    public static CornerRadius RowRadius => new(Scaled(12));

    /// <summary>Entrada do modal: só opacidade, curta, e nunca atrasa a entrada (o estado já é o do modal).</summary>
    public static readonly TimeSpan MotionModal = TimeSpan.FromMilliseconds(120);

    /// <summary>Cores das faces do Xbox nas legendas (A verde, B vermelho, X azul, Y amarelo) e a cor da letra por cima.</summary>
    public static (Color Body, Color Letter) XboxFace(Core.Actions.ControllerButton button) => button switch
    {
        Core.Actions.ControllerButton.FaceSouth => (ColorHelper.FromArgb(255, 0x2E, 0xB3, 0x4A), Colors.White),
        Core.Actions.ControllerButton.FaceEast => (ColorHelper.FromArgb(255, 0xE5, 0x39, 0x3F), Colors.White),
        Core.Actions.ControllerButton.FaceWest => (ColorHelper.FromArgb(255, 0x2C, 0x7F, 0xE8), Colors.White),
        _ => (ColorHelper.FromArgb(255, 0xF4, 0xC4, 0x2F), ColorHelper.FromArgb(255, 0x1A, 0x1A, 0x1A)),
    };
}
