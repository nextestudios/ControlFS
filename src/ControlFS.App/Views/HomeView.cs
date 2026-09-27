using ControlFS.App.Controls;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ControlFS.App.Views;

/// <summary>
/// Início na grade (redesenho, fase B): seções "Favoritos", "Pastas principais", "Unidades e dispositivos" e "Outros
/// locais", com cartões. Pastas mostram o caminho real e "N itens • tamanho" (calculado pelo AppController, fora da
/// thread de UI); unidades mostram a barra de uso e "X livres de Y". As colunas saem da largura disponível e são
/// publicadas no AppController, que navega em 2D com as mesmas. Os cartões só são refeitos quando os locais ou as
/// colunas mudam; foco e contagens atualizam os elementos existentes.
/// </summary>
internal sealed class HomeView
{
    /// <summary>Tamanho do ícone dos cartões, em pixels efetivos (ícone grande das pastas do Windows).</summary>
    public const double IconSize = 96;

    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private const string ChevronGlyph = "";

    private readonly AppController _app;
    private readonly IconLoader _icons;
    private readonly ScrollViewer _scroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsTabStop = false,
        Visibility = Visibility.Collapsed, // a janela mostra no início em grade
    };
    private readonly StackPanel _sections = new();
    private readonly Dictionary<int, Card> _cards = [];
    private IReadOnlyList<FileEntry>? _shownPlaces;
    private string _shownColumns = string.Empty;
    private int _shownFocus = -2;

    private sealed record Card(Border Ring, Border Glow, TextBlock Chevron, Image Icon, TextBlock? Stats, string? StatsPath, bool FirstRow, FrameworkElement Section);

    public HomeView(AppController app, IconLoader icons)
    {
        _app = app;
        _icons = icons;
        _scroll.Content = _sections;
        _scroll.SizeChanged += (_, _) => SizeChanged?.Invoke();
        AutomationProperties.SetName(_scroll, "Início");
    }

    public FrameworkElement Root => _scroll;

    /// <summary>A largura mudou: a janela pede um novo Render (as colunas podem mudar).</summary>
    public event Action? SizeChanged;

    /// <summary>Medidas da faixa de layout atual: refaz os cartões no próximo Render.</summary>
    public void ApplyLayout()
    {
        _sections.Padding = new Thickness(PageGutter, Theme.Space(28), PageGutter, Theme.Space(32));
        _shownPlaces = null;
    }

    /// <summary>Margem lateral: os cartões ficam um pouco para dentro da barra superior, como na referência.</summary>
    private static double PageGutter => Theme.SpaceL + Theme.Space(28);

    private static double Gap => Theme.Space(24);

    /// <summary>Largura mínima de um cartão de pasta/unidade: define as colunas (1080p: 3 pastas, 2 unidades; portátil: 2 e 1).</summary>
    private static double FolderMinWidth => Theme.Scaled(480) * (Theme.Layout.Tier == Core.Layout.LayoutTier.Large ? 0.9 : 1);

    private static double DriveMinWidth => Theme.Scaled(640) * (Theme.Layout.Tier == Core.Layout.LayoutTier.Large ? 0.9 : 1);

    private double Available => _scroll.ActualWidth - _sections.Padding.Left - _sections.Padding.Right;

    /// <summary>Colunas por seção para a largura disponível. Pastas: até 3 (6 pastas = 3×2); unidades: até 4, nunca mais que as unidades.</summary>
    private Dictionary<HomeSectionKind, int> Columns(IReadOnlyList<HomeSection> sections)
    {
        var available = Available;
        int Fit(double min) => available > 0 ? Math.Max(1, (int)Math.Floor((available + Gap) / (min + Gap))) : 1;
        var columns = new Dictionary<HomeSectionKind, int>();
        foreach (var section in sections)
        {
            columns[section.Kind] = section.Kind == HomeSectionKind.Drives
                ? Math.Clamp(Math.Min(section.Places.Count, Fit(DriveMinWidth)), 1, 4)
                : Math.Clamp(Fit(FolderMinWidth), 1, 3);
        }
        return columns;
    }

    /// <summary>Início em grade: os locais em seções.</summary>
    public void RenderHome()
    {
        var focus = _app.FocusRegion == PaneRegion.List && _app.TopModal is null ? _app.PlacesFocus : -1;
        Render(_app.Places, _app.HomeSections, focus, _app.SetHomeGridLayout);
    }

    /// <summary>Meu computador em grade: as unidades da aba como cartões (mesmo desenho do início).</summary>
    public void RenderThisPc(PaneState pane)
    {
        var items = pane.List.Items;
        if (!ReferenceEquals(items, _thisPcSource))
        {
            _thisPcSource = items;
            _thisPcSections = items.Count == 0 ? [] : [new HomeSection(HomeSectionKind.Drives, "Unidades e dispositivos", [.. Enumerable.Range(0, items.Count)])];
        }
        var focus = _app.FocusRegion == PaneRegion.List && _app.TopModal is null ? pane.List.FocusIndex : -1;
        Render(items, _thisPcSections, focus, columns => _app.SetGridLayout(columns.GetValueOrDefault(HomeSectionKind.Drives, 1), 1));
    }

    private IReadOnlyList<FileEntry>? _thisPcSource;
    private IReadOnlyList<HomeSection> _thisPcSections = [];

    /// <summary>Página vai ser mostrada: força refazer (a mesma view serve o início e Meu computador).</summary>
    public void Reset() => _shownPlaces = null;

    private void Render(IReadOnlyList<FileEntry> places, IReadOnlyList<HomeSection> sections, int focus, Action<Dictionary<HomeSectionKind, int>> publish)
    {
        if (_scroll.Visibility != Visibility.Visible) return;
        var columns = Columns(sections);
        var columnsKey = string.Join(",", columns.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"));
        var rebuilt = false;
        if (!ReferenceEquals(places, _shownPlaces) || columnsKey != _shownColumns)
        {
            _shownPlaces = places;
            _shownColumns = columnsKey;
            _shownFocus = -2;
            publish(columns);
            Build(places, sections, columns);
            rebuilt = true;
        }

        // Contagens e tamanhos chegam aos poucos: só o texto muda.
        foreach (var card in _cards.Values)
            if (card is { Stats: { } stats, StatsPath: { } path }) stats.Text = EntryText.FolderStats(_app.FolderStatsFor(path));

        if (focus == _shownFocus) return;
        if (_cards.TryGetValue(_shownFocus, out var previous)) SetFocused(previous, false);
        _shownFocus = focus;
        if (!_cards.TryGetValue(focus, out var current)) return;
        SetFocused(current, true);
        if (rebuilt) _sections.UpdateLayout();
        ScrollTo(current);
    }

    /// <summary>
    /// Rola só o necessário para o cartão focado aparecer inteiro; na primeira linha de uma seção, o título dela também
    /// (na primeira seção, volta ao topo).
    /// </summary>
    private void ScrollTo(Card card)
    {
        var margin = Theme.SpaceM;
        var viewport = _scroll.ViewportHeight;
        if (viewport <= 0) return;
        var cardTop = card.Glow.TransformToVisual(_sections).TransformPoint(default).Y;
        var top = card.FirstRow ? card.Section.TransformToVisual(_sections).TransformPoint(default).Y : cardTop;
        var bottom = cardTop + card.Glow.ActualHeight;
        var offset = _scroll.VerticalOffset;
        double? target = null;
        if (card.FirstRow && ReferenceEquals(card.Section, _sections.Children.FirstOrDefault())) target = 0;
        else if (top - margin < offset) target = top - margin;
        else if (bottom + margin > offset + viewport) target = Math.Min(top - margin, bottom + margin - viewport);
        if (target is { } y) _scroll.ChangeView(null, Math.Max(0, y), null, disableAnimation: false);
    }

    private static void SetFocused(Card card, bool focused)
    {
        Theme.ApplyCardFocus(card.Ring, focused);
        card.Chevron.Foreground = focused ? Theme.Accent : Theme.TextMuted;
    }

    private void Build(IReadOnlyList<FileEntry> places, IReadOnlyList<HomeSection> sections, Dictionary<HomeSectionKind, int> columns)
    {
        foreach (var card in _cards.Values) _icons.Cancel(card.Icon);
        _cards.Clear();
        _sections.Children.Clear();
        _sections.Spacing = Theme.Space(40);
        foreach (var section in sections)
        {
            var block = new StackPanel { Spacing = Theme.Space(20) };
            block.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontSize = Theme.Font(28),
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text,
            });
            var count = columns[section.Kind];
            var grid = new Grid { ColumnSpacing = Gap, RowSpacing = Theme.Space(22) };
            for (var c = 0; c < count; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rows = (section.Places.Count + count - 1) / count;
            for (var r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < section.Places.Count; i++)
            {
                var index = section.Places[i];
                var card = BuildCard(places[index], index, section.Kind, i < count, block);
                Grid.SetRow(card.Glow, i / count);
                Grid.SetColumn(card.Glow, i % count);
                grid.Children.Add(card.Glow);
                _cards[index] = card;
            }
            block.Children.Add(grid);
            _sections.Children.Add(block);
        }
    }

    private Card BuildCard(FileEntry place, int index, HomeSectionKind kind, bool firstRow, FrameworkElement section)
    {
        var drive = kind == HomeSectionKind.Drives;
        var (primary, secondary) = _app.DescribePlace(place);
        var body = new Grid { ColumnSpacing = Theme.Space(36), Padding = new Thickness(Theme.Space(32), Theme.Space(20), Theme.Space(28), Theme.Space(20)) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var (iconCell, icon) = IconCell(place);
        body.Children.Add(iconCell);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = Theme.Space(drive ? 10 : 6) };
        Grid.SetColumn(text, 1);
        text.Children.Add(Line(place.Name, Theme.Font(24), place.IsBlocked ? Theme.Danger : Theme.Text));
        TextBlock? stats = null;
        if (drive && place.Volume is { TotalBytes: > 0 } volume) text.Children.Add(UsageBar(volume.UsedFraction));
        text.Children.Add(Line(primary, Theme.Font(19), place.IsBlocked ? Theme.Danger : Theme.TextMuted));
        if (secondary is not null)
        {
            var line = Line(secondary, Theme.Font(19), Theme.TextMuted);
            text.Children.Add(line);
            if (kind == HomeSectionKind.Folders) stats = line;
        }
        body.Children.Add(text);

        var chevron = new TextBlock
        {
            Text = ChevronGlyph,
            FontFamily = new FontFamily(IconFont),
            FontSize = Theme.Font(24),
            Foreground = Theme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(chevron, 2);
        body.Children.Add(chevron);

        var ring = new Border { CornerRadius = new CornerRadius(Theme.Radius.TopLeft + 2), Child = body, MinHeight = Theme.Scaled(drive ? 172 : 164) };
        var glow = Theme.CardWithGlow(ring);
        glow.CornerRadius = new CornerRadius(ring.CornerRadius.TopLeft + Theme.GlowRing.Left);
        Theme.ApplyCardFocus(ring, false);
        glow.Tapped += (_, _) => _app.PointerActivateListItem(index);
        var spoken = string.Join(", ", new[] { place.Name, primary, secondary }.Where(t => t is { Length: > 0 }));
        AutomationProperties.SetName(glow, spoken);
        return new Card(ring, glow, chevron, icon, stats, stats is null ? null : place.FullPath, firstRow, section);
    }

    private static TextBlock Line(string text, double size, Brush foreground) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = foreground,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        MaxLines = 1,
    };

    /// <summary>Barra de uso da unidade: trilho discreto e preenchimento azul → ciano na fração usada.</summary>
    private static Grid UsageBar(double used)
    {
        var height = Theme.Space(12);
        var bar = new Grid { Height = height, Margin = new Thickness(0, Theme.Space(2), Theme.Space(56), Theme.Space(2)) };
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

    /// <summary>Ícone do Windows (pasta conhecida, unidade, Lixeira) com símbolo de reserva até chegar.</summary>
    private (Grid Cell, Image Image) IconCell(FileEntry place)
    {
        var size = Theme.Scaled(IconSize);
        var cell = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var recent = place.Id == AppController.RecentPlaceId;
        var fallback = new TextBlock
        {
            Text = place.IsBlocked ? "" : recent ? "" : place.Kind == EntryKind.Drive ? "" : place.Id == RecycleBinLocation.PlaceId ? "" : "",
            FontFamily = new FontFamily(IconFont),
            FontSize = Math.Round(size * 0.62),
            Foreground = place.IsBlocked ? Theme.Danger : recent ? Theme.Accent : Theme.TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        cell.Children.Add(fallback);
        cell.Children.Add(image);
        _icons.Load(image, fallback, recent ? null : IconRequest.For(place));
        return (cell, image);
    }
}
