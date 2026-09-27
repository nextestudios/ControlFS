using ControlFS.Core.Actions;
using ControlFS.Core.Input;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace ControlFS.App.Resources;

/// <summary>Cores de um glifo: corpo do botão e símbolo por cima. Escuro para o tema atual; claro para fundos claros.</summary>
public sealed record GlyphPalette(Brush Body, Brush Symbol)
{
    public static GlyphPalette Dark { get; } = new(Theme.Text, Theme.Background);

    public static GlyphPalette Light { get; } = new(new SolidColorBrush(Theme.BackgroundColor), new SolidColorBrush(Colors.White));
}

/// <summary>
/// Glifos vetoriais ORIGINAIS dos botões de controle, desenhados só com formas geométricas (círculos, retângulos,
/// polígonos e letras). Nenhuma imagem, fonte de ícones ou logotipo de Xbox, PlayStation ou Nintendo é usado.
/// Cada glifo é desenhado numa grade de 24 unidades de altura e escalado por um <see cref="Viewbox"/>: continua
/// vetorial (nítido de 100% a 300% de escala). A largura é 24 (botões redondos) ou 36 (ombros, gatilhos, pílulas).
/// </summary>
public static class ControllerGlyphs
{
    private const double Unit = 24;
    private const double Wide = 36;

    /// <summary>Glifo com a altura pedida (use ~1,4× o tamanho da fonte ao lado para alinhar com o texto).</summary>
    public static FrameworkElement Create(ControllerButton button, ControllerFamily family, double height, GlyphPalette? palette = null)
    {
        var p = palette ?? GlyphPalette.Dark;
        var canvas = Draw(button, family, p);
        var box = new Viewbox { Height = height, Stretch = Stretch.Uniform, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, ControllerButtons.SpokenName(button, family));
        return box;
    }

    private static Grid Draw(ControllerButton button, ControllerFamily family, GlyphPalette p) => button switch
    {
        ControllerButton.FaceSouth or ControllerButton.FaceEast or ControllerButton.FaceWest or ControllerButton.FaceNorth => Face(button, family, p),
        ControllerButton.LeftShoulder or ControllerButton.RightShoulder => Shoulder(ControllerButtons.SpokenName(button, family), p),
        ControllerButton.LeftTrigger or ControllerButton.RightTrigger => Trigger(ControllerButtons.SpokenName(button, family), p),
        ControllerButton.Start or ControllerButton.Select => SystemButton(button == ControllerButton.Start, family, p),
        ControllerButton.LeftStick or ControllerButton.RightStick => Stick(button == ControllerButton.LeftStick ? "L" : "R", pressed: false, p),
        ControllerButton.LeftStickClick or ControllerButton.RightStickClick => Stick(button == ControllerButton.LeftStickClick ? "L" : "R", pressed: true, p),
        _ => DPad(button, p),
    };

    // ---------- Botões de face ----------

    private static Grid Face(ControllerButton button, ControllerFamily family, GlyphPalette p)
    {
        var g = Box(Unit);
        if (family == ControllerFamily.Xbox)
        {
            // Xbox: as cores das faces (A verde, B vermelho, X azul, Y amarelo) ajudam a achar o botão; a letra continua.
            var (body, letter) = Theme.XboxFace(button);
            g.Children.Add(Disc(new SolidColorBrush(body)));
            g.Children.Add(Letter(ControllerButtons.FaceLetter(button, family), new SolidColorBrush(letter), 14));
            return g;
        }
        g.Children.Add(Disc(p.Body));
        switch (family)
        {
            case ControllerFamily.PlayStation:
                FaceShape(g, button, p.Symbol);
                break;
            case ControllerFamily.Nintendo:
                g.Children.Add(Letter(ControllerButtons.FaceLetter(button, family), p.Symbol, 14));
                break;
            default:
                PositionDots(g, button, p.Symbol);
                break;
        }
        return g;
    }

    /// <summary>Cruz, círculo, quadrado e triângulo desenhados como traços simples (PlayStation, por posição).</summary>
    private static void FaceShape(Grid g, ControllerButton button, Brush ink)
    {
        const double stroke = 2.2;
        switch (button)
        {
            case ControllerButton.FaceSouth:
                g.Children.Add(Segment(7, 7, 17, 17, ink, stroke));
                g.Children.Add(Segment(17, 7, 7, 17, ink, stroke));
                break;
            case ControllerButton.FaceEast:
                g.Children.Add(Place(new Ellipse { Width = 12, Height = 12, Stroke = ink, StrokeThickness = stroke }, 6, 6));
                break;
            case ControllerButton.FaceWest:
                g.Children.Add(Place(new Rectangle { Width = 11, Height = 11, Stroke = ink, StrokeThickness = stroke }, 6.5, 6.5));
                break;
            default:
                g.Children.Add(Shape(new Polygon { Stroke = ink, StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round }, (12, 5.5), (18.5, 16.5), (5.5, 16.5)));
                break;
        }
    }

    /// <summary>Genérico: quatro pontos em losango; o do botão em questão vem cheio (posição, não rótulo).</summary>
    private static void PositionDots(Grid g, ControllerButton button, Brush ink)
    {
        (ControllerButton Button, double X, double Y)[] dots =
        [
            (ControllerButton.FaceNorth, 12, 6.5), (ControllerButton.FaceEast, 17.5, 12),
            (ControllerButton.FaceSouth, 12, 17.5), (ControllerButton.FaceWest, 6.5, 12),
        ];
        foreach (var (b, x, y) in dots)
        {
            var active = b == button;
            var r = active ? 3.2 : 2.2;
            var dot = new Ellipse { Width = r * 2, Height = r * 2 };
            if (active) dot.Fill = ink;
            else { dot.Stroke = ink; dot.StrokeThickness = 1.2; }
            g.Children.Add(Place(dot, x - r, y - r));
        }
    }

    // ---------- Ombros e gatilhos ----------

    private static Grid Shoulder(string label, GlyphPalette p)
    {
        var g = Box(Wide);
        g.Children.Add(Place(new Rectangle { Width = 34, Height = 15, RadiusX = 5, RadiusY = 5, Fill = p.Body }, 1, 4.5));
        g.Children.Add(Letter(label, p.Symbol, 10.5));
        return g;
    }

    /// <summary>Gatilho: topo bem arredondado e base reta, diferente do ombro (pílula baixa).</summary>
    private static Grid Trigger(string label, GlyphPalette p)
    {
        var g = Box(Wide);
        g.Children.Add(Place(new Rectangle { Width = 30, Height = 22, RadiusX = 10, RadiusY = 10, Fill = p.Body }, 3, 1));
        g.Children.Add(Place(new Rectangle { Width = 30, Height = 11, Fill = p.Body }, 3, 12));
        var text = Letter(label, p.Symbol, 10.5);
        text.Margin = new Thickness(0, 3, 0, 0);
        g.Children.Add(text);
        return g;
    }

    // ---------- Start/Select (Menu/Exibir, Options/Create, +/−) ----------

    private static Grid SystemButton(bool start, ControllerFamily family, GlyphPalette p)
    {
        switch (family)
        {
            case ControllerFamily.Xbox:
            {
                var g = Box(Unit);
                g.Children.Add(Disc(p.Body));
                if (start) Lines(g, p.Symbol, 7, 17, 8, 12, 16);
                else
                {
                    // duas janelas sobrepostas
                    g.Children.Add(Place(new Rectangle { Width = 8, Height = 7, Stroke = p.Symbol, StrokeThickness = 1.6 }, 6.5, 7));
                    g.Children.Add(Place(new Rectangle { Width = 8, Height = 7, Fill = p.Symbol }, 9.5, 10));
                }
                return g;
            }
            case ControllerFamily.Nintendo:
            {
                var g = Box(Unit);
                g.Children.Add(Disc(p.Body));
                g.Children.Add(Place(new Rectangle { Width = 11, Height = 2.6, Fill = p.Symbol }, 6.5, 10.7));
                if (start) g.Children.Add(Place(new Rectangle { Width = 2.6, Height = 11, Fill = p.Symbol }, 10.7, 6.5));
                return g;
            }
            case ControllerFamily.PlayStation:
            {
                var g = Pill(p);
                if (start) Lines(g, p.Symbol, 12, 24, 7.5, 12, 16.5);
                else
                {
                    // caneta: traço diagonal com ponta
                    g.Children.Add(Segment(13, 16, 21, 8, p.Symbol, 2));
                    g.Children.Add(Shape(new Polygon { Fill = p.Symbol }, (11.5, 17.5), (12.2, 14.4), (14.6, 16.8)));
                }
                return g;
            }
            default:
            {
                var g = Pill(p);
                if (start) g.Children.Add(Shape(new Polygon { Fill = p.Symbol }, (14.5, 7), (22.5, 12), (14.5, 17)));
                else g.Children.Add(Place(new Rectangle { Width = 8, Height = 8, Stroke = p.Symbol, StrokeThickness = 1.8 }, 14, 8));
                return g;
            }
        }
    }

    private static Grid Pill(GlyphPalette p)
    {
        var g = Box(Wide);
        g.Children.Add(Place(new Rectangle { Width = 32, Height = 16, RadiusX = 8, RadiusY = 8, Fill = p.Body }, 2, 4));
        return g;
    }

    // ---------- Direcional e analógicos ----------

    /// <summary>
    /// Cruz do direcional; setas marcam as direções que a legenda usa. Numa direção só (ou num eixo), os braços que não
    /// valem ficam esmaecidos: a direção é lida pela forma, não só pela seta pequena (legível a 100% e a distância).
    /// </summary>
    private static Grid DPad(ControllerButton button, GlyphPalette p)
    {
        var g = Box(Unit);
        var up = button is ControllerButton.DPad or ControllerButton.DPadUp or ControllerButton.DPadVertical;
        var down = button is ControllerButton.DPad or ControllerButton.DPadDown or ControllerButton.DPadVertical;
        var left = button is ControllerButton.DPad or ControllerButton.DPadLeft or ControllerButton.DPadHorizontal;
        var right = button is ControllerButton.DPad or ControllerButton.DPadRight or ControllerButton.DPadHorizontal;
        var all = up && down && left && right;
        g.Children.Add(Shape(new Polygon { Fill = p.Body, Opacity = all ? 1 : 0.35 },
            (8.5, 1), (15.5, 1), (15.5, 8.5), (23, 8.5), (23, 15.5), (15.5, 15.5),
            (15.5, 23), (8.5, 23), (8.5, 15.5), (1, 15.5), (1, 8.5), (8.5, 8.5)));
        if (!all)
        {
            g.Children.Add(Place(new Rectangle { Width = 7, Height = 7, Fill = p.Body }, 8.5, 8.5)); // centro
            if (up) g.Children.Add(Place(new Rectangle { Width = 7, Height = 8, Fill = p.Body }, 8.5, 1));
            if (down) g.Children.Add(Place(new Rectangle { Width = 7, Height = 8, Fill = p.Body }, 8.5, 15));
            if (left) g.Children.Add(Place(new Rectangle { Width = 8, Height = 7, Fill = p.Body }, 1, 8.5));
            if (right) g.Children.Add(Place(new Rectangle { Width = 8, Height = 7, Fill = p.Body }, 15, 8.5));
        }
        if (up) g.Children.Add(Shape(new Polygon { Fill = p.Symbol }, (12, 2.4), (15, 6.8), (9, 6.8)));
        if (down) g.Children.Add(Shape(new Polygon { Fill = p.Symbol }, (12, 21.6), (15, 17.2), (9, 17.2)));
        if (left) g.Children.Add(Shape(new Polygon { Fill = p.Symbol }, (2.4, 12), (6.8, 9), (6.8, 15)));
        if (right) g.Children.Add(Shape(new Polygon { Fill = p.Symbol }, (21.6, 12), (17.2, 9), (17.2, 15)));
        return g;
    }

    /// <summary>
    /// Analógico: anel externo e topo interno. Pressionado (L3/R3): disco cheio com a letra em cima e uma seta para
    /// baixo (apertar), para não ser confundido com o analógico solto em tamanhos pequenos.
    /// </summary>
    private static Grid Stick(string side, bool pressed, GlyphPalette p)
    {
        var g = Box(Unit);
        if (pressed)
        {
            g.Children.Add(Disc(p.Body));
            var letter = Letter(side, p.Symbol, 9.5);
            letter.Margin = new Thickness(0, 0, 0, 5);
            g.Children.Add(letter);
            g.Children.Add(Shape(new Polygon { Fill = p.Symbol }, (8.5, 15), (15.5, 15), (12, 19)));
            return g;
        }
        g.Children.Add(Place(new Ellipse { Width = 21, Height = 21, Stroke = p.Body, StrokeThickness = 1.8 }, 1.5, 1.5));
        g.Children.Add(Place(new Ellipse { Width = 15, Height = 15, Fill = p.Body }, 4.5, 4.5));
        g.Children.Add(Letter(side, p.Symbol, 9.5));
        return g;
    }

    // ---------- Primitivas ----------

    private static Grid Box(double width) => new() { Width = width, Height = Unit };

    private static Ellipse Disc(Brush fill) => Place(new Ellipse { Width = 22, Height = 22, Fill = fill }, 1, 1);

    private static TextBlock Letter(string text, Brush ink, double size) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.Bold,
        Foreground = ink,
        TextLineBounds = TextLineBounds.Tight, // centraliza pela altura das maiúsculas, não pela linha
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsTextScaleFactorEnabled = false, // o Viewbox já decide o tamanho
    };

    private static void Lines(Grid g, Brush ink, double x1, double x2, params double[] ys)
    {
        foreach (var y in ys) g.Children.Add(Segment(x1, y, x2, y, ink, 1.8));
    }

    private static Line Segment(double x1, double y1, double x2, double y2, Brush ink, double thickness) =>
        Place(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = ink, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }, 0, 0);

    private static Polygon Shape(Polygon polygon, params (double X, double Y)[] points)
    {
        var collection = new PointCollection();
        foreach (var (x, y) in points) collection.Add(new Point(x, y));
        polygon.Points = collection;
        return Place(polygon, 0, 0);
    }

    /// <summary>Posiciona um elemento em coordenadas absolutas da grade (topo-esquerda).</summary>
    private static T Place<T>(T element, double x, double y)
        where T : FrameworkElement
    {
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.VerticalAlignment = VerticalAlignment.Top;
        element.Margin = new Thickness(x, y, 0, 0);
        return element;
    }
}
