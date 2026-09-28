using ControlFS.App.Controls;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ControlFS.App.Views;

/// <summary>
/// O painel sem o foco nos dois painéis (#56): o local dele no título e a lista (sem anel de foco, com as marcações), um
/// pouco esmaecida para que o painel ativo — contornado em ciano, com o título "ativo" — seja inconfundível. Só desenha:
/// a entrada vai sempre para o painel ativo; clicar aqui torna este painel o ativo (a mesma troca do L3).
/// </summary>
internal sealed class PaneView
{
    private readonly AppController _app;
    private readonly IconLoader _icons;
    private readonly Border _root = new();
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock _path = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ListView _list = new();
    private IReadOnlyList<FileEntry>? _shownItems;
    private HashSet<string> _shownSelection = [];
    private object? _shownTemplate;
    private PaneState? _pane;
    private Func<EntryRowTemplate.RowContext>? _rowContext;
    private IReadOnlySet<string>? _specialFolders;

    public PaneView(AppController app, IconLoader icons)
    {
        _app = app;
        _icons = icons;
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var caption = new StackPanel();
        caption.Children.Add(_title);
        caption.Children.Add(_path);
        body.Children.Add(caption);
        Grid.SetRow(_list, 1);
        body.Children.Add(_list);
        Grid.SetRow(_empty, 1);
        body.Children.Add(_empty);
        _root.Child = body;
        _root.Visibility = Visibility.Collapsed;
        _root.Opacity = 0.72;
        _root.Tapped += (_, _) => _app.PointerFocusPane(second: ReferenceEquals(_pane, _app.SecondPane));

        _list.SelectionMode = ListViewSelectionMode.None;
        _list.IsItemClickEnabled = false;
        _list.IsTabStop = false;
        _list.AllowFocusOnInteraction = false;
        _list.ItemContainerTransitions = new TransitionCollection();
        _list.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue)
            {
                EntryRowTemplate.Recycle(args.ItemContainer, _icons);
                return;
            }
            if (args.Item is not FileEntry entry) return;
            args.ItemContainer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            args.ItemContainer.VerticalContentAlignment = VerticalAlignment.Stretch;
            args.ItemContainer.Margin = args.ItemContainer.Padding = new Thickness(0);
            args.ItemContainer.MinHeight = 0;
            args.ItemContainer.UseSystemFocusVisuals = false;
            EntryRowTemplate.Fill(args.ItemContainer, entry, focused: false, _shownSelection.Contains(entry.Id), _app.IsCut(entry), _icons, _specialFolders, _rowContext?.Invoke());
        };
    }

    public FrameworkElement Root => _root;

    public void ApplyLayout()
    {
        _root.Background = Theme.SurfaceRaised;
        _root.BorderBrush = Theme.Border;
        _root.BorderThickness = Theme.Hairline;
        _root.CornerRadius = new CornerRadius(Theme.Scaled(14));
        _root.Padding = new Thickness(0, Theme.Space(10), 0, Theme.Space(10));
        _title.Margin = _path.Margin = new Thickness(Theme.Space(18), 0, Theme.Space(18), 0);
        _path.Margin = new Thickness(Theme.Space(18), 0, Theme.Space(18), Theme.Space(8));
        _title.FontSize = Theme.FontCaption;
        _path.FontSize = Theme.FontCaption;
        _empty.FontSize = Theme.FontBody;
        _title.Foreground = _path.Foreground = _empty.Foreground = Theme.TextMuted;
        _list.Padding = new Thickness(Theme.Space(10), 0, Theme.Space(10), 0);
        _shownItems = null; // o modelo muda com o layout: recria as linhas
    }

    /// <summary>Mostra <paramref name="pane"/> (o painel sem foco) com o modelo de linha da lista principal.</summary>
    public void Render(PaneState pane, DataTemplate template, Func<EntryRowTemplate.RowContext> rowContext, IReadOnlySet<string> specialFolders)
    {
        _pane = pane;
        _rowContext = rowContext;
        _specialFolders = specialFolders;
        var name = _app.PaneName(pane);
        _title.Text = $"{name.ToUpperInvariant()} · L3 ou Tab para ativar";
        _path.Text = pane.Location?.DisplayPath ?? "Início";
        AutomationProperties.SetName(_root, $"{name} (inativo): {_path.Text}");
        var items = pane.IsLoading || pane.Location is null ? [] : pane.List.Items;
        var selection = pane.List.SelectedIds.ToHashSet();
        if (!ReferenceEquals(template, _shownTemplate))
        {
            _shownTemplate = template;
            _list.ItemTemplate = template;
            _shownItems = null;
        }
        if (!ReferenceEquals(items, _shownItems) || !selection.SetEquals(_shownSelection))
        {
            _shownItems = items;
            _shownSelection = selection;
            _list.ItemsSource = items.ToList();
            if (pane.List.FocusIndex is var focus and >= 0 && focus < items.Count) _list.ScrollIntoView(items[focus]);
        }
        _empty.Text = _app.EmptyMessage(pane);
    }

    public void Hide()
    {
        if (_root.Visibility == Visibility.Collapsed) return;
        _root.Visibility = Visibility.Collapsed;
        _list.ItemsSource = null;
        _shownItems = null;
    }

    public void Show() => _root.Visibility = Visibility.Visible;

    /// <summary>Ícones mudaram de tamanho: recria as linhas no próximo Render.</summary>
    public void Invalidate() => _shownItems = null;
}
