using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ControlFS.App.Controls;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ControlFS.App.Views;

/// <summary>
/// Painel de detalhes à direita da lista (redesenho, fase C): ícone grande do Windows (ou a miniatura da imagem), nome,
/// tipo e as linhas reais do <see cref="AppController.Details"/> (caminho, conteúdo, tamanho, data, formato, dimensões,
/// uso da unidade). Só desenha: medir, ler e decodificar é do AppController, fora da thread de UI. O conteúdo só é
/// refeito quando o texto muda (foco, soma chegando, miniatura pronta).
/// </summary>
internal sealed class DetailsPanelView
{
    /// <summary>Tamanho do ícone grande, em pixels efetivos (o ícone "jumbo" do Shell, reduzido).</summary>
    public const double IconSize = 144;

    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    /// <summary>O bitmap de cada miniatura é criado uma vez só.</summary>
    private static readonly ConditionalWeakTable<PreviewImage, WriteableBitmap> Bitmaps = [];

    private readonly IconLoader _icons;
    private readonly Border _card = new();
    private readonly ScrollViewer _scroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsTabStop = false,
    };
    private readonly StackPanel _stack = new();
    private Image? _iconImage;
    private string _shownKey = string.Empty;

    public DetailsPanelView(IconLoader icons)
    {
        _icons = icons;
        _scroll.Content = _stack;
        _card.Child = _scroll;
        AutomationProperties.SetName(_card, "Detalhes");
    }

    public FrameworkElement Root => _card;

    /// <summary>Medidas da faixa de layout atual (refaz o conteúdo no próximo Render).</summary>
    public void ApplyLayout()
    {
        _card.CornerRadius = new CornerRadius(Theme.Scaled(14));
        _card.BorderThickness = Theme.Hairline;
        _card.BorderBrush = Theme.Border;
        // Fundo do cartão com um leve azul no topo (a referência usa uma onda; aqui só um degradê discreto).
        var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 0.55) };
        gradient.GradientStops.Add(new GradientStop { Color = Theme.DetailsGlowColor, Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = Theme.SurfaceRaised.Color, Offset = 1 });
        _card.Background = gradient;
        _stack.Padding = new Thickness(Theme.Space(32), Theme.Space(32), Theme.Space(28), Theme.Space(28));
        _shownKey = string.Empty;
    }

    public void Render(ItemDetails? details, IReadOnlySet<string> specialFolders)
    {
        var key = Key(details);
        if (key == _shownKey) return;
        _shownKey = key;
        if (_iconImage is not null) _icons.Cancel(_iconImage);
        _iconImage = null;
        _stack.Children.Clear();
        _stack.Spacing = Theme.Space(6);
        if (details is null)
        {
            _stack.Children.Add(new TextBlock { Text = "Nenhum item em foco.", FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
            AutomationProperties.SetName(_card, "Detalhes");
            return;
        }

        _stack.Children.Add(Hero(details, specialFolders));
        _stack.Children.Add(new TextBlock
        {
            Text = details.Title,
            FontSize = Theme.Font(30),
            FontWeight = FontWeights.SemiBold,
            Foreground = details.Entry.IsBlocked ? Theme.Danger : Theme.Text,
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, Theme.Space(20), 0, 0),
        });
        _stack.Children.Add(new TextBlock { Text = details.Subtitle, FontSize = Theme.Font(20), Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });

        var lines = new StackPanel { Spacing = Theme.Space(18), Margin = new Thickness(0, Theme.Space(24), 0, 0) };
        foreach (var line in details.Lines) lines.Children.Add(Line(line));
        if (details.Volume is { TotalBytes: > 0 } volume) lines.Children.Insert(Math.Min(1, lines.Children.Count), UsageBar(volume.UsedFraction));
        _stack.Children.Add(lines);
        if (details.Note is { Length: > 0 } note)
            _stack.Children.Add(new TextBlock { Text = note, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Theme.Space(16), 0, 0) });
        AutomationProperties.SetName(_card, "Detalhes: " + string.Join(", ", new[] { details.Title, details.Subtitle }
            .Concat(details.Lines.Select(l => l.Label is null ? l.Value : $"{l.Label} {l.Value}"))));
    }

    /// <summary>Muda quando qualquer texto, a miniatura ou o uso mudam (nada é refeito à toa a cada Render).</summary>
    private static string Key(ItemDetails? details) => details is null ? "-" : string.Join("\u001F", new[]
    {
        details.Entry.Id, details.Kind.ToString(), details.Title, details.Subtitle, details.Note ?? string.Empty,
        details.Thumbnail is null ? "0" : RuntimeHelpers.GetHashCode(details.Thumbnail).ToString(System.Globalization.CultureInfo.InvariantCulture),
        details.Volume?.ToString() ?? string.Empty, Theme.Layout.ToString(),
    }.Concat(details.Lines.Select(l => $"{l.Icon}|{l.Label}|{l.Value}")));

    /// <summary>Miniatura da imagem (quando pronta) ou o ícone grande do Windows com o símbolo de reserva.</summary>
    private Grid Hero(ItemDetails details, IReadOnlySet<string> specialFolders)
    {
        // Portáteis: ícone e área menores, para as linhas (caminho, tamanho, datas) aparecerem sem rolar.
        var compact = Theme.Layout.Tier == Core.Layout.LayoutTier.Compact;
        var height = Theme.Scaled(compact ? 120 : 200);
        var cell = new Grid { Height = height, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (details.Thumbnail is { } thumbnail)
        {
            if (!Bitmaps.TryGetValue(thumbnail, out var bitmap))
            {
                bitmap = new WriteableBitmap(thumbnail.Width, thumbnail.Height);
                using (var pixels = bitmap.PixelBuffer.AsStream()) pixels.Write(thumbnail.Pixels.Span);
                bitmap.Invalidate();
                Bitmaps.AddOrUpdate(thumbnail, bitmap);
            }
            var frame = new Border
            {
                CornerRadius = new CornerRadius(Theme.Scaled(10)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = height },
            };
            AutomationProperties.SetName(frame, "Miniatura de " + details.Title);
            cell.Children.Add(frame);
            return cell;
        }
        var size = Theme.Scaled(compact ? 96 : IconSize);
        var entry = details.Entry;
        var fallback = new TextBlock
        {
            Text = entry.IsBlocked ? "" : entry.IsSteamGame ? "\uE7FC" : details.Kind switch
            {
                DetailsKind.Selection => "\uE762",
                DetailsKind.Drive => "",
                DetailsKind.Place when entry.Id == RecycleBinLocation.PlaceId => "",
                DetailsKind.Place => "",
                DetailsKind.Folder => "",
                DetailsKind.Archive => "",
                DetailsKind.Image => "",
                _ => "",
            },
            FontFamily = new FontFamily(IconFont),
            FontSize = Math.Round(size * 0.6),
            Foreground = entry.IsBlocked ? Theme.Danger : details.Kind == DetailsKind.Selection ? Theme.Selected : details.Kind == DetailsKind.Place ? Theme.Accent : Theme.TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(fallback);
        cell.Children.Add(image);
        _iconImage = image;
        _icons.Load(image, fallback, entry.Id == AppController.RecentPlaceId || details.Kind is DetailsKind.ArchiveEntry or DetailsKind.Selection ? null : IconRequest.For(entry, specialFolders));
        return cell;
    }

    private static Grid Line(DetailsLine line)
    {
        var grid = new Grid { ColumnSpacing = Theme.Space(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.Scaled(30)) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = Glyph(line.Icon),
            FontFamily = new FontFamily(IconFont),
            FontSize = Theme.Font(24),
            Foreground = line.Icon == DetailsIcon.Warning ? Theme.Danger : line.Icon == DetailsIcon.Marked ? Theme.Selected : Theme.TextMuted,
            VerticalAlignment = line.Label is null ? VerticalAlignment.Center : VerticalAlignment.Top,
            Margin = new Thickness(0, line.Label is null ? 0 : Theme.Space(2), 0, 0),
        });
        var text = new StackPanel { Spacing = Theme.Space(2), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        if (line.Label is { } label) text.Children.Add(new TextBlock { Text = label, FontSize = Theme.Font(16), Foreground = Theme.TextMuted });
        text.Children.Add(new TextBlock
        {
            Text = line.Value,
            FontSize = Theme.Font(19),
            Foreground = line.Icon == DetailsIcon.Marked ? Theme.Selected : Theme.Text,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>Segoe Fluent Icons por tipo de linha (nunca emoji).</summary>
    private static string Glyph(DetailsIcon icon) => icon switch
    {
        DetailsIcon.Location => "",
        DetailsIcon.Items => "",
        DetailsIcon.Size => "",
        DetailsIcon.Date => "",
        DetailsIcon.Format => "",
        DetailsIcon.Dimensions => "",
        DetailsIcon.FileSystem => "",
        DetailsIcon.Free => "",
        DetailsIcon.Compression => "",
        DetailsIcon.Lock => "",
        DetailsIcon.Marked => "",
        DetailsIcon.Warning => "",
        _ => "",
    };

    /// <summary>Barra de uso da unidade (mesmo desenho dos cartões do início).</summary>
    private static Grid UsageBar(double used)
    {
        var height = Theme.Space(10);
        var bar = new Grid { Height = height, Margin = new Thickness(Theme.Scaled(30) + Theme.Space(18), 0, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(used, 0.001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - used, 0.001), GridUnitType.Star) });
        var track = new Border { Background = Theme.Border, CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0.5), EndPoint = new Windows.Foundation.Point(1, 0.5) };
        gradient.GradientStops.Add(new GradientStop { Color = Theme.Primary.Color, Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = Theme.Accent.Color, Offset = 1 });
        bar.Children.Add(new Border { Background = gradient, CornerRadius = new CornerRadius(height / 2) });
        return bar;
    }
}
