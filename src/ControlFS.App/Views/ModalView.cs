using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Input.Mapping;
using ControlFS.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
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
            AboutModal about => BuildAbout(about),
            MappingWizardModal wizard => BuildMappingWizard(app, wizard),
            ControllerTestModal test => BuildControllerTest(app, test),
            _ => null,
        };
        if (panel is null) return null;
        var scrim = new Grid { Background = Theme.Scrim };
        scrim.Children.Add(panel);
        return scrim;
    }

    /// <summary>Largura máxima em pixels efetivos de 1080p, escalada pela faixa de layout (TV grande = cartão maior).</summary>
    private static Border Card(UIElement content, double maxWidth) => new()
    {
        Background = Theme.Surface,
        BorderBrush = Theme.Border,
        BorderThickness = Theme.Hairline,
        CornerRadius = Theme.Radius,
        Padding = new Thickness(Theme.SpaceL),
        MaxWidth = Theme.Scaled(maxWidth),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(Theme.SpaceM),
        Child = content,
    };

    /// <summary>
    /// Conteúdo rolável que nunca passa da altura da janela (menus longos em 1280×720): o cartão inteiro, com margens,
    /// cabe na tela, e o item focado é trazido para a vista (<see cref="KeepInView"/>).
    /// </summary>
    private static ScrollViewer Scroll(UIElement content)
    {
        var chrome = 2 * (Theme.SpaceM + Theme.SpaceL + Theme.Hairline.Top);
        return new ScrollViewer { Content = content, MaxHeight = Math.Max(160, Theme.Viewport.Height - chrome), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>
    /// O foco é lógico (AppController), não do XAML: a cada quadro o modal é refeito, então o elemento focado pede
    /// para ser mostrado, centralizado, assim que entra na árvore. Sem isso itens abaixo da dobra ficavam invisíveis.
    /// </summary>
    private static void KeepInView(FrameworkElement element) =>
        element.Loaded += (_, _) => element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.5 });

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
        };
        Theme.ApplyFocus(border, focused);
        if (focused) KeepInView(border);
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
        return Card(Scroll(stack), 560);
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
        return Card(Scroll(stack), 760);
    }

    private static Border BuildAbout(AboutModal about)
    {
        var stack = new StackPanel { Spacing = Theme.SpaceS };
        if (Branding.Logo is { } logo)
        {
            var image = new Image { Source = logo, Height = Theme.Scaled(72), HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, Theme.SpaceM) };
            AutomationProperties.SetName(image, "ControlFS");
            stack.Children.Add(image);
        }
        else
        {
            stack.Children.Add(Title("ControlFS"));
        }
        var grid = new Grid { ColumnSpacing = Theme.SpaceM, RowSpacing = Theme.SpaceXs };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < about.Lines.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = about.Lines[i].Label, FontSize = Theme.FontBody, Foreground = Theme.TextMuted };
            var value = new TextBlock { Text = about.Lines[i].Value, FontSize = Theme.FontBody, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        stack.Children.Add(grid);
        stack.Children.Add(new TextBlock
        {
            Text = "Este programa é software livre: você pode redistribuí-lo e/ou modificá-lo sob os termos da GNU AGPL versão 3. Ele é distribuído sem nenhuma garantia.",
            FontSize = Theme.FontCaption,
            Foreground = Theme.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, Theme.SpaceM, 0, 0),
        });
        return Card(Scroll(stack), 760);
    }

    private static (WeakReference<VirtualKeyboard>? Keyboard, int Caret, int Length) _lastCaret;

    /// <summary>Só movimentos do cursor (texto igual) são anunciados ao Narrador, para não falar a cada tecla.</summary>
    private static void AnnounceCaretMove(FrameworkElement field, VirtualKeyboard kb, string spoken)
    {
        var moved = _lastCaret.Keyboard is { } last && last.TryGetTarget(out var previous) && ReferenceEquals(previous, kb) && _lastCaret.Length == kb.Length && _lastCaret.Caret != kb.Caret;
        _lastCaret = (new WeakReference<VirtualKeyboard>(kb), kb.Caret, kb.Length);
        if (!moved) return;
        field.Loaded += (_, _) => FrameworkElementAutomationPeer.CreatePeerForElement(field)?.RaiseNotificationEvent(
            AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, spoken, "ControlFS.KeyboardCaret");
    }

    /// <summary>
    /// Texto com um cursor fixo (sem piscar) na cor de destaque, largo o bastante para ser visto a distância. O trecho
    /// selecionado ganha fundo de destaque e sublinhado (não depende só de cor).
    /// </summary>
    private static TextBlock CaretText(string display, int caret, int selectionStart, int selectionLength)
    {
        var text = new TextBlock { FontSize = Theme.FontItem, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
        var selStart = Math.Clamp(selectionStart, 0, display.Length);
        var selEnd = Math.Clamp(selStart + selectionLength, selStart, display.Length);
        int[] cuts = [.. new[] { 0, selStart, selEnd, caret, display.Length }.Distinct().Order()];
        for (var i = 0; i < cuts.Length; i++)
        {
            if (cuts[i] == caret) text.Inlines.Add(new Run { Text = "┃", Foreground = Theme.Accent, FontWeight = FontWeights.Bold });
            if (i + 1 >= cuts.Length || cuts[i + 1] == cuts[i]) continue;
            var selected = cuts[i] >= selStart && cuts[i + 1] <= selEnd && selEnd > selStart;
            text.Inlines.Add(new Run { Text = display[cuts[i]..cuts[i + 1]], TextDecorations = selected ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None });
        }
        if (selEnd > selStart)
        {
            // O cursor é um caractere a mais no texto: índices depois dele andam uma posição.
            text.TextHighlighters.Add(new TextHighlighter
            {
                Background = Theme.AccentSoft,
                Foreground = Theme.Text,
                Ranges = { new TextRange { StartIndex = selStart >= caret ? selStart + 1 : selStart, Length = selEnd - selStart } },
            });
        }
        return text;
    }

    private static Border BuildMappingWizard(AppController app, MappingWizardModal modal)
    {
        var wizard = modal.Wizard;
        var stack = new StackPanel { Spacing = Theme.SpaceS };
        stack.Children.Add(Title($"Configurar {wizard.DeviceName}"));
        var required = wizard.Targets.Count(t => !t.Optional);
        string headline;
        string detail;
        switch (wizard.Phase)
        {
            case MappingPhase.Neutral:
                headline = "Solte todos os botões e alavancas";
                detail = "Medindo a posição de repouso de cada eixo…";
                break;
            case MappingPhase.Review:
                headline = "Teste antes de salvar";
                detail = "O controle já funciona com este mapeamento: use as direções e confirmar para escolher. Nada foi salvo ainda." +
                    (wizard.LastTested is { } tested ? $"\nÚltimo comando: {wizard.Targets.FirstOrDefault(t => t.Control == tested)?.Label ?? tested.ToString()}" : string.Empty);
                break;
            default:
                var target = wizard.Current!;
                headline = wizard.Phase == MappingPhase.WaitRelease ? "Solte para continuar" : $"Aperte: {target.Label}";
                detail = $"Passo {wizard.StepIndex + 1} de {wizard.Targets.Count}" + (target.Optional
                    ? $" · opcional — aperte o botão de Voltar do controle (ou Enter) para pular"
                    : wizard.StepIndex < required ? " · obrigatório" : string.Empty);
                break;
        }
        stack.Children.Add(new TextBlock { Text = headline, FontSize = Theme.FontTitle, Foreground = Theme.Accent, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = detail, FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        if (wizard.Feedback is { } feedback)
            stack.Children.Add(new TextBlock { Text = feedback, FontSize = Theme.FontBody, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap });

        var grid = new Grid { ColumnSpacing = Theme.SpaceM, RowSpacing = Theme.SpaceXs, Margin = new Thickness(0, Theme.SpaceS, 0, Theme.SpaceS) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < wizard.Targets.Count; i++)
        {
            var t = wizard.Targets[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var current = wizard.Phase is MappingPhase.Capture or MappingPhase.WaitRelease && i == wizard.StepIndex;
            var label = new TextBlock { Text = (current ? "▶ " : string.Empty) + t.Label, FontSize = Theme.FontCaption, Foreground = current ? Theme.Accent : Theme.TextMuted };
            var value = new TextBlock
            {
                Text = wizard.Bindings.TryGetValue(t.Control, out var b) ? b.Describe() : i < wizard.StepIndex ? "sem botão" : "—",
                FontSize = Theme.FontCaption,
                Foreground = wizard.LastTested == t.Control ? Theme.Accent : Theme.Text,
            };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        stack.Children.Add(grid);

        if (wizard.Phase == MappingPhase.Review)
        {
            for (var i = 0; i < MappingWizardModal.ReviewOptions.Count; i++)
            {
                var index = i;
                stack.Children.Add(Choice(MappingWizardModal.ReviewOptions[i], i == modal.ReviewFocus, true, () => app.PointerChooseModalOption(index), danger: i == 2));
            }
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Teclado: Esc cancela sem salvar · ← refaz o passo anterior · Enter pula um passo opcional. Sem nenhuma entrada por 20 s, a configuração é cancelada.",
                FontSize = Theme.FontCaption,
                Foreground = Theme.TextMuted,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        return Card(Scroll(stack), 760);
    }

    /// <summary>Teste de controles: dispositivos, a última pressão em destaque e as anteriores (mais recente no topo).</summary>
    private static Border BuildControllerTest(AppController app, ControllerTestModal modal)
    {
        var stack = new StackPanel { Spacing = Theme.SpaceS };
        stack.Children.Add(Title(modal.Title));
        stack.Children.Add(new TextBlock
        {
            Text = "Aperte cada botão, direcional, analógico e gatilho: cada pressão mostra o controle físico e a ação que ele produz no ControlFS. " +
                "Nada é executado aqui. No controle, segure Confirmar 1 s para copiar o relatório e segure Voltar 1 s para sair; no teclado, Enter copia e Esc sai.",
            FontSize = Theme.FontBody,
            Foreground = Theme.TextMuted,
            TextWrapping = TextWrapping.Wrap,
        });

        var devices = app.ControllerTestDevices(modal);
        stack.Children.Add(new TextBlock { Text = $"Controles ({devices.Count})", FontSize = Theme.FontItem, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, Margin = new Thickness(0, Theme.SpaceS, 0, 0) });
        if (devices.Count == 0)
            stack.Children.Add(new TextBlock { Text = "Nenhum controle detectado. Conecte um controle (USB, Bluetooth ou receptor).", FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        foreach (var device in devices)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"{device.Number}. {AppController.DescribeDevice(device)}",
                FontSize = Theme.FontCaption,
                Foreground = device.IsActive ? Theme.Accent : device.IsConnected ? Theme.Text : Theme.TextDisabled,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var last = modal.Lines.Count > 0 ? modal.Lines[^1] : null;
        stack.Children.Add(new TextBlock
        {
            Text = last is null ? "Aperte um botão…" : $"#{last.Device} {AppController.DescribeInput(last, english: false)} → {last.Action?.ToString() ?? "nenhuma ação"}",
            FontSize = Theme.FontTitle,
            Foreground = Theme.Accent,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, Theme.SpaceS, 0, 0),
        });
        foreach (var line in modal.Lines.AsEnumerable().Reverse().Skip(1).Take(8))
            stack.Children.Add(new TextBlock { Text = $"#{line.Device} {AppController.DescribeInput(line, english: false)} → {line.Action?.ToString() ?? "nenhuma ação"}", FontSize = Theme.FontCaption, Foreground = Theme.TextMuted });
        if (modal.Notice is { } notice)
            stack.Children.Add(new TextBlock { Text = notice, FontSize = Theme.FontBody, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(Choice($"Copiar relatório ({modal.Lines.Count} pressões)", false, true, app.CopyControllerReport));
        return Card(Scroll(stack), 760);
    }

    private static Border BuildKeyboard(AppController app, KeyboardModal modal)
    {
        var kb = modal.Keyboard;
        var stack = new StackPanel();
        stack.Children.Add(Title(kb.Title));

        var display = kb.DisplayText;
        var caret = Math.Min(kb.Caret, display.Length);
        var fieldText = CaretText(display, caret, kb.SelectionStart, kb.SelectionLength);
        var field = new Border
        {
            Background = Theme.SurfaceRaised,
            BorderBrush = Theme.Accent,
            BorderThickness = Theme.Hairline,
            CornerRadius = Theme.Radius,
            Padding = new Thickness(Theme.SpaceM),
            Child = fieldText,
        };
        var caretSpoken = kb.Length == 0 ? "campo vazio" : kb.HasSelection ? $"{kb.SelectionLength} de {kb.Length} caracteres selecionados" : caret == 0 ? "cursor no início" : caret >= kb.Length ? "cursor no fim" : $"cursor na posição {caret} de {kb.Length}";
        AutomationProperties.SetName(field, (kb.Kind == TextFieldKind.Password ? $"Senha, {kb.Length} caracteres" : $"Texto: {kb.Text}") + ", " + caretSpoken);
        AnnounceCaretMove(fieldText, kb, caretSpoken); // o TextBlock tem peer de automação; o Border não
        stack.Children.Add(field);

        var status = $"{(kb.Language == KeyboardLanguage.PortugueseBrazil ? "PT-BR" : "EN")} · {kb.Page switch { KeyboardPage.Symbols => "símbolos", KeyboardPage.Accents => "acentos", _ => "letras" }}" +
            (kb.Shift == ShiftState.Locked ? " · MAIÚSCULAS" : kb.Shift == ShiftState.Once ? " · próxima maiúscula" : string.Empty) +
            (modal.IsBusy ? " · aplicando…" : string.Empty);
        stack.Children.Add(new TextBlock { Text = status, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, Margin = new Thickness(0, Theme.SpaceS, 0, 0) });
        if (kb.ErrorMessage is { } error)
            stack.Children.Add(new TextBlock { Text = "⚠ " + error, FontSize = Theme.FontBody, Foreground = Theme.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Theme.SpaceS, 0, 0) });

        var gap = Theme.Scaled(6);
        var grid = new Grid { ColumnSpacing = gap, RowSpacing = gap, Margin = new Thickness(0, Theme.SpaceM, 0, 0) };
        for (var c = 0; c < VirtualKeyboardLayouts.Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < kb.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Theme.Layout.KeyHeight) });
            var column = 0;
            for (var k = 0; k < kb.Rows[r].Count; k++)
            {
                var key = kb.Rows[r][k];
                var focused = r == kb.Row && k == kb.Column;
                var enabled = kb.IsKeyEnabled(key);
                var row = r;
                var keyIndex = k;
                var isDone = key.Kind == KeyKind.Done;
                var isCurrentPage = VirtualKeyboardLayouts.PageOf(key) == kb.Page;
                var isActiveShift = key.Kind == KeyKind.Shift && kb.Shift != ShiftState.Off;
                var label = key.Kind == KeyKind.Shift && kb.Shift == ShiftState.Locked ? "⇪" : kb.DisplayLabel(key);
                var cell = new Border
                {
                    Background = focused ? Theme.AccentSoft : key.IsFunction ? Theme.Surface : Theme.SurfaceRaised,
                    BorderBrush = focused || isDone ? Theme.Accent : Theme.Border,
                    BorderThickness = focused ? Theme.FocusRing : Theme.Hairline,
                    CornerRadius = Theme.Radius,
                    Child = new TextBlock
                    {
                        Text = label,
                        FontSize = key.IsFunction && label.Length > 3 ? Theme.FontBody : Theme.FontItem,
                        FontWeight = isDone || isCurrentPage || isActiveShift ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = !enabled ? Theme.TextDisabled : isCurrentPage || isActiveShift || isDone ? Theme.Accent : Theme.Text,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                if (focused) KeepInView(cell);
                AutomationProperties.SetName(cell, key.Name + (isCurrentPage ? ", página atual" : string.Empty) + (enabled ? string.Empty : ", indisponível neste campo"));
                cell.Tapped += (_, _) => app.PointerPressKey(row, keyIndex);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, column);
                Grid.SetColumnSpan(cell, key.Span);
                grid.Children.Add(cell);
                column += key.Span;
            }
        }
        stack.Children.Add(grid);
        return Card(Scroll(stack), 960);
    }
}
