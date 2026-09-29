using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Preview;
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

    /// <summary>
    /// Área útil de uma visualização em tela cheia: a janela menos as margens, o cabeçalho e o rodapé do painel (medidos
    /// antes, porque o texto precisa saber quantas linhas cabem e a imagem, o tamanho de ajuste).
    /// </summary>
    private static (double Width, double Height) PreviewBox(AppController app, FrameworkElement header, double reserved = 0)
    {
        var inner = Math.Max(200, Theme.Viewport.Width - (2 * PanelMargin) - (2 * PanelPadding) - 2);
        header.Measure(new Size(inner, double.PositiveInfinity));
        var footer = Footer(app, statusBand: true);
        footer.Measure(new Size(inner, double.PositiveInfinity));
        var chrome = (2 * PanelMargin) + (PanelPadding - Theme.SpaceXs) + Theme.Space(20) + (3 * Theme.SpaceM) + Theme.Hairline.Top + 2;
        var height = Theme.Viewport.Height - chrome - header.DesiredSize.Height - footer.DesiredSize.Height - reserved - Theme.SpaceXs;
        return (inner, Math.Max(160, height));
    }

    private static Border BuildImagePreview(AppController app, ImagePreviewModal modal)
    {
        var entry = modal.Current;
        var details = new List<string> { $"{modal.Index + 1} de {modal.Images.Count}" };
        if (modal.Info is { } info) details.Add($"{info.Width} × {info.Height} · {info.Format.ToString().ToUpperInvariant()}");
        if (modal.Image is not null) details.Add(string.Create(CultureInfo.CurrentCulture, $"zoom {modal.Zoom * 100:0}%"));
        if (modal.HintsFaded) details.Add("qualquer botão mostra os comandos");
        var header = Header(entry.Name, modal.Icon, string.Join(" · ", details));
        var (boxWidth, boxHeight) = PreviewBox(app, header);
        var area = PreviewArea(boxWidth, boxHeight);
        if (modal.Image is { } image)
        {
            var described = modal.Info is { } i ? $"{i.Width} por {i.Height} pixels" : string.Empty;
            area.Children.Add(ZoomedImage(image, modal, boxWidth, boxHeight, $"Imagem {entry.Name}, {described}, {modal.Index + 1} de {modal.Images.Count}"));
        }
        else
        {
            area.Children.Add(modal.IsLoading ? PreviewMessage("Carregando imagem…", error: false) : PreviewMessage(modal.Error ?? "Não foi possível mostrar esta imagem.", error: true));
        }
        return Panel(app, header, area, modal.Size, scroll: false, fadedHints: modal.HintsFaded);
    }

    /// <summary>
    /// Conteúdo decodificado ajustado à área sem ampliar além do tamanho real; o zoom amplia a partir daí, em torno do
    /// centro escolhido. O bitmap de cada imagem é criado uma vez só (o modal é refeito a cada quadro).
    /// </summary>
    private static Image ZoomedImage(PreviewImage image, ZoomablePreviewModal modal, double boxWidth, double boxHeight, string name)
    {
        if (!Bitmaps.TryGetValue(image, out var bitmap))
        {
            bitmap = new WriteableBitmap(image.Width, image.Height);
            using (var pixels = bitmap.PixelBuffer.AsStream()) pixels.Write(image.Pixels.Span);
            bitmap.Invalidate();
            Bitmaps.AddOrUpdate(image, bitmap);
        }
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
        AutomationProperties.SetName(view, name);
        return view;
    }

    /// <summary>Área escura da visualização, recortada nos cantos (o zoom nunca vaza para fora dela).</summary>
    private static Grid PreviewArea(double width, double height) => new()
    {
        Width = width,
        Height = height,
        Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) },
        CornerRadius = Theme.RowRadius,
    };

    /// <summary>Aviso no centro da área (carregando, erro); lido pelo Narrador quando muda.</summary>
    private static TextBlock PreviewMessage(string message, bool error)
    {
        var text = new TextBlock
        {
            Text = error ? "⚠ " + message : message,
            FontSize = Theme.FontBody,
            Foreground = error ? Theme.Danger : Theme.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Theme.SpaceL),
        };
        AutomationProperties.SetLiveSetting(text, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        return text;
    }

    private static Border BuildPdfPreview(AppController app, PdfPreviewModal modal)
    {
        var details = new List<string>();
        if (modal.PageCount > 0)
            details.Add(string.Create(CultureInfo.CurrentCulture, $"página {modal.PageIndex + 1} de {modal.PageCount}") +
                (modal.TotalPages > modal.PageCount ? string.Create(CultureInfo.CurrentCulture, $" (o PDF tem {modal.TotalPages})") : string.Empty));
        if (modal.Page is not null) details.Add(string.Create(CultureInfo.CurrentCulture, $"zoom {modal.Zoom * 100:0}%"));
        if (modal.HintsFaded) details.Add("qualquer botão mostra os comandos");
        var header = Header(modal.Entry.Name, modal.Icon, details.Count > 0 ? string.Join(" · ", details) : null);
        var (boxWidth, boxHeight) = PreviewBox(app, header);
        var area = PreviewArea(boxWidth, boxHeight);
        if (modal.Page is { } page)
            area.Children.Add(ZoomedImage(page, modal, boxWidth, boxHeight, string.Create(CultureInfo.CurrentCulture, $"PDF {modal.Entry.Name}, página {modal.PageIndex + 1} de {modal.PageCount}")));
        else if (modal.Error is { } error)
            area.Children.Add(PreviewMessage(modal.NeedsPassword ? error + " Confirme para digitar a senha." : error, error: true));
        else
            area.Children.Add(PreviewMessage(modal.PageCount > 0 ? "Desenhando a página…" : "Abrindo PDF…", error: false));
        return Panel(app, header, area, modal.Size, scroll: false, fadedHints: modal.HintsFaded);
    }

    /// <summary>Barra de progresso da reprodução: trilho escuro, parte tocada no ciano do tema.</summary>
    private static Grid ProgressTrack(double fraction, double height)
    {
        var track = new Grid { Height = height, CornerRadius = new CornerRadius(height / 2), Background = Theme.SurfaceRaised };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(fraction, 0, 1), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(fraction, 0, 1), GridUnitType.Star) });
        track.Children.Add(new Border { Background = Theme.Accent, CornerRadius = new CornerRadius(height / 2) });
        AutomationProperties.SetAccessibilityView(track, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return track;
    }

    private static string StateLabel(MediaStatus status) => status.State switch
    {
        MediaPlaybackState.Opening => "Abrindo…",
        MediaPlaybackState.Buffering => "Carregando…",
        MediaPlaybackState.Playing => "▶ Tocando",
        MediaPlaybackState.Paused => "⏸ Pausado",
        MediaPlaybackState.Ended => "Fim",
        _ => string.Empty,
    };

    private static string VolumeLabel(MediaStatus status) =>
        status.IsMuted ? "Sem som" : string.Create(CultureInfo.CurrentCulture, $"Volume {status.Volume * 100:0}%");

    /// <summary>Áudio (#60): estado, tempo decorrido/total, barra de progresso e volume, legíveis de longe.</summary>
    private static Border BuildAudioPreview(AppController app, AudioPreviewModal modal)
    {
        var status = modal.Status;
        var header = Header(modal.Entry.Name, modal.Icon, "Áudio · " + modal.Entry.Extension.TrimStart('.').ToUpperInvariant());
        var body = new StackPanel { Spacing = Theme.SpaceM, Padding = new Thickness(0, Theme.SpaceS, 0, Theme.SpaceS) };
        if (modal.DisplayError is { } error)
        {
            body.Children.Add(PreviewMessage(error, error: true));
            return Panel(app, header, body, modal.Size);
        }
        var state = new TextBlock { Text = StateLabel(status), FontSize = Theme.FontTitle, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text };
        AutomationProperties.SetLiveSetting(state, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        body.Children.Add(state);
        var fraction = status.Duration > TimeSpan.Zero ? status.Position / status.Duration : 0;
        body.Children.Add(ProgressTrack(fraction, Theme.Scaled(10)));
        var times = new Grid();
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var elapsed = MediaPreviewPolicy.FormatTime(status.Position) + (status.Duration > TimeSpan.Zero ? " / " + MediaPreviewPolicy.FormatTime(status.Duration) : string.Empty);
        times.Children.Add(new TextBlock { Text = elapsed, FontSize = Theme.FontBody, Foreground = Theme.Text });
        var volume = new TextBlock { Text = VolumeLabel(status), FontSize = Theme.FontBody, Foreground = status.IsMuted ? Theme.Warning : Theme.TextMuted };
        Grid.SetColumn(volume, 1);
        times.Children.Add(volume);
        AutomationProperties.SetName(times, $"{StateLabel(status)}, {elapsed}, {VolumeLabel(status)}");
        body.Children.Add(times);
        return Panel(app, header, body, modal.Size);
    }

    /// <summary>Colunas desenhadas por linha: o resto da linha fica fora da tela (Esquerda/Direita deslocam).</summary>
    private const int VisibleColumns = 400;

    private static readonly FontFamily MonospaceFont = new("Cascadia Mono, Consolas, Courier New");

    private static Border BuildTextPreview(AppController app, TextPreviewModal modal)
    {
        var document = modal.Document;
        var editor = modal.Editor;
        var count = modal.DisplayLineCount;
        string? Details(int pageLines) => editor is not null
            ? string.Create(CultureInfo.CurrentCulture, $"Editando · {editor.Document.EncodingName} · {count:N0} linhas · linha {editor.Cursor + 1:N0}") +
                (editor.IsModified ? " · modificado (Start salva)" : " · sem alterações")
            : document is null ? null
            : string.Create(CultureInfo.CurrentCulture,
                $"{document.EncodingName} · {document.Lines.Count:N0} linhas · mostrando {(document.Lines.Count == 0 ? 0 : modal.Top + 1):N0}–{Math.Min(document.Lines.Count, modal.Top + pageLines):N0}") +
                (modal.Column > 0 ? string.Create(CultureInfo.CurrentCulture, $" · a partir da coluna {modal.Column + 1}") : string.Empty) +
                (modal.Monospace ? " · fonte fixa" : " · fonte proporcional");
        var body = new StackPanel { Spacing = Theme.SpaceS };
        var reserved = 0.0;
        if (editor is null && document is not null && Core.Preview.TextPreview.TruncationNotice(document) is { } notice)
        {
            var truncated = new TextBlock { Text = "⚠ " + notice, FontSize = Theme.FontCaption, Foreground = Theme.Warning, TextWrapping = TextWrapping.Wrap };
            truncated.Measure(new Size(Math.Max(200, Theme.Viewport.Width - (2 * PanelMargin) - (2 * PanelPadding)), double.PositiveInfinity));
            reserved = truncated.DesiredSize.Height + Theme.SpaceS;
            body.Children.Add(truncated);
        }
        // O cabeçalho tem sempre uma linha de contexto: mede com ela para saber quantas linhas de texto cabem.
        var (boxWidth, boxHeight) = PreviewBox(app, Header(modal.Entry.Name, modal.Icon, "·"), reserved);
        var fontSize = Theme.FontBody;
        var lineHeight = Math.Ceiling(fontSize * 1.4);
        var pageLines = Math.Max(1, (int)((boxHeight - 2 * Theme.SpaceS) / lineHeight));
        app.ReportTextPreviewPage(pageLines);
        var header = Header(modal.Entry.Name, modal.Icon, Details(pageLines) ?? (modal.IsLoading ? "Lendo arquivo…" : null));

        var area = new Grid
        {
            Width = boxWidth,
            Height = boxHeight,
            Background = Theme.ModalInset,
            Padding = new Thickness(Theme.SpaceM, Theme.SpaceS, Theme.SpaceM, Theme.SpaceS),
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, boxWidth, boxHeight) },
            CornerRadius = Theme.RowRadius,
        };
        if (count > 0)
        {
            // Edição (#62): a linha em foco ganha uma faixa atrás do texto (a mesma cor do foco dos menus, suave).
            if (editor is not null && editor.Cursor >= modal.Top && editor.Cursor < modal.Top + pageLines)
            {
                var band = new Border
                {
                    Background = Theme.AccentSoft,
                    BorderBrush = Theme.Accent,
                    BorderThickness = new Thickness(Theme.Scaled(3), 0, 0, 0),
                    Height = lineHeight,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(-Theme.SpaceM, (editor.Cursor - modal.Top) * lineHeight, -Theme.SpaceM, 0),
                };
                Grid.SetColumnSpan(band, 2);
                area.Children.Add(band);
            }
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
            var end = Math.Min(count, modal.Top + pageLines);
            for (var i = modal.Top; i < end; i++)
            {
                var line = modal.DisplayLine(i);
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
            AutomationProperties.SetName(text, editor is not null
                ? string.Create(CultureInfo.CurrentCulture, $"Editando a linha {editor.Cursor + 1} de {count}: ") + editor.Lines[editor.Cursor].Text
                : string.Create(CultureInfo.CurrentCulture, $"Linhas {modal.Top + 1} a {end} de {count}: ") + text.Text);
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
        body.Children.Add(area);
        return Panel(app, header, body, modal.Size, scroll: false);
    }
}
