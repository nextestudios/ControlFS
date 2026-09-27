using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ControlFS.App.Resources;
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
}
