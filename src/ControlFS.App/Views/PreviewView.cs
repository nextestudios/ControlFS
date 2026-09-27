using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace ControlFS.App.Views;

/// <summary>Visualizações internas (imagem). Só desenham o estado do AppController; zoom e posição vêm do modal.</summary>
public static partial class ModalView
{
    /// <summary>O modal é refeito a cada quadro: o bitmap de cada imagem decodificada é criado uma vez só.</summary>
    private static readonly ConditionalWeakTable<PreviewImage, WriteableBitmap> Bitmaps = [];

    private static Border BuildImagePreview(ImagePreviewModal modal)
    {
        var entry = modal.Current;
        var stack = new StackPanel { Spacing = Theme.SpaceXs };
        stack.Children.Add(new TextBlock { Text = entry.Name, FontSize = Theme.FontItem, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis });
        var details = new List<string> { $"{modal.Index + 1} de {modal.Images.Count}" };
        if (modal.Info is { } info) details.Add($"{info.Width} × {info.Height} · {info.Format.ToString().ToUpperInvariant()}");
        if (modal.Image is not null) details.Add(string.Create(CultureInfo.CurrentCulture, $"zoom {modal.Zoom * 100:0}%"));
        stack.Children.Add(new TextBlock { Text = string.Join(" · ", details), FontSize = Theme.FontCaption, Foreground = Theme.TextMuted });

        var chrome = 2 * (Theme.SpaceM + Theme.SpaceL);
        var boxWidth = Math.Max(200, Theme.Viewport.Width - chrome);
        var boxHeight = Math.Max(160, Theme.Viewport.Height - chrome - Theme.Scaled(150)); // título, detalhes e rodapé
        var area = new Grid
        {
            Width = boxWidth,
            Height = boxHeight,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, boxWidth, boxHeight) },
            Margin = new Thickness(0, Theme.SpaceS, 0, 0),
        };
        if (modal.Image is { } image)
        {
            if (!Bitmaps.TryGetValue(image, out var bitmap))
            {
                bitmap = new WriteableBitmap(image.Width, image.Height);
                using (var pixels = bitmap.PixelBuffer.AsStream()) pixels.Write(image.Pixels.Span);
                bitmap.Invalidate();
                Bitmaps.AddOrUpdate(image, bitmap);
            }
            // Ajusta à área sem ampliar além do tamanho real; o zoom amplia a partir daí, em torno do centro escolhido.
            var fit = Math.Min(1, Math.Min(boxWidth / image.Width, boxHeight / image.Height));
            var width = image.Width * fit;
            var height = image.Height * fit;
            var view = new Image
            {
                Source = bitmap,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new CompositeTransform
                {
                    CenterX = width / 2,
                    CenterY = height / 2,
                    ScaleX = modal.Zoom,
                    ScaleY = modal.Zoom,
                    TranslateX = (0.5 - modal.CenterX) * width * modal.Zoom,
                    TranslateY = (0.5 - modal.CenterY) * height * modal.Zoom,
                },
            };
            var described = modal.Info is { } i ? $"{i.Width} por {i.Height} pixels" : string.Empty;
            AutomationProperties.SetName(view, $"Imagem {entry.Name}, {described}, {modal.Index + 1} de {modal.Images.Count}");
            area.Children.Add(view);
        }
        else
        {
            var message = modal.IsLoading ? "Carregando imagem…" : "⚠ " + (modal.Error ?? "Não foi possível mostrar esta imagem.");
            var text = new TextBlock
            {
                Text = message,
                FontSize = Theme.FontBody,
                Foreground = modal.IsLoading ? Theme.TextMuted : Theme.Danger,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Theme.SpaceL),
            };
            AutomationProperties.SetLiveSetting(text, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            area.Children.Add(text);
        }
        stack.Children.Add(area);
        return Card(stack, Theme.Viewport.Width);
    }

    /// <summary>Colunas desenhadas por linha: o resto da linha fica fora da tela (Esquerda/Direita deslocam).</summary>
    private const int VisibleColumns = 400;

    private static readonly FontFamily MonospaceFont = new("Cascadia Mono, Consolas, Courier New");

    private static Border BuildTextPreview(AppController app, TextPreviewModal modal)
    {
        var stack = new StackPanel { Spacing = Theme.SpaceXs };
        stack.Children.Add(new TextBlock { Text = modal.Entry.Name, FontSize = Theme.FontItem, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis });

        var chrome = 2 * (Theme.SpaceM + Theme.SpaceL);
        var boxWidth = Math.Max(200, Theme.Viewport.Width - chrome);
        var boxHeight = Math.Max(160, Theme.Viewport.Height - chrome - Theme.Scaled(190)); // título, detalhes, aviso e rodapé
        var fontSize = Theme.FontBody;
        var lineHeight = Math.Ceiling(fontSize * 1.4);
        var pageLines = Math.Max(1, (int)((boxHeight - 2 * Theme.SpaceS) / lineHeight));
        app.ReportTextPreviewPage(pageLines);

        var document = modal.Document;
        if (document is not null)
        {
            var last = Math.Min(document.Lines.Count, modal.Top + pageLines);
            var details = string.Create(CultureInfo.CurrentCulture,
                $"{document.EncodingName} · {document.Lines.Count:N0} linhas · mostrando {(document.Lines.Count == 0 ? 0 : modal.Top + 1):N0}–{last:N0}") +
                (modal.Column > 0 ? string.Create(CultureInfo.CurrentCulture, $" · a partir da coluna {modal.Column + 1}") : string.Empty) +
                (modal.Monospace ? " · fonte fixa" : " · fonte proporcional");
            stack.Children.Add(new TextBlock { Text = details, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
            if (Core.Preview.TextPreview.TruncationNotice(document) is { } notice)
                stack.Children.Add(new TextBlock { Text = "⚠ " + notice, FontSize = Theme.FontCaption, Foreground = Theme.Selected, TextWrapping = TextWrapping.Wrap });
        }

        var area = new Grid
        {
            Width = boxWidth,
            Height = boxHeight,
            Background = Theme.SurfaceRaised,
            Padding = new Thickness(Theme.SpaceM, Theme.SpaceS, Theme.SpaceM, Theme.SpaceS),
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, boxWidth, boxHeight) },
            Margin = new Thickness(0, Theme.SpaceS, 0, 0),
        };
        if (document is not null && document.Lines.Count > 0)
        {
            area.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var numbers = new TextBlock { FontSize = fontSize, FontFamily = MonospaceFont, Foreground = Theme.TextMuted, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, Theme.SpaceM, 0) };
            var text = new TextBlock { FontSize = fontSize, FontFamily = modal.Monospace ? MonospaceFont : FontFamily.XamlAutoFontFamily, Foreground = Theme.Text, TextWrapping = TextWrapping.NoWrap };
            foreach (var block in new[] { numbers, text })
            {
                block.LineHeight = lineHeight;
                block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                block.IsTextSelectionEnabled = false;
            }
            var numberBuilder = new System.Text.StringBuilder();
            var textBuilder = new System.Text.StringBuilder();
            var end = Math.Min(document.Lines.Count, modal.Top + pageLines);
            for (var i = modal.Top; i < end; i++)
            {
                var line = document.Lines[i];
                var visible = modal.Column >= line.Length ? string.Empty : line.Substring(modal.Column, Math.Min(VisibleColumns, line.Length - modal.Column));
                if (i > modal.Top)
                {
                    numberBuilder.Append('\n');
                    textBuilder.Append('\n');
                }
                numberBuilder.Append((i + 1).ToString(CultureInfo.CurrentCulture));
                textBuilder.Append(visible);
            }
            numbers.Text = numberBuilder.ToString();
            text.Text = textBuilder.ToString();
            AutomationProperties.SetName(text, string.Create(CultureInfo.CurrentCulture, $"Linhas {modal.Top + 1} a {end} de {document.Lines.Count}: ") + text.Text);
            Grid.SetColumn(text, 1);
            area.Children.Add(numbers);
            area.Children.Add(text);
        }
        else
        {
            var message = modal.IsLoading ? "Lendo arquivo…" : modal.Error is { } error ? "⚠ " + error : "Arquivo vazio.";
            var text = new TextBlock
            {
                Text = message,
                FontSize = Theme.FontBody,
                Foreground = modal.Error is null ? Theme.TextMuted : Theme.Danger,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetLiveSetting(text, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            area.Children.Add(text);
        }
        stack.Children.Add(area);
        return Card(stack, Theme.Viewport.Width);
    }
}
