using ControlFS.App.Controls;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ControlFS.App.Views;

/// <summary>
/// Barra superior compartilhada: glifo do LB, caminho (raiz "Locais"/"Meu computador" e segmentos reais) e acesso
/// rápido com os ícones do Windows. Só desenha o estado do <see cref="AppController"/> (foco, itens, local atual); os
/// chips são refeitos apenas quando o conteúdo muda, então a troca de foco anima o fundo.
/// </summary>
internal sealed class TopBarView
{
    /// <summary>Tamanho do ícone dos atalhos, em pixels efetivos.</summary>
    public const double IconSize = 24;

    /// <summary>Shell: "Este Computador".</summary>
    private const string ThisPcParsingName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    private readonly AppController _app;
    private readonly IconLoader _icons;
    private readonly Grid _grid = new();
    private readonly Border _lb = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _quick = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _crumbScroll = Scroller();
    private readonly ScrollViewer _quickScroll = Scroller();
    private readonly Border _divider = new() { VerticalAlignment = VerticalAlignment.Stretch };
    private readonly List<Border> _crumbRings = [];
    private readonly List<Border> _quickRings = [];
    private string? _shownKey;
    private (PaneRegion Region, int Crumb, int Quick)? _shownFocus;

    public TopBarView(AppController app, IconLoader icons)
    {
        _app = app;
        _icons = icons;
        Root = new Border { Background = Theme.Surface, BorderBrush = Theme.Border, Child = _grid };
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _crumbScroll.Content = _crumbs;
        _quickScroll.Content = _quick;
        _divider.Background = Theme.Border;
        Grid.SetColumn(_crumbScroll, 1);
        Grid.SetColumn(_divider, 2);
        Grid.SetColumn(_quickScroll, 3);
        _grid.Children.Add(_lb);
        _grid.Children.Add(_crumbScroll);
        _grid.Children.Add(_divider);
        _grid.Children.Add(_quickScroll);
        AutomationProperties.SetName(Root, "Barra superior");
    }

    public Border Root { get; }

    private static ScrollViewer Scroller() => new()
    {
        HorizontalScrollMode = ScrollMode.Enabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollMode = ScrollMode.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsTabStop = false,
    };

    /// <summary>Medidas da faixa de layout atual; força refazer os chips.</summary>
    public void ApplyLayout()
    {
        Root.Margin = new Thickness(Theme.SpaceL, 0, Theme.SpaceL, Theme.SpaceS);
        Root.Padding = new Thickness(Theme.SpaceS, Theme.SpaceXs, Theme.SpaceS, Theme.SpaceXs);
        Root.CornerRadius = new CornerRadius(Theme.Radius.TopLeft + 4);
        Root.BorderThickness = Theme.Hairline;
        _grid.ColumnSpacing = Theme.SpaceS;
        _crumbs.Spacing = Theme.SpaceXs / 2;
        _quick.Spacing = Theme.SpaceXs;
        _divider.Width = Theme.Hairline.Left;
        _divider.Margin = new Thickness(Theme.SpaceXs, Theme.SpaceS, Theme.SpaceXs, Theme.SpaceS);
        _crumbScroll.MaxWidth = Math.Max(240, Theme.Viewport.Width * 0.42); // caminho longo não espreme o acesso rápido
        _shownKey = null;
    }

    public void Render()
    {
        var crumbs = _app.Breadcrumbs;
        var quick = _app.QuickAccess;
        var region = _app.TopModal is null ? _app.FocusRegion : PaneRegion.List;
        var crumbFocus = region == PaneRegion.Breadcrumbs ? _app.BreadcrumbFocus : -1;
        var quickFocus = region == PaneRegion.QuickAccess && quick.Count > 0 ? Math.Clamp(_app.QuickAccessFocus, 0, quick.Count - 1) : -1;
        var lb = _app.PromptProvider.For(InputAction.PreviousRegion, "Barra superior");
        var iconsOnly = !LabelsFit(crumbs, quick);
        var key = string.Join("|", crumbs.Select(c => $"{c.Kind}:{c.Label}:{c.IsCurrent}"))
            + "#" + string.Join("|", quick.Select(q => $"{q.Label}:{_app.IsQuickAccessActive(q)}"))
            + $"#{lb.Button}:{lb.Family}:{lb.Key}#{iconsOnly}" + (iconsOnly ? $"#{quickFocus}" : string.Empty);
        if (key != _shownKey)
        {
            _shownKey = key;
            _shownFocus = null;
            Build(crumbs, quick, lb, iconsOnly, quickFocus);
        }
        var focus = (region, crumbFocus, quickFocus);
        if (_shownFocus == focus) return;
        _shownFocus = focus;
        for (var i = 0; i < _crumbRings.Count; i++) SetCrumbFocus(_crumbRings[i], crumbs[i], i == crumbFocus);
        for (var i = 0; i < _quickRings.Count; i++) Theme.ApplyFocus(_quickRings[i], i == quickFocus);
        var target = crumbFocus >= 0 && crumbFocus < _crumbRings.Count ? _crumbRings[crumbFocus]
            : quickFocus >= 0 && quickFocus < _quickRings.Count ? _quickRings[quickFocus] : null;
        if (target is not null) target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
        else _crumbScroll.DispatcherQueue.TryEnqueue(() => _crumbScroll.ChangeView(_crumbScroll.ScrollableWidth, null, null, disableAnimation: true)); // pasta atual à vista
    }

    /// <summary>
    /// Os rótulos do acesso rápido cabem? Estimativa pelo número de caracteres (sem medir de novo a cada quadro). Não
    /// cabendo (portátil, janela estreita, caminho longo), os atalhos mostram só o ícone, menos o focado e o local atual.
    /// </summary>
    private bool LabelsFit(IReadOnlyList<Breadcrumb> crumbs, IReadOnlyList<QuickAccessItem> quick)
    {
        if (quick.Count == 0) return true;
        double Text(string s) => s.Length * FontSize * 0.56;
        var chrome = Theme.Scaled(IconSize) + (2 * Theme.SpaceS) + Theme.SpaceXs + (2 * (Theme.FocusRing.Left + Theme.GlowRing.Left)) + _quick.Spacing;
        var quickWidth = quick.Sum(q => Text(q.Label) + chrome);
        var crumbWidth = Math.Min(_crumbScroll.MaxWidth, crumbs.Sum(c => Math.Min(Text(c.Label), Theme.Scaled(220)) + chrome));
        var available = Theme.Viewport.Width - (2 * Theme.SpaceL) - (4 * Theme.SpaceS) - Theme.Scaled(56) - crumbWidth;
        return quickWidth <= available;
    }

    private static double FontSize => Theme.FontCaption + 1;

    private void Build(IReadOnlyList<Breadcrumb> crumbs, IReadOnlyList<QuickAccessItem> quick, Application.Prompts.ControllerPrompt lb, bool iconsOnly, int quickFocus)
    {
        _lb.Child = lb is { Button: { } button, Family: { } family }
            ? ControllerGlyphs.Create(button, family, Math.Round(Theme.FontBody * 1.5))
            : new Border
            {
                Background = Theme.SurfaceRaised,
                BorderBrush = Theme.Border,
                BorderThickness = Theme.Hairline,
                CornerRadius = Theme.Radius,
                Padding = new Thickness(Theme.SpaceS, Theme.SpaceXs / 2, Theme.SpaceS, Theme.SpaceXs / 2),
                Child = new TextBlock { Text = lb.Key, FontSize = Theme.FontCaption, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text },
            };
        AutomationProperties.SetName(_lb, lb.AccessibilityText);
        _lb.Margin = new Thickness(Theme.SpaceXs, 0, 0, 0);

        _crumbs.Children.Clear();
        _crumbRings.Clear();
        for (var i = 0; i < crumbs.Count; i++)
        {
            var crumb = crumbs[i];
            if (i > 0)
            {
                var boundary = crumb.Kind == BreadcrumbKind.Archive;
                _crumbs.Children.Add(new TextBlock
                {
                    Text = boundary ? "▸" : "", // compactado: ▸; pastas: chevron do Windows
                    FontFamily = boundary ? null : new FontFamily(IconFont),
                    FontSize = boundary ? Theme.FontItem : Theme.FontCaption,
                    Foreground = boundary ? Theme.Accent : Theme.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(Theme.SpaceXs / 2, 0, Theme.SpaceXs / 2, 0),
                });
            }
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS };
            if (crumb.Kind == BreadcrumbKind.Root)
                content.Children.Add(Icon(crumb.Label == "Meu computador" ? new IconRequest("thispc", IconSourceKind.Path, ThisPcParsingName) : null,
                    crumb.Label == "Meu computador" ? "" : "", Theme.Accent));
            else if (crumb.Kind == BreadcrumbKind.Archive)
                content.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = Theme.FontBody, Foreground = Theme.Accent, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock
            {
                Text = crumb.Label,
                FontSize = Theme.FontBody,
                FontWeight = crumb.IsCurrent || crumb.Kind == BreadcrumbKind.Root ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = crumb.IsCurrent || crumb.Kind == BreadcrumbKind.Root ? Theme.Text : crumb.Kind is BreadcrumbKind.Archive or BreadcrumbKind.ArchiveFolder ? Theme.Accent : Theme.TextMuted,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = Theme.Scaled(220),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var ring = Ring(content);
            var index = i;
            var glow = Theme.WithGlow(ring);
            glow.Tapped += (_, _) => _app.PointerActivateBreadcrumb(index);
            AutomationProperties.SetName(glow, crumb.Kind switch
            {
                BreadcrumbKind.Collapsed => $"{crumb.Hidden.Count} pastas recolhidas",
                BreadcrumbKind.Archive => $"{crumb.Label}, compactado",
                _ => crumb.Label,
            } + (crumb.IsCurrent ? ", pasta atual" : string.Empty));
            _crumbRings.Add(ring);
            _crumbs.Children.Add(glow);
        }

        _quick.Children.Clear();
        _quickRings.Clear();
        _divider.Visibility = _quickScroll.Visibility = quick.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < quick.Count; i++)
        {
            var item = quick[i];
            var active = _app.IsQuickAccessActive(item);
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS };
            content.Children.Add(QuickIcon(item, active));
            var label = new TextBlock
            {
                Text = item.Label,
                FontSize = FontSize,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = active ? Theme.Accent : Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = !iconsOnly || active || i == quickFocus ? Visibility.Visible : Visibility.Collapsed,
            };
            content.Children.Add(label);
            var ring = Ring(content);
            var index = i;
            var glow = Theme.WithGlow(ring);
            glow.Tapped += (_, _) => _app.PointerActivateQuickAccess(index);
            AutomationProperties.SetName(glow, item.Label + (active ? ", local atual" : string.Empty));
            ToolTipService.SetToolTip(glow, item.Kind == QuickAccessKind.Folder ? item.Path : item.Label);
            _quickRings.Add(ring);
            _quick.Children.Add(glow);
        }
    }

    private static Border Ring(UIElement content) => new()
    {
        Child = content,
        CornerRadius = Theme.Radius,
        Padding = new Thickness(Theme.SpaceS + Theme.SpaceXs, Theme.SpaceXs + 2, Theme.SpaceS + Theme.SpaceXs, Theme.SpaceXs + 2),
        BorderThickness = Theme.FocusRing,
        BorderBrush = Theme.Transparent,
    };

    /// <summary>Raiz em destaque discreto (como a referência: "Locais" num botão), o anel de foco por cima quando focada.</summary>
    private static void SetCrumbFocus(Border ring, Breadcrumb crumb, bool focused)
    {
        Theme.ApplyFocus(ring, focused);
        if (focused || crumb.Kind != BreadcrumbKind.Root) return;
        ring.Background = Theme.SurfaceRaised;
        ring.BorderBrush = Theme.Border;
    }

    /// <summary>Ícone do Windows (pasta conhecida, Este Computador, Lixeira) ou símbolo da fonte de ícones do Windows.</summary>
    private Grid QuickIcon(QuickAccessItem item, bool active)
    {
        var brush = active ? Theme.Accent : Theme.TextMuted;
        return item.Kind switch
        {
            QuickAccessKind.Favorites => Icon(null, "", Theme.Selected),
            QuickAccessKind.Recents => Icon(null, "", brush),
            QuickAccessKind.ThisPc => Icon(new IconRequest("thispc", IconSourceKind.Path, ThisPcParsingName), "", brush),
            QuickAccessKind.RecycleBin => Icon(item.Place is { } bin ? IconRequest.For(bin) : null, "", brush),
            _ => Icon(item.Place is { } place ? IconRequest.For(place) : null, "", brush),
        };
    }

    private Grid Icon(IconRequest? request, string glyph, Brush brush)
    {
        var size = Theme.Scaled(IconSize);
        var fallback = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(IconFont),
            FontSize = Math.Round(size * 0.8),
            Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        var cell = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(fallback);
        cell.Children.Add(image);
        _icons.Load(image, fallback, request);
        return cell;
    }
}
