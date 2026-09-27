using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ControlFS.App.Controls;

/// <summary>
/// Painel que quebra linha quando os filhos não cabem na largura (legendas do rodapé). Em 1280×720 nenhuma legenda
/// fica escondida atrás de uma rolagem horizontal que o controle não alcança.
/// </summary>
public sealed class WrapPanel : Panel
{
    public double HorizontalSpacing { get; set; }

    public double VerticalSpacing { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        double x = 0, y = 0, lineHeight = 0, widest = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
            }
            x += size.Width + HorizontalSpacing;
            widest = Math.Max(widest, x - HorizontalSpacing);
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return new Size(double.IsInfinity(width) ? widest : Math.Min(widest, width), y + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        var line = new List<UIElement>();
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > finalSize.Width)
            {
                ArrangeLine(line, y, lineHeight);
                y += lineHeight + VerticalSpacing;
                x = 0;
                lineHeight = 0;
                line.Clear();
            }
            line.Add(child);
            x += size.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        ArrangeLine(line, y, lineHeight);
        return finalSize;
    }

    /// <summary>Filhos de uma linha centralizados verticalmente entre si (glifos e teclas têm alturas diferentes).</summary>
    private void ArrangeLine(List<UIElement> line, double y, double lineHeight)
    {
        double x = 0;
        foreach (var child in line)
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect(x, y + (lineHeight - size.Height) / 2, size.Width, size.Height));
            x += size.Width + HorizontalSpacing;
        }
    }
}
