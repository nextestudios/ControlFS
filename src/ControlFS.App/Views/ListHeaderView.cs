using ControlFS.App.Controls;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Core.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ControlFS.App.Views;

/// <summary>
/// Cabeçalho das colunas da lista (redesenho, fase C): caixa de marcação | Nome | Tipo | Tamanho | Modificado em, com a
/// seta da ordenação atual (↑ crescente, ↓ decrescente) no título da coluna ordenada. Usa as mesmas medidas das linhas
/// (<see cref="EntryRowTemplate.ColumnLayout"/>), então fica alinhado com elas. No controle a ordem muda pelo Menu
/// (Ordenar por / Ordem); com mouse ou toque, o título da coluna faz o mesmo, e a caixa marca todos ou limpa.
/// </summary>
internal sealed class ListHeaderView
{
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private const string ArrowUp = "";
    private const string ArrowDown = "";
    private const string Unchecked = "";
    private const string Checked = "";
    private const string Partial = "";

    private readonly AppController _app;
    private readonly Border _root = new();
    private readonly Grid _grid = new();
    private readonly Dictionary<SortField, (TextBlock Label, TextBlock Arrow, StackPanel Cell)> _titles = [];
    private TextBlock? _check;
    private TextBlock? _checkInner;
    private Grid? _checkCell;
    private string _shownKey = string.Empty;

    public ListHeaderView(AppController app)
    {
        _app = app;
        _root.Child = _grid;
    }

    public FrameworkElement Root => _root;

    /// <summary>Refaz as colunas quando as medidas mudam; atualiza seta e caixa a cada Render.</summary>
    public void Render(EntryRowTemplate.ListColumns columns, ListHeader header, Thickness listPadding)
    {
        if (columns.Key + listPadding != _shownKey)
        {
            _shownKey = columns.Key + listPadding;
            Build(columns, listPadding);
        }
        foreach (var (field, (label, arrow, cell)) in _titles)
        {
            var sorted = header.Sort is { } sort && sort.Field == field;
            arrow.Text = sorted ? (header.Sort!.Descending ? ArrowDown : ArrowUp) : string.Empty;
            label.Foreground = arrow.Foreground = sorted ? Theme.Text : Theme.TextMuted;
            label.FontWeight = sorted ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetName(cell, sorted
                ? $"{label.Text}, ordenado em ordem {(header.Sort!.Descending ? "decrescente" : "crescente")}"
                : header.Sort is null ? label.Text : $"Ordenar por {label.Text.ToLowerInvariant()}");
        }
        if (_check is not null && _checkInner is not null && _checkCell is not null)
        {
            // Caixa sempre desenhada; por cima, o visto (todos) ou o quadrado de "alguns" (nunca só a cor).
            _checkCell.Visibility = header.CanMark ? Visibility.Visible : Visibility.Collapsed;
            _checkInner.Text = header.Marks switch { MarkAllState.All => Checked, MarkAllState.Some => Partial, _ => string.Empty };
            _check.Foreground = _checkInner.Foreground = header.Marks == MarkAllState.None ? Theme.TextMuted : Theme.Selected;
            AutomationProperties.SetName(_checkCell, header.Marks == MarkAllState.All ? "Limpar marcação" : "Marcar todos");
        }
    }

    private void Build(EntryRowTemplate.ListColumns c, Thickness listPadding)
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _titles.Clear();
        _check = _checkInner = null;
        _checkCell = null;
        var (mark, icon, name, type, size, date, chevron, _) = EntryRowTemplate.ColumnLayout(c);
        var widths = new double?[chevron + 1];
        if (mark >= 0) widths[mark] = c.MarkWidth;
        widths[icon] = c.IconWidth;
        if (type >= 0) widths[type] = c.TypeWidth;
        widths[size] = c.SizeWidth;
        widths[date] = c.DateWidth;
        widths[chevron] = c.ChevronWidth;
        foreach (var width in widths)
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width is { } w ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });
        _grid.ColumnSpacing = c.Spacing;
        var inset = EntryRowTemplate.ListColumns.Inset;
        _grid.MinHeight = Theme.Scaled(c.Compact ? 42 : 62);
        _grid.Padding = new Thickness(listPadding.Left + inset + c.Padding.Left, 0, listPadding.Right + inset + c.Padding.Right, 0);
        _root.BorderBrush = Theme.Border;
        _root.BorderThickness = new Thickness(0, 0, 0, Theme.Hairline.Top);
        _root.Margin = new Thickness(0, 0, 0, Theme.Space(c.Compact ? 2 : 4));

        var fontSize = Theme.Font(c.Compact ? 15 : 19);
        if (mark >= 0)
        {
            TextBlock Glyph(string text) => new()
            {
                Text = text,
                FontFamily = new FontFamily(IconFont),
                FontSize = Theme.Font(c.Compact ? 18 : 26),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            _check = Glyph(Unchecked);
            _checkInner = Glyph(string.Empty);
            _checkCell = new Grid { Background = Theme.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            _checkCell.Children.Add(_check);
            _checkCell.Children.Add(_checkInner);
            _checkCell.Tapped += (_, _) => _app.PointerToggleMarkAll();
            Grid.SetColumn(_checkCell, mark);
            _grid.Children.Add(_checkCell);
        }
        Title(SortField.Name, "Nome", name, fontSize);
        if (type >= 0) Title(SortField.Type, "Tipo", type, fontSize);
        Title(SortField.Size, "Tamanho", size, fontSize);
        Title(SortField.Modified, "Modificado em", date, fontSize);
    }

    private void Title(SortField field, string text, int column, double fontSize)
    {
        var label = new TextBlock { Text = text, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var arrow = new TextBlock { FontFamily = new FontFamily(IconFont), FontSize = Math.Round(fontSize * 0.75), VerticalAlignment = VerticalAlignment.Center };
        var cell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Theme.Space(8),
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Theme.Transparent, // a célula inteira recebe o toque
        };
        cell.Children.Add(label);
        cell.Children.Add(arrow);
        cell.Tapped += (_, _) => _app.PointerSortBy(field);
        Controls.Hover.AttachDim(cell);
        Grid.SetColumn(cell, column);
        _grid.Children.Add(cell);
        _titles[field] = (label, arrow, cell);
    }
}
