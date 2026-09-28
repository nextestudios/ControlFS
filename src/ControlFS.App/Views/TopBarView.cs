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
/// Barra superior compartilhada: glifo do L1 (alvo anterior), caminho (raiz "Locais"/"Meu computador" e segmentos
/// reais), acesso rápido com os ícones do Windows e glifo do R1 (alvo seguinte), com a família do controle em uso. Só desenha o estado do <see cref="AppController"/> (foco, itens, local atual); os
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
    private readonly Border _rb = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _quick = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _crumbScroll = Scroller();
    private readonly ScrollViewer _quickScroll = Scroller();
    private readonly Border _divider = new() { VerticalAlignment = VerticalAlignment.Stretch };
    private readonly List<Border> _crumbRings = [];
    private readonly List<Border> _crumbGlows = [];
    private readonly List<TextBlock> _crumbSeparators = [];
    private readonly List<TextBlock> _crumbLabels = [];
    private readonly TextBlock _overflow = new() { Text = "…", VerticalAlignment = VerticalAlignment.Center };
    private double[] _crumbWidths = [];
    private double[] _labelWidths = [];
    private double _pathMax = double.PositiveInfinity;
    private readonly List<Border> _quickRings = [];
    private readonly List<TextBlock> _quickLabels = [];
    private readonly List<bool> _quickActive = [];
    private bool _iconsOnly;
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
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _crumbScroll.Content = _crumbs;
        _quickScroll.Content = _quick;
        _divider.Background = Theme.Border;
        Grid.SetColumn(_crumbScroll, 1);
        Grid.SetColumn(_divider, 2);
        Grid.SetColumn(_quickScroll, 3);
        Grid.SetColumn(_rb, 4);
        _grid.Children.Add(_lb);
        _grid.Children.Add(_crumbScroll);
        _grid.Children.Add(_divider);
        _grid.Children.Add(_quickScroll);
        _grid.Children.Add(_rb);
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
        // Mais alta fora dos portáteis (a referência tem ~70 px a 1080p); em 720p/800p continua enxuta.
        var vertical = Theme.Layout.Tier == Core.Layout.LayoutTier.Compact ? Theme.SpaceXs : Theme.Space(10);
        Root.Padding = new Thickness(Theme.SpaceS, vertical, Theme.SpaceS, vertical);
        Root.CornerRadius = new CornerRadius(Theme.Radius.TopLeft + 4);
        Root.BorderThickness = Theme.Hairline;
        _grid.ColumnSpacing = Theme.SpaceS;
        _crumbs.Spacing = Theme.SpaceXs / 2;
        _quick.Spacing = Theme.SpaceXs;
        _divider.Width = Theme.Hairline.Left;
        _divider.Margin = new Thickness(Theme.SpaceXs, Theme.SpaceS, Theme.SpaceXs, Theme.SpaceS);
        _shownKey = null;
    }

    /// <summary>A janela mudou de tamanho dentro da mesma faixa: redistribui a largura no próximo Render.</summary>
    public void Invalidate() => _shownKey = null;

    public void Render()
    {
        var crumbs = _app.Breadcrumbs;
        var quick = _app.QuickAccess;
        var region = _app.TopModal is null ? _app.FocusRegion : PaneRegion.List;
        var crumbFocus = region == PaneRegion.Breadcrumbs ? _app.BreadcrumbFocus : -1;
        var quickFocus = region == PaneRegion.QuickAccess && quick.Count > 0 ? Math.Clamp(_app.QuickAccessFocus, 0, quick.Count - 1) : -1;
        var lb = _app.PromptProvider.For(InputAction.PreviousRegion, "Barra superior, alvo anterior");
        var rb = _app.PromptProvider.For(InputAction.NextRegion, "Barra superior, próximo alvo");
        var key = string.Join("|", crumbs.Select(c => $"{c.Kind}:{c.Label}:{c.IsCurrent}"))
            + "#" + string.Join("|", quick.Select(q => $"{q.Label}:{_app.IsQuickAccessActive(q)}"))
            + $"#{lb.Button}:{lb.Family}:{lb.Key}#{rb.Button}:{rb.Family}:{rb.Key}";
        if (key != _shownKey)
        {
            _shownKey = key;
            _shownFocus = null;
            Build(crumbs, quick, lb, rb);
            Fit();
        }
        var focus = (region, crumbFocus, quickFocus);
        if (_shownFocus == focus) return;
        _shownFocus = focus;
        for (var i = 0; i < _crumbRings.Count; i++) SetCrumbFocus(_crumbRings[i], crumbs[i], i == crumbFocus);
        LayoutCrumbs(crumbFocus);
        for (var i = 0; i < _quickRings.Count; i++)
        {
            Theme.ApplyFocus(_quickRings[i], i == quickFocus);
            _quickLabels[i].Visibility = !_iconsOnly || _quickActive[i] || i == quickFocus ? Visibility.Visible : Visibility.Collapsed;
        }
        var target = crumbFocus >= 0 && crumbFocus < _crumbRings.Count ? _crumbRings[crumbFocus]
            : quickFocus >= 0 && quickFocus < _quickRings.Count ? _quickRings[quickFocus] : null;
        if (target is not null) target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
        else _crumbScroll.DispatcherQueue.TryEnqueue(() => _crumbScroll.ChangeView(_crumbScroll.ScrollableWidth, null, null, disableAnimation: true)); // pasta atual à vista
    }

    /// <summary>
    /// Divide a largura da barra medindo os chips uma vez por conteúdo novo. O caminho vem primeiro: se caminho e atalhos
    /// com nome não cabem juntos, os atalhos mostram só o ícone (menos o focado e o do local atual) e o caminho fica com o
    /// resto; só um caminho maior que isso rola, mostrando a pasta atual.
    /// </summary>
    private void Fit()
    {
        var infinite = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
        _lb.Measure(infinite);
        _crumbs.Measure(infinite);
        _quick.Measure(infinite);
        var bar = Theme.Viewport.Width - Root.Margin.Left - Root.Margin.Right - Root.Padding.Left - Root.Padding.Right - (2 * Root.BorderThickness.Left)
            - _lb.DesiredSize.Width - _lb.Margin.Left - (Theme.Hairline.Left + _divider.Margin.Left + _divider.Margin.Right) - (3 * _grid.ColumnSpacing) - Theme.SpaceXs;
        var path = _crumbs.DesiredSize.Width;
        _crumbWidths = [.. _crumbGlows.Select((g, i) => g.DesiredSize.Width + _crumbs.Spacing
            + (i > 0 ? _crumbSeparators[i - 1].DesiredSize.Width + _crumbSeparators[i - 1].Margin.Left + _crumbSeparators[i - 1].Margin.Right + _crumbs.Spacing : 0))];
        _labelWidths = [.. _crumbLabels.Select(l => l.DesiredSize.Width)];
        var labeled = _quick.DesiredSize.Width;
        _iconsOnly = _quickLabels.Count > 0 && path + labeled > bar;
        var widestLabel = 0.0;
        if (_iconsOnly)
        {
            for (var i = 0; i < _quickLabels.Count; i++)
            {
                widestLabel = Math.Max(widestLabel, _quickLabels[i].DesiredSize.Width + Theme.SpaceS);
                if (!_quickActive[i]) _quickLabels[i].Visibility = Visibility.Collapsed;
            }
            _quick.Measure(infinite);
        }
        // O focado mostra o nome; o glifo do R1 usa essa mesma folga (sem ela, o caminho perderia um segmento).
        var quickWidth = _quickLabels.Count == 0 ? 0 : _quick.DesiredSize.Width + widestLabel;
        // A raiz, o "…" e um pedaço da pasta atual sempre cabem (auditoria de UX, P2-7): com pouco espaço, quem cede são
        // os atalhos (que rolam até o focado), nunca a raiz do caminho.
        var rootAndCurrent = _crumbWidths.Length > 1 ? _crumbWidths[0] + (Theme.FontBody * 1.2) + _crumbs.Spacing + Theme.Scaled(150) : 0;
        var max = Math.Max(Math.Max(Theme.Scaled(240), rootAndCurrent), bar - quickWidth);
        _pathMax = double.IsFinite(max) ? max : double.PositiveInfinity; // antes da primeira faixa de layout há medidas NaN
        _crumbScroll.MaxWidth = _pathMax;
    }

    /// <summary>
    /// Caminho maior que o espaço: some o começo (depois da raiz) atrás de um "…", nunca o segmento focado nem, com o foco
    /// fora do caminho, a pasta atual. Só visibilidade: os índices do caminho continuam os do AppController.
    /// </summary>
    private void LayoutCrumbs(int focus)
    {
        var count = _crumbGlows.Count;
        if (count == 0 || _crumbWidths.Length != count) return;
        var total = _crumbWidths.Sum();
        int start = 1, end = count - 1;
        if (total > _pathMax && count > 2)
        {
            var budget = _pathMax - _crumbWidths[0] - (Theme.FontBody * 1.2) - _crumbs.Spacing;
            var anchor = focus >= 1 ? focus : end;
            // Janela contígua que termina na pasta atual; se o foco ficou à esquerda dela, a janela começa no foco.
            start = end;
            var used = _crumbWidths[end];
            while (start > 1 && used + _crumbWidths[start - 1] <= budget) used += _crumbWidths[--start];
            if (anchor < start)
            {
                start = anchor;
                end = anchor;
                used = _crumbWidths[anchor];
                while (end < count - 1 && used + _crumbWidths[end + 1] <= budget) used += _crumbWidths[++end];
            }
        }
        _overflow.Visibility = start > 1 ? Visibility.Visible : Visibility.Collapsed;
        // A raiz nunca sai de vista (auditoria de UX, P2-7): se nem raiz + "…" + a pasta mostrada cabem (portátil com
        // texto grande), o nome dessa pasta é cortado no fim em vez de a barra rolar e esconder a raiz.
        if (_crumbLabels.Count == count && _labelWidths.Length == count)
        {
            foreach (var label in _crumbLabels) label.MaxWidth = CrumbLabelMax;
            var shown = _crumbWidths[0] + (start > 1 ? (Theme.FontBody * 1.2) + _crumbs.Spacing : 0);
            for (var i = start; i <= end; i++) shown += _crumbWidths[i];
            if (count > 1 && shown > _pathMax && double.IsFinite(_pathMax))
                _crumbLabels[end].MaxWidth = Math.Max(Theme.Scaled(60), _labelWidths[end] - (shown - _pathMax));
        }
        for (var i = 1; i < count; i++)
        {
            var visible = i >= start && i <= end ? Visibility.Visible : Visibility.Collapsed;
            _crumbGlows[i].Visibility = visible;
            _crumbSeparators[i - 1].Visibility = visible;
        }
    }

    private static double FontSize => Theme.FontCaption + 1;

    /// <summary>Largura máxima do nome de um segmento do caminho.</summary>
    private static double CrumbLabelMax => Theme.Scaled(220);

    /// <summary>Glifo do ombro (LB/RB, L1/R1, L/R) da família em uso; sem controle, a tecla (Ctrl+←/Ctrl+→).</summary>
    private static UIElement Shoulder(Application.Prompts.ControllerPrompt prompt) => prompt is { Button: { } button, Family: { } family }
        ? ControllerGlyphs.Create(button, family, Math.Round(Theme.FontBody * 1.5))
        : new Border
        {
            Background = Theme.SurfaceRaised,
            BorderBrush = Theme.Border,
            BorderThickness = Theme.Hairline,
            CornerRadius = Theme.Radius,
            Padding = new Thickness(Theme.SpaceS, Theme.SpaceXs / 2, Theme.SpaceS, Theme.SpaceXs / 2),
            Child = new TextBlock { Text = prompt.Key, FontSize = Theme.FontCaption, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text },
        };

    private void Build(IReadOnlyList<Breadcrumb> crumbs, IReadOnlyList<QuickAccessItem> quick, Application.Prompts.ControllerPrompt lb, Application.Prompts.ControllerPrompt rb)
    {
        _lb.Child = Shoulder(lb);
        AutomationProperties.SetName(_lb, lb.AccessibilityText);
        _lb.Margin = new Thickness(Theme.SpaceXs, 0, 0, 0);
        _rb.Child = Shoulder(rb);
        AutomationProperties.SetName(_rb, rb.AccessibilityText);
        _rb.Margin = new Thickness(0, 0, Theme.SpaceXs, 0);

        _crumbs.Children.Clear();
        _crumbRings.Clear();
        _crumbGlows.Clear();
        _crumbSeparators.Clear();
        _crumbLabels.Clear();
        _overflow.FontSize = Theme.FontBody;
        _overflow.Foreground = Theme.TextMuted;
        _overflow.Margin = new Thickness(Theme.SpaceXs, 0, 0, 0);
        _overflow.Visibility = Visibility.Collapsed;
        for (var i = 0; i < crumbs.Count; i++)
        {
            var crumb = crumbs[i];
            if (i > 0)
            {
                var boundary = crumb.Kind == BreadcrumbKind.Archive;
                var separator = new TextBlock
                {
                    Text = boundary ? "▸" : "\uE76C", // compactado: ▸; pastas: chevron do Windows
                    FontSize = boundary ? Theme.FontItem : Theme.FontCaption,
                    Foreground = boundary ? Theme.Accent : Theme.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(Theme.SpaceXs / 2, 0, Theme.SpaceXs / 2, 0),
                };
                if (!boundary) separator.FontFamily = new FontFamily(IconFont);
                _crumbSeparators.Add(separator);
                _crumbs.Children.Add(separator);
            }
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS };
            if (crumb.Kind == BreadcrumbKind.Root)
                content.Children.Add(Icon(crumb.Label == "Meu computador" ? new IconRequest("thispc", IconSourceKind.Path, ThisPcParsingName) : null,
                    crumb.Label == "Meu computador" ? "\uE977" : "\uE80F", Theme.Accent));
            else if (crumb.Kind == BreadcrumbKind.Archive)
                content.Children.Add(new TextBlock { Text = "\uE7B8", FontFamily = new FontFamily(IconFont), FontSize = Theme.FontBody, Foreground = Theme.Accent, VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock
            {
                Text = crumb.Label,
                FontSize = Theme.FontBody,
                FontWeight = crumb.IsCurrent || crumb.Kind == BreadcrumbKind.Root ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = crumb.IsCurrent || crumb.Kind == BreadcrumbKind.Root ? Theme.Text : crumb.Kind is BreadcrumbKind.Archive or BreadcrumbKind.ArchiveFolder ? Theme.Accent : Theme.TextMuted,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = CrumbLabelMax,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _crumbLabels.Add(label);
            content.Children.Add(label);
            var ring = Ring(content);
            var index = i;
            var glow = Theme.WithGlow(ring);
            if (!crumb.IsCurrent) glow.Tapped += (_, _) => _app.PointerActivateBreadcrumb(index); // a pasta atual é só o rótulo
            AutomationProperties.SetName(glow, crumb.Kind switch
            {
                BreadcrumbKind.Collapsed => $"{crumb.Hidden.Count} pastas recolhidas",
                BreadcrumbKind.Archive => $"{crumb.Label}, compactado",
                _ => crumb.Label,
            } + (crumb.IsCurrent ? ", pasta atual" : string.Empty));
            _crumbRings.Add(ring);
            _crumbGlows.Add(glow);
            _crumbs.Children.Add(glow);
            if (i == 0) _crumbs.Children.Add(_overflow); // "…" quando o começo do caminho não cabe
        }

        _quick.Children.Clear();
        _quickRings.Clear();
        _quickLabels.Clear();
        _quickActive.Clear();
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
            };
            content.Children.Add(label);
            var ring = Ring(content);
            var index = i;
            var glow = Theme.WithGlow(ring);
            glow.Tapped += (_, _) => _app.PointerActivateQuickAccess(index);
            AutomationProperties.SetName(glow, item.Label + (active ? ", local atual" : string.Empty));
            ToolTipService.SetToolTip(glow, item.Kind == QuickAccessKind.Folder ? item.Path : item.Label);
            _quickRings.Add(ring);
            _quickLabels.Add(label);
            _quickActive.Add(active);
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
            QuickAccessKind.Favorites => Icon(null, "\uE734", Theme.Selected),
            QuickAccessKind.Recents => Icon(null, "\uE81C", brush),
            QuickAccessKind.ThisPc => Icon(new IconRequest("thispc", IconSourceKind.Path, ThisPcParsingName), "\uE977", brush),
            QuickAccessKind.RecycleBin => Icon(item.Place is { } bin ? IconRequest.For(bin) : null, "\uE74D", brush),
            _ => Icon(item.Place is { } place ? IconRequest.For(place) : null, "\uE8B7", brush),
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
