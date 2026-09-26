using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ControlFS.App.Views;

/// <summary>Constrói a camada modal a partir do estado do AppController (escopo exclusivo de entrada).</summary>
public static class ModalView
{
    public static UIElement? Build(AppController app)
    {
        if (app.TopModal is not { } modal) return null;
        var panel = modal switch
        {
            MenuModal menu => BuildMenu(app, menu),
            DialogModal dialog => BuildDialog(app, dialog),
            KeyboardModal keyboard => BuildKeyboard(app, keyboard),
            _ => null,
        };
        if (panel is null) return null;
        var scrim = new Grid { Background = Theme.Scrim };
        scrim.Children.Add(panel);
        return scrim;
    }

    private static Border Card(UIElement content, double maxWidth) => new()
    {
        Background = Theme.Surface,
        BorderBrush = Theme.Border,
        BorderThickness = Theme.Hairline,
        CornerRadius = Theme.Radius,
        Padding = new Thickness(Theme.SpaceL),
        MaxWidth = maxWidth,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(Theme.SpaceM),
        Child = content,
    };

    private static TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = Theme.FontTitle,
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.Text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, Theme.SpaceM),
    };

    private static Border Choice(string text, bool focused, bool enabled, Action onTap, string? secondary = null, bool danger = false)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = (danger ? "⚠ " : string.Empty) + text,
            FontSize = Theme.FontItem,
            Foreground = !enabled ? Theme.TextDisabled : danger ? Theme.Danger : Theme.Text,
            TextWrapping = TextWrapping.Wrap,
        });
        if (secondary is not null)
            stack.Children.Add(new TextBlock { Text = secondary, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        var border = new Border
        {
            Child = stack,
            Padding = new Thickness(Theme.SpaceM, Theme.SpaceS, Theme.SpaceM, Theme.SpaceS),
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = Theme.Radius,
            Background = focused ? Theme.AccentSoft : Theme.Transparent,
            BorderBrush = focused ? Theme.Accent : Theme.Transparent,
            BorderThickness = Theme.FocusRing,
        };
        AutomationProperties.SetName(border, text + (enabled ? string.Empty : ", indisponível"));
        border.Tapped += (_, _) => onTap();
        return border;
    }

    private static Border BuildMenu(AppController app, MenuModal menu)
    {
        var stack = new StackPanel();
        stack.Children.Add(Title(menu.Title));
        for (var i = 0; i < menu.Items.Count; i++)
        {
            var item = menu.Items[i];
            var index = i;
            var focused = i == menu.FocusIndex;
            var secondary = !item.IsEnabled && focused ? "Indisponível: " + item.DisabledReason : item.Detail;
            stack.Children.Add(Choice(item.Label, focused, item.IsEnabled, () => app.PointerChooseModalOption(index), secondary));
        }
        return Card(new ScrollViewer { Content = stack, MaxHeight = 640 }, 560);
    }

    private static Border BuildDialog(AppController app, DialogModal dialog)
    {
        var stack = new StackPanel();
        stack.Children.Add(Title(dialog.Title));
        var grid = new Grid { ColumnSpacing = Theme.SpaceM, RowSpacing = Theme.SpaceXs, Margin = new Thickness(0, 0, 0, Theme.SpaceM) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < dialog.Lines.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = dialog.Lines[i].Label, FontSize = Theme.FontBody, Foreground = Theme.TextMuted };
            var value = new TextBlock { Text = dialog.Lines[i].Value, FontSize = Theme.FontBody, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        stack.Children.Add(grid);
        if (dialog.Message is { } message)
            stack.Children.Add(new TextBlock { Text = message, FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, Theme.SpaceM) });
        for (var i = 0; i < dialog.Options.Count; i++)
        {
            var option = dialog.Options[i];
            var index = i;
            var label = option.Kind == DialogOptionKind.Toggle ? (option.IsChecked ? "☑ " : "☐ ") + option.Label : option.Label;
            stack.Children.Add(Choice(label, i == dialog.FocusIndex, true, () => app.PointerChooseModalOption(index), danger: option.Kind == DialogOptionKind.Danger));
        }
        return Card(new ScrollViewer { Content = stack, MaxHeight = 720 }, 760);
    }

    private static Border BuildKeyboard(AppController app, KeyboardModal modal)
    {
        var kb = modal.Keyboard;
        var stack = new StackPanel();
        stack.Children.Add(Title(kb.Title));

        var display = kb.DisplayText;
        var caret = Math.Min(kb.Caret, display.Length);
        var field = new Border
        {
            Background = Theme.SurfaceRaised,
            BorderBrush = Theme.Accent,
            BorderThickness = Theme.Hairline,
            CornerRadius = Theme.Radius,
            Padding = new Thickness(Theme.SpaceM),
            Child = new TextBlock
            {
                Text = display[..caret] + "│" + display[caret..],
                FontSize = Theme.FontItem,
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        AutomationProperties.SetName(field, kb.Kind == TextFieldKind.Password ? $"Senha, {kb.Length} caracteres" : $"Texto: {kb.Text}");
        stack.Children.Add(field);

        var status = $"{(kb.Language == KeyboardLanguage.PortugueseBrazil ? "PT-BR" : "EN")} · {kb.Page switch { KeyboardPage.Symbols => "símbolos", KeyboardPage.Accents => "acentos", _ => "letras" }}" +
            (kb.Shift == ShiftState.Locked ? " · MAIÚSCULAS" : kb.Shift == ShiftState.Once ? " · próxima maiúscula" : string.Empty) +
            (modal.IsBusy ? " · aplicando…" : string.Empty);
        stack.Children.Add(new TextBlock { Text = status, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, Margin = new Thickness(0, Theme.SpaceS, 0, 0) });
        if (kb.ErrorMessage is { } error)
            stack.Children.Add(new TextBlock { Text = "⚠ " + error, FontSize = Theme.FontBody, Foreground = Theme.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Theme.SpaceS, 0, 0) });

        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6, Margin = new Thickness(0, Theme.SpaceM, 0, 0) };
        for (var c = 0; c < VirtualKeyboardLayouts.Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < kb.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            var column = 0;
            for (var k = 0; k < kb.Rows[r].Count; k++)
            {
                var key = kb.Rows[r][k];
                var focused = r == kb.Row && k == kb.Column;
                var enabled = kb.IsKeyEnabled(key);
                var row = r;
                var keyIndex = k;
                var cell = new Border
                {
                    Background = focused ? Theme.AccentSoft : Theme.SurfaceRaised,
                    BorderBrush = focused ? Theme.Accent : Theme.Border,
                    BorderThickness = focused ? Theme.FocusRing : Theme.Hairline,
                    CornerRadius = Theme.Radius,
                    Child = new TextBlock
                    {
                        Text = kb.DisplayLabel(key),
                        FontSize = Theme.FontItem,
                        Foreground = enabled ? Theme.Text : Theme.TextDisabled,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                AutomationProperties.SetName(cell, key.Name + (enabled ? string.Empty : ", indisponível neste campo"));
                cell.Tapped += (_, _) => app.PointerPressKey(row, keyIndex);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, column);
                Grid.SetColumnSpan(cell, key.Span);
                grid.Children.Add(cell);
                column += key.Span;
            }
        }
        stack.Children.Add(grid);
        return Card(stack, 880);
    }
}
