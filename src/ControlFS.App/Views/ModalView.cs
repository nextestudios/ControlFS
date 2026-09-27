using ControlFS.App.Controls;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.Prompts;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Input.Mapping;
using ControlFS.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace ControlFS.App.Views;

/// <summary>
/// Constrói a camada modal a partir do estado do AppController (escopo exclusivo de entrada). Todos os modais usam o
/// mesmo sistema (#172): página escurecida, painel fosco com cantos arredondados, fio de luz e sombra; cabeçalho com
/// ícone, título e contexto; opções em linhas de largura total com ícone; a opção focada preenchida (ciano, texto
/// escuro, negrito, um pouco maior); legendas do controle em uso no rodapé do próprio painel.
/// </summary>
public static partial class ModalView
{
    /// <summary>
    /// AutomationIds estáveis que os testes de UI Automation (build/Test-UiAutomation.ps1) procuram. O foco é lógico
    /// (linha preenchida desenhada, não foco do XAML), então o texto da opção/tecla focada recebe um id próprio.
    /// </summary>
    public const string TitleId = "ControlFS.ModalTitle";
    public const string FocusedOptionId = "ControlFS.FocusedOption";
    public const string FocusedKeyId = "ControlFS.FocusedKey";
    public const string KeyboardFieldId = "ControlFS.KeyboardField";

    /// <summary>Marca o painel dentro da camada (o gerador de capturas mede se ele cabe na tela).</summary>
    public const string CardTag = "modal-card";

    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    /// <summary>O último modal desenhado: só um modal novo ganha a transição de entrada (o modal é refeito a cada quadro).</summary>
    private static WeakReference<Modal>? _shown;

    /// <summary>Modal montado e mantido na tela; mudanças só de foco (e de texto no teclado) são aplicadas nele.</summary>
    private sealed record Retained(Modal Modal, string Key, UIElement Scrim, Action Update);

    private static Retained? _retained;

    /// <summary>
    /// Devolve a camada do modal do topo. Menus, diálogos e o teclado virtual são montados uma vez e, enquanto a estrutura
    /// (opções, textos, página, tamanho da tela…) não muda, só as linhas/teclas que ganham ou perdem o foco são trocadas
    /// no lugar: sem recriar o painel fosco, a sombra e a rolagem a cada movimento, que davam o efeito de "fantasma"
    /// (a rolagem nova nascia no topo e só depois voltava à opção focada). Os demais modais são refeitos como antes.
    /// </summary>
    public static UIElement? Build(AppController app)
    {
        if (app.TopModal is not { } modal)
        {
            _retained = null;
            return null;
        }
        var key = RetainKey(modal);
        if (key is not null && _retained is { } kept && ReferenceEquals(kept.Modal, modal) && kept.Key == key)
        {
            kept.Update();
            return kept.Scrim;
        }
        Action? update = null;
        var card = modal switch
        {
            MenuModal menu => BuildMenu(app, menu, out update),
            DialogModal dialog => BuildDialog(app, dialog, out update),
            KeyboardModal keyboard => BuildKeyboard(app, keyboard, out update),
            AboutModal about => BuildAbout(app, about),
            MappingWizardModal wizard => BuildMappingWizard(app, wizard),
            ControllerTestModal test => BuildControllerTest(app, test),
            ImagePreviewModal preview => BuildImagePreview(app, preview),
            TextPreviewModal text => BuildTextPreview(app, text),
            _ => null,
        };
        if (card is null) return null;
        card.Tag = CardTag;

        // A camada inteira recebe os toques: nada por baixo do modal pode ser clicado (o AppController já ignora a entrada).
        var scrim = new Grid { Background = Theme.ModalScrim };
        var shadowReceiver = new Grid();
        scrim.Children.Add(shadowReceiver);
        scrim.Children.Add(card);
        if (!Theme.SolidSurfaces)
        {
            var shadow = new ThemeShadow();
            shadow.Receivers.Add(shadowReceiver);
            card.Shadow = shadow;
            card.Translation = new System.Numerics.Vector3(0, 0, 48);
        }

        var isNew = _shown is null || !_shown.TryGetTarget(out var previous) || !ReferenceEquals(previous, modal);
        _shown = new WeakReference<Modal>(modal);
        if (isNew && !Theme.ReduceMotion)
        {
            // Só opacidade, 120 ms: o estado do modal já vale; a entrada nunca espera a animação.
            card.Opacity = 0;
            card.OpacityTransition = new ScalarTransition { Duration = Theme.MotionModal };
            card.Loaded += (_, _) => card.Opacity = 1;
        }
        _retained = key is not null && update is not null ? new Retained(modal, key, scrim, update) : null;
        return scrim;
    }

    /// <summary>
    /// Tudo o que muda a estrutura de um modal mantido (fora o foco). Null: o modal é refeito a cada quadro. O tamanho da
    /// tela, a faixa de layout e a superfície sólida entram na chave para um redimensionamento refazer o painel.
    /// </summary>
    private static string? RetainKey(Modal modal)
    {
        var screen = $"{Theme.Viewport.Width:0}x{Theme.Viewport.Height:0}|{Theme.Layout.Tier}|{Theme.SolidSurfaces}|{modal.Title}|{modal.Subtitle}|{modal.Icon}";
        return modal switch
        {
            MenuModal menu => screen + "|m|" + string.Join("\u0001", menu.Items.Select(i => $"{i.Label}|{i.IsEnabled}|{i.DisabledReason}|{i.Detail}|{i.Section}|{i.Icon}|{i.IsDestructive}")),
            DialogModal dialog => dialog.Progress is not null ? null : screen + "|d|" + dialog.Message + "|"
                + string.Join("\u0001", dialog.Lines.Select(l => l.Label + "=" + l.Value)) + "|"
                + string.Join("\u0001", dialog.Options.Select(o => $"{o.Label}|{o.Kind}|{o.IsChecked}|{o.Icon}|{o.IsDestructive}")),
            KeyboardModal keyboard => screen + $"|k|{keyboard.Keyboard.Page}|{keyboard.Keyboard.Language}|{keyboard.Keyboard.Shift}|{keyboard.Keyboard.IsRevealed}|{keyboard.IsBusy}|"
                + string.Join(",", keyboard.Keyboard.Rows.Select(r => r.Count)),
            _ => null,
        };
    }

    // ---------- Painel ----------

    private static double PanelPadding => Theme.Space(28);

    private static double PanelMargin => Theme.SpaceM;

    /// <summary>
    /// Painel do modal: cabeçalho, corpo (rolável; a altura nunca passa da janela) e, embaixo, o aviso do rodapé (se
    /// houver) e as legendas do controle em uso. <paramref name="scroll"/> false: o corpo já cabe (visualizações).
    /// </summary>
    private static Border Panel(AppController app, FrameworkElement header, UIElement body, double maxWidth, bool scroll = true, bool stretch = false, double minWidth = 0, Action<Border>? footerSink = null)
    {
        var grid = new Grid { RowSpacing = Theme.SpaceM };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // Cabeçalho separado do corpo por um fio: o conteúdo que rola nunca encosta no título.
        var top = new StackPanel { Spacing = Theme.SpaceM };
        top.Children.Add(header);
        top.Children.Add(new Border { Height = Theme.Hairline.Top, Background = Theme.ModalDivider });
        grid.Children.Add(top);

        UIElement content = body;
        if (scroll)
        {
            // A opção focada cresce um pouco: a folga (dentro do conteúdo, que a rolagem não recorta) mantém as bordas dela.
            var inset = Theme.Scaled(8);
            content = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(inset, Theme.SpaceXs, inset, Theme.SpaceXs), Child = body },
                Margin = new Thickness(-inset, -Theme.SpaceXs, -inset, -Theme.SpaceXs),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
        }
        Grid.SetRow((FrameworkElement)content, 1);
        grid.Children.Add(content);

        var footer = new Border { Child = Footer(app) };
        footerSink?.Invoke(footer);
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        return new Border
        {
            Background = Theme.ModalPanel(),
            BorderBrush = Theme.ModalEdge,
            BorderThickness = Theme.Hairline,
            CornerRadius = Theme.ModalRadius,
            Padding = new Thickness(PanelPadding, PanelPadding - Theme.SpaceXs, PanelPadding, Theme.Space(20)),
            MaxWidth = Math.Min(Theme.Scaled(maxWidth), Theme.Viewport.Width - (2 * PanelMargin)),
            MaxHeight = Theme.Viewport.Height - (2 * PanelMargin),
            MinWidth = Math.Min(Theme.Scaled(minWidth), Theme.Viewport.Width - (2 * PanelMargin)),
            Width = stretch ? Theme.Viewport.Width - (2 * PanelMargin) : double.NaN,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(PanelMargin),
            Child = grid,
        };
    }

    /// <summary>Cabeçalho: ícone num selo com o tom do modal, título (id estável para o UIA) e a linha de contexto.</summary>
    private static Grid Header(string title, ActionIcon icon, string? subtitle = null)
    {
        var grid = new Grid { ColumnSpacing = Theme.SpaceM };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (icon != ActionIcon.None)
        {
            var tone = ToneBrush(icon);
            var size = Theme.Scaled(46);
            grid.Children.Add(new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(Theme.Scaled(13)),
                Background = new SolidColorBrush(WithAlpha(tone.Color, 0x2E)),
                VerticalAlignment = VerticalAlignment.Top,
                Child = Glyph(icon, Theme.Font(22), tone),
            });
        }
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = Theme.Space(2) };
        var heading = new TextBlock
        {
            Text = title,
            FontSize = Theme.FontTitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AutomationProperties.SetAutomationId(heading, TitleId);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        texts.Children.Add(heading);
        if (subtitle is { Length: > 0 })
            texts.Children.Add(new TextBlock { Text = subtitle, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        return grid;
    }

    private static Grid Header(Modal modal) => Header(modal.Title, modal.Icon, modal.Subtitle);

    /// <summary>Cor do selo do cabeçalho: erro vermelho, aviso âmbar, sucesso verde; o resto, o ciano do tema.</summary>
    private static SolidColorBrush ToneBrush(ActionIcon icon) => icon switch
    {
        ActionIcon.Error => Theme.Danger,
        ActionIcon.Warning => Theme.Warning,
        ActionIcon.Success => Theme.Success,
        _ when ActionIcons.IsDestructive(icon) => Theme.Danger,
        _ => Theme.Accent,
    };

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>Símbolo da fonte de ícones do Windows; o Narrador ignora (o texto ao lado já diz tudo).</summary>
    private static TextBlock Glyph(ActionIcon icon, double size, Brush foreground) => Glyph(ActionIcons.Glyph(icon), size, foreground);

    private static TextBlock Glyph(string glyph, double size, Brush foreground)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(IconFont),
            FontSize = size,
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsTextScaleFactorEnabled = false,
        };
        AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw);
        return text;
    }

    /// <summary>
    /// Rodapé do painel: o aviso do momento (ex.: por que uma opção está indisponível) e as legendas do controle em uso,
    /// as mesmas do rodapé da janela (glifo certo por família; só ações que funcionam neste modal).
    /// </summary>
    private static StackPanel Footer(AppController app)
    {
        var footer = new StackPanel { Spacing = Theme.SpaceS };
        footer.Children.Add(new Border { Height = Theme.Hairline.Top, Background = Theme.ModalDivider, Margin = new Thickness(0, 0, 0, Theme.SpaceXs) });
        if (app.StatusMessage is { Length: > 0 } status)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS };
            line.Children.Add(Glyph(ActionIcon.Info, Theme.FontBody, Theme.Accent));
            line.Children.Add(new TextBlock { Text = status, FontSize = Theme.FontBody, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            footer.Children.Add(line);
        }
        footer.Children.Add(PromptBar(app.Prompts, PromptGlyphHeight, Theme.FontBody));
        return footer;
    }

    private static double PromptGlyphHeight => Math.Round(Theme.FontBody * (Theme.Layout.Tier == Core.Layout.LayoutTier.Compact ? 1.6 : 1.8));

    /// <summary>Legendas (glifo do controle ou tecla do teclado + texto) que quebram linha; usadas também pelo rodapé da janela.</summary>
    public static WrapPanel PromptBar(IEnumerable<ControllerPrompt> prompts, double glyphHeight, double labelSize, Func<ControllerPrompt, bool>? skip = null)
    {
        var bar = new WrapPanel { HorizontalSpacing = Theme.SpaceL, VerticalSpacing = Theme.SpaceS };
        foreach (var prompt in prompts)
        {
            if (skip?.Invoke(prompt) == true) continue;
            bar.Children.Add(PromptChip(prompt, glyphHeight, labelSize));
        }
        return bar;
    }

    public static StackPanel PromptChip(ControllerPrompt prompt, double glyphHeight, double labelSize)
    {
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS + Theme.SpaceXs };
        if (prompt is { Button: { } button, Family: { } family })
            chip.Children.Add(ControllerGlyphs.Create(button, family, glyphHeight));
        else
            chip.Children.Add(new Border
            {
                Background = Theme.SurfaceRaised,
                BorderBrush = Theme.Border,
                BorderThickness = Theme.Hairline,
                CornerRadius = Theme.Radius,
                MinHeight = glyphHeight * 0.8,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(Theme.SpaceS, Theme.SpaceXs / 2, Theme.SpaceS, Theme.SpaceXs / 2),
                Child = new TextBlock { Text = prompt.Key, FontSize = Theme.FontCaption, Foreground = Theme.Text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            });
        chip.Children.Add(new TextBlock { Text = prompt.Label, FontSize = labelSize, Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(chip, prompt.AccessibilityText);
        return chip;
    }

    /// <summary>
    /// O foco é lógico (AppController), não do XAML: a cada quadro o modal é refeito, então o elemento focado pede
    /// para ser mostrado, centralizado, assim que entra na árvore. Sem isso itens abaixo da dobra ficavam invisíveis.
    /// </summary>
    private static void KeepInView(FrameworkElement element) =>
        element.Loaded += (_, _) => element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.5 });

    // ---------- Linhas de opção ----------

    /// <summary>
    /// Uma opção: ícone, texto (e uma linha secundária) e, à direita, um marcador. Focada: preenchimento ciano (ou
    /// vermelho, se perigosa), texto escuro em negrito e um pouco maior — nunca depende só da cor. Perigosa: vermelha e
    /// com o símbolo de alerta no fim da linha. Indisponível: esmaecida; focada, diz o motivo.
    /// </summary>
    private static Border Row(string label, string glyph, bool focused, bool enabled, bool destructive, Action onTap, string? secondary = null)
    {
        var fill = !focused ? Theme.Transparent : !enabled ? Theme.DisabledFill : destructive ? Theme.DangerFill : Theme.FocusFill;
        var ink = focused && enabled ? Theme.FocusText : !enabled ? Theme.TextDisabled : destructive ? Theme.Danger : Theme.Text;
        var iconInk = focused && enabled ? Theme.FocusText : !enabled ? Theme.TextDisabled : destructive ? Theme.Danger : Theme.Accent;
        if (focused && !enabled) ink = iconInk = Theme.TextMuted;

        var grid = new Grid { ColumnSpacing = Theme.Space(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.Scaled(28)) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (glyph.Length > 0) grid.Children.Add(Glyph(glyph, Theme.Font(20), iconInk));

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock
        {
            Text = label,
            FontSize = Theme.FontItem,
            FontWeight = focused ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = ink,
            TextWrapping = TextWrapping.Wrap,
        };
        if (focused) AutomationProperties.SetAutomationId(text, FocusedOptionId);
        texts.Children.Add(text);
        if (secondary is { Length: > 0 })
            texts.Children.Add(new TextBlock
            {
                Text = secondary,
                FontSize = Theme.FontCaption,
                Foreground = focused && enabled ? Theme.FocusText : Theme.TextMuted,
                Opacity = focused && enabled ? 0.8 : 1,
                TextWrapping = TextWrapping.Wrap,
            });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        if (destructive)
        {
            var warning = Glyph(ActionIcon.Warning, Theme.FontBody, focused && enabled ? Theme.FocusText : !enabled ? Theme.TextDisabled : Theme.Danger);
            Grid.SetColumn(warning, 2);
            grid.Children.Add(warning);
        }

        var row = new Border
        {
            Child = grid,
            Background = fill,
            CornerRadius = Theme.RowRadius,
            Padding = new Thickness(Theme.Space(14), Theme.Space(11), Theme.Space(14), Theme.Space(11)),
            Margin = new Thickness(0, 1, 0, 1),
            MinHeight = Theme.Scaled(52),
        };
        if (focused)
        {
            row.RenderTransformOrigin = new Point(0.5, 0.5);
            row.RenderTransform = new ScaleTransform { ScaleX = Theme.ModalFocusScale, ScaleY = Theme.ModalFocusScale };
            KeepInView(row);
        }
        AutomationProperties.SetName(row, label + (enabled ? string.Empty : ", indisponível") + (destructive ? ", ação perigosa" : string.Empty));
        row.Tapped += (_, _) => onTap();
        return row;
    }

    /// <summary>Título de um grupo de opções, com a linha que o separa do grupo anterior.</summary>
    private static StackPanel SectionHeading(string? title, bool first)
    {
        var section = new StackPanel { Spacing = Theme.SpaceXs, Margin = new Thickness(Theme.Space(14), first ? 0 : Theme.SpaceS, 0, Theme.Space(2)) };
        if (!first) section.Children.Add(new Border { Height = Theme.Hairline.Top, Background = Theme.ModalDivider, Margin = new Thickness(-Theme.Space(14), 0, 0, Theme.SpaceXs) });
        if (title is { Length: > 0 })
        {
            var heading = new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontSize = Theme.FontCaption - 1,
                FontWeight = FontWeights.SemiBold,
                CharacterSpacing = 60,
                Foreground = Theme.TextMuted,
            };
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
            section.Children.Add(heading);
        }
        return section;
    }

    private static Border BuildMenu(AppController app, MenuModal menu, out Action update)
    {
        var stack = new StackPanel();
        var positions = new int[menu.Items.Count];
        string? section = null;
        for (var i = 0; i < menu.Items.Count; i++)
        {
            var item = menu.Items[i];
            if (item.Section != section && (i > 0 || item.Section is not null)) stack.Children.Add(SectionHeading(item.Section, first: i == 0));
            section = item.Section;
            positions[i] = stack.Children.Count;
            stack.Children.Add(MenuRow(app, menu, i));
        }
        if (menu.Items.Count == 0)
            stack.Children.Add(new TextBlock { Text = "Nenhuma opção.", FontSize = Theme.FontBody, Foreground = Theme.TextMuted });
        Border? footer = null;
        var card = Panel(app, Header(menu), stack, 600, minWidth: 460, footerSink: f => footer = f);
        var shown = menu.FocusIndex;
        update = () =>
        {
            var now = menu.FocusIndex;
            if (now != shown)
            {
                if (shown >= 0 && shown < positions.Length) stack.Children[positions[shown]] = MenuRow(app, menu, shown);
                if (now >= 0 && now < positions.Length) stack.Children[positions[now]] = MenuRow(app, menu, now);
                shown = now;
            }
            if (footer is not null) footer.Child = Footer(app);
        };
        return card;
    }

    private static Border MenuRow(AppController app, MenuModal menu, int index)
    {
        var item = menu.Items[index];
        var focused = index == menu.FocusIndex;
        var secondary = !item.IsEnabled && focused ? "Indisponível: " + item.DisabledReason : item.Detail;
        return Row(item.Label, ActionIcons.Glyph(item.Icon), focused, item.IsEnabled, item.IsDestructive, () => app.PointerChooseModalOption(index), secondary);
    }

    // ---------- Diálogos ----------

    /// <summary>Linhas "rótulo: valor" num quadro discreto (informações do diálogo, do Sobre e do assistente).</summary>
    private static Border InfoLines(IReadOnlyList<(string Label, string Value)> lines, double fontSize)
    {
        var grid = new Grid { ColumnSpacing = Theme.SpaceM, RowSpacing = Theme.SpaceS };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < lines.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = lines[i].Label, FontSize = fontSize, Foreground = Theme.TextMuted, MaxWidth = Theme.Scaled(260), TextWrapping = TextWrapping.Wrap };
            var value = new TextBlock { Text = lines[i].Value, FontSize = fontSize, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
        return new Border
        {
            Background = Theme.ModalInset,
            CornerRadius = Theme.RowRadius,
            Padding = new Thickness(Theme.SpaceM, Theme.Space(12), Theme.SpaceM, Theme.Space(12)),
            Child = grid,
        };
    }

    /// <summary>Barra de andamento (operação em curso): trilho discreto e preenchimento ciano, com a porcentagem ao lado.</summary>
    private static Grid ProgressBar(double fraction)
    {
        var grid = new Grid { ColumnSpacing = Theme.SpaceM };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var height = Theme.Scaled(8);
        var track = new Grid { Height = height, CornerRadius = new CornerRadius(height / 2), Background = Theme.ModalInset, VerticalAlignment = VerticalAlignment.Center };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.0001), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fraction, 0.0001), GridUnitType.Star) });
        track.Children.Add(new Border { Background = Theme.Accent, CornerRadius = new CornerRadius(height / 2) });
        grid.Children.Add(track);
        var percent = new TextBlock
        {
            Text = string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{fraction * 100:0}%"),
            FontSize = Theme.FontBody,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
        };
        Grid.SetColumn(percent, 1);
        grid.Children.Add(percent);
        AutomationProperties.SetName(grid, percent.Text + " concluído");
        return grid;
    }

    private static string CheckGlyph(bool on) => char.ConvertFromUtf32(on ? 0xE73A : 0xE739);

    private static Border BuildDialog(AppController app, DialogModal dialog, out Action update)
    {
        var stack = new StackPanel { Spacing = Theme.Space(12) };
        if (dialog.Lines.Count > 0) stack.Children.Add(InfoLines(dialog.Lines, Theme.FontBody));
        if (dialog.Progress is { } progress) stack.Children.Add(ProgressBar(progress));
        if (dialog.Message is { } message)
            stack.Children.Add(new TextBlock { Text = message, FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        var options = new StackPanel { Margin = new Thickness(0, Theme.SpaceXs, 0, 0) };
        for (var i = 0; i < dialog.Options.Count; i++) options.Children.Add(DialogRow(app, dialog, i));
        if (dialog.Options.Count > 0) stack.Children.Add(options);
        Border? footer = null;
        var card = Panel(app, Header(dialog), stack, 720, footerSink: f => footer = f);
        var shown = dialog.FocusIndex;
        update = () =>
        {
            var now = dialog.FocusIndex;
            if (now != shown)
            {
                if (shown >= 0 && shown < options.Children.Count) options.Children[shown] = DialogRow(app, dialog, shown);
                if (now >= 0 && now < options.Children.Count) options.Children[now] = DialogRow(app, dialog, now);
                shown = now;
            }
            if (footer is not null) footer.Child = Footer(app);
        };
        return card;
    }

    private static Border DialogRow(AppController app, DialogModal dialog, int index)
    {
        var option = dialog.Options[index];
        var glyph = option.Kind == DialogOptionKind.Toggle ? CheckGlyph(option.IsChecked) : ActionIcons.Glyph(option.Icon);
        return Row(option.Label, glyph, index == dialog.FocusIndex, true, option.IsDestructive, () => app.PointerChooseModalOption(index));
    }

    private static Border BuildAbout(AppController app, AboutModal about)
    {
        var stack = new StackPanel { Spacing = Theme.SpaceM };
        if (Branding.Logo is { } logo)
        {
            var image = new Image { Source = logo, Height = Theme.Scaled(64), HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
            AutomationProperties.SetName(image, "ControlFS");
            stack.Children.Add(image);
        }
        stack.Children.Add(InfoLines(about.Lines, Theme.FontBody));
        stack.Children.Add(new TextBlock
        {
            Text = "Este programa é software livre: você pode redistribuí-lo e/ou modificá-lo sob os termos da GNU AGPL versão 3. Ele é distribuído sem nenhuma garantia.",
            FontSize = Theme.FontCaption,
            Foreground = Theme.TextMuted,
            TextWrapping = TextWrapping.Wrap,
        });
        return Panel(app, Header(about.Title, about.Icon, "Versão " + about.Version), stack, 720);
    }

    // ---------- Teclado na tela ----------

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

    private static Border BuildKeyboard(AppController app, KeyboardModal modal, out Action update)
    {
        var kb = modal.Keyboard;
        var stack = new StackPanel { Spacing = Theme.SpaceS };
        var top = new StackPanel { Spacing = Theme.SpaceS };
        FillKeyboardTop(app, modal, top);
        stack.Children.Add(top);

        var gap = Theme.Scaled(6);
        var grid = new Grid { ColumnSpacing = gap, RowSpacing = gap, Margin = new Thickness(0, Theme.SpaceS, 0, 0) };
        for (var c = 0; c < VirtualKeyboardLayouts.Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var positions = new int[kb.Rows.Count][];
        var columns = new int[kb.Rows.Count][];
        for (var r = 0; r < kb.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Theme.Layout.KeyHeight) });
            positions[r] = new int[kb.Rows[r].Count];
            columns[r] = new int[kb.Rows[r].Count];
            var column = 0;
            for (var k = 0; k < kb.Rows[r].Count; k++)
            {
                positions[r][k] = grid.Children.Count;
                columns[r][k] = column;
                grid.Children.Add(KeyCell(app, kb, r, k, column));
                column += kb.Rows[r][k].Span;
            }
        }
        stack.Children.Add(grid);
        Border? footer = null;
        var card = Panel(app, Header(modal), stack, 960, footerSink: f => footer = f);

        var shown = (kb.Row, kb.Column, kb.SuggestionIndex);
        update = () =>
        {
            top.Children.Clear();
            FillKeyboardTop(app, modal, top);
            var now = (kb.Row, kb.Column, kb.SuggestionIndex);
            if (now != shown)
            {
                Refresh(shown.Row, shown.Column);
                Refresh(now.Row, now.Column);
                shown = now;
            }
            if (footer is not null) footer.Child = Footer(app);
        };
        return card;

        void Refresh(int r, int k)
        {
            if (r < 0 || r >= positions.Length || k < 0 || k >= positions[r].Length) return;
            grid.Children[positions[r][k]] = KeyCell(app, kb, r, k, columns[r][k]);
        }
    }

    /// <summary>Campo de texto, estado, erro e faixa de sugestões: refeitos no lugar a cada quadro (o texto muda ao digitar).</summary>
    private static void FillKeyboardTop(AppController app, KeyboardModal modal, StackPanel stack)
    {
        var kb = modal.Keyboard;
        var display = kb.DisplayText;
        var caret = Math.Min(kb.Caret, display.Length);
        var fieldText = CaretText(display, caret, kb.SelectionStart, kb.SelectionLength);
        var field = new Border
        {
            Background = Theme.ModalInset,
            BorderBrush = kb.ErrorMessage is null ? Theme.Accent : Theme.Danger,
            BorderThickness = Theme.FocusRing,
            CornerRadius = Theme.RowRadius,
            Padding = new Thickness(Theme.SpaceM, Theme.Space(12), Theme.SpaceM, Theme.Space(12)),
            Child = fieldText,
        };
        var caretSpoken = kb.Length == 0 ? "campo vazio" : kb.HasSelection ? $"{kb.SelectionLength} de {kb.Length} caracteres selecionados" : caret == 0 ? "cursor no início" : caret >= kb.Length ? "cursor no fim" : $"cursor na posição {caret} de {kb.Length}";
        AutomationProperties.SetName(field, (kb.Kind == TextFieldKind.Password ? $"Senha, {kb.Length} caracteres" : $"Texto: {kb.Text}") + ", " + caretSpoken);
        AnnounceCaretMove(fieldText, kb, caretSpoken); // o TextBlock tem peer de automação; o Border não
        AutomationProperties.SetName(fieldText, AutomationProperties.GetName(field));
        AutomationProperties.SetAutomationId(fieldText, KeyboardFieldId);
        stack.Children.Add(field);

        var status = $"{(kb.Language == KeyboardLanguage.PortugueseBrazil ? "PT-BR" : "EN")} · {kb.Page switch { KeyboardPage.Symbols => "símbolos", KeyboardPage.Accents => "acentos", _ => "letras" }}" +
            (kb.Shift == ShiftState.Locked ? " · MAIÚSCULAS" : kb.Shift == ShiftState.Once ? " · próxima maiúscula" : string.Empty) +
            (modal.IsBusy ? " · aplicando…" : string.Empty);
        stack.Children.Add(new TextBlock { Text = status, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted });
        if (kb.ErrorMessage is { } error)
        {
            // Erro de validação: símbolo e texto (nunca só a cor), e o campo continua aberto para corrigir.
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS };
            line.Children.Add(Glyph(ActionIcon.Error, Theme.FontBody, Theme.Danger));
            line.Children.Add(new TextBlock { Text = error, FontSize = Theme.FontBody, Foreground = Theme.Danger, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(line);
        }

        var gap = Theme.Scaled(6);
        if (kb.Suggestions is { Count: > 0 } suggestions)
        {
            // Faixa de sugestões locais (#45): acima das teclas; cima a partir da primeira linha foca, Sul usa.
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = gap, Margin = new Thickness(0, Theme.SpaceS, 0, 0) };
            strip.Children.Add(Glyph(ActionIcon.Keyboard, Theme.FontBody, Theme.TextMuted));
            for (var i = 0; i < suggestions.Count; i++)
            {
                var focused = kb.SuggestionIndex == i;
                var index = i;
                var chip = new Border
                {
                    Background = focused ? Theme.FocusFill : Theme.ModalInset,
                    BorderBrush = focused ? Theme.Text : Theme.Transparent,
                    BorderThickness = Theme.Hairline,
                    CornerRadius = new CornerRadius(Theme.Scaled(10)),
                    Padding = new Thickness(Theme.SpaceM, Theme.SpaceS, Theme.SpaceM, Theme.SpaceS),
                    Child = new TextBlock
                    {
                        Text = suggestions[i],
                        FontSize = Theme.FontBody,
                        FontWeight = focused ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = focused ? Theme.FocusText : Theme.Text,
                        MaxWidth = Theme.Scaled(320),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                };
                AutomationProperties.SetName(chip, $"Sugestão: {suggestions[i]}");
                if (focused) KeepInView(chip);
                chip.Tapped += (_, _) => app.PointerPressSuggestion(index);
                strip.Children.Add(chip);
            }
            stack.Children.Add(new ScrollViewer
            {
                Content = strip,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollMode = ScrollMode.Enabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled,
            });
        }
    }

    /// <summary>Uma tecla na grade (posição e largura já aplicadas).</summary>
    private static Border KeyCell(AppController app, VirtualKeyboard kb, int r, int k, int column)
    {
        var key = kb.Rows[r][k];
        var focused = kb.SuggestionIndex is null && r == kb.Row && k == kb.Column;
        var enabled = kb.IsKeyEnabled(key);
        var row = r;
        var keyIndex = k;
        var isDone = key.Kind == KeyKind.Done;
        var isCurrentPage = VirtualKeyboardLayouts.PageOf(key) == kb.Page;
        var isActiveShift = key.Kind == KeyKind.Shift && kb.Shift != ShiftState.Off;
        var label = key.Kind == KeyKind.Shift && kb.Shift == ShiftState.Locked ? "⇪" : kb.DisplayLabel(key);
        // Tecla focada: preenchida (como a opção focada dos menus), em negrito e um pouco maior.
        var cell = new Border
        {
            Background = focused ? (enabled ? Theme.FocusFill : Theme.DisabledFill) : key.IsFunction ? Theme.ModalInset : Theme.ModalDivider,
            BorderBrush = focused ? Theme.Text : isDone ? Theme.Accent : Theme.Transparent,
            BorderThickness = Theme.Hairline,
            CornerRadius = new CornerRadius(Theme.Scaled(10)),
            Child = new TextBlock
            {
                Text = label,
                FontSize = key.IsFunction && label.Length > 3 ? Theme.FontBody : Theme.FontItem,
                FontWeight = focused || isDone || isCurrentPage || isActiveShift ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = focused ? (enabled ? Theme.FocusText : Theme.TextMuted) : !enabled ? Theme.TextDisabled : isCurrentPage || isActiveShift || isDone ? Theme.Accent : Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (focused)
        {
            cell.RenderTransformOrigin = new Point(0.5, 0.5);
            cell.RenderTransform = new ScaleTransform { ScaleX = 1.06, ScaleY = 1.06 };
            Canvas.SetZIndex(cell, 1);
            KeepInView(cell);
            AutomationProperties.SetAutomationId(cell.Child, FocusedKeyId);
        }
        AutomationProperties.SetName(cell, key.Name + (isCurrentPage ? ", página atual" : string.Empty) + (enabled ? string.Empty : ", indisponível neste campo"));
        cell.Tapped += (_, _) => app.PointerPressKey(row, keyIndex);
        Grid.SetRow(cell, r);
        Grid.SetColumn(cell, column);
        Grid.SetColumnSpan(cell, key.Span);
        return cell;
    }

    // ---------- Controles: assistente e teste ----------

    private static Border BuildMappingWizard(AppController app, MappingWizardModal modal)
    {
        var wizard = modal.Wizard;
        var stack = new StackPanel { Spacing = Theme.SpaceS };
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

        var grid = new Grid { ColumnSpacing = Theme.SpaceM, RowSpacing = Theme.SpaceXs };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < wizard.Targets.Count; i++)
        {
            var t = wizard.Targets[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var current = wizard.Phase is MappingPhase.Capture or MappingPhase.WaitRelease && i == wizard.StepIndex;
            var label = new TextBlock { Text = (current ? "▶ " : string.Empty) + t.Label, FontSize = Theme.FontCaption, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, Foreground = current ? Theme.Accent : Theme.TextMuted };
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
        stack.Children.Add(new Border { Background = Theme.ModalInset, CornerRadius = Theme.RowRadius, Padding = new Thickness(Theme.SpaceM, Theme.Space(12), Theme.SpaceM, Theme.Space(12)), Margin = new Thickness(0, Theme.SpaceXs, 0, Theme.SpaceXs), Child = grid });

        if (wizard.Phase == MappingPhase.Review)
        {
            var options = new StackPanel();
            for (var i = 0; i < MappingWizardModal.ReviewOptions.Count; i++)
            {
                var index = i;
                var icon = i switch { 0 => ActionIcon.Accept, 1 => ActionIcon.Retry, _ => ActionIcon.Erase };
                options.Children.Add(Row(MappingWizardModal.ReviewOptions[i], ActionIcons.Glyph(icon), i == modal.ReviewFocus, true, destructive: i == 2, () => app.PointerChooseModalOption(index)));
            }
            stack.Children.Add(options);
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
        return Panel(app, Header($"Configurar {wizard.DeviceName}", modal.Icon, modal.Device.Name != wizard.DeviceName ? modal.Device.Name : null), stack, 760);
    }

    /// <summary>Teste de controles: dispositivos, a última pressão em destaque e as anteriores (mais recente no topo).</summary>
    private static Border BuildControllerTest(AppController app, ControllerTestModal modal)
    {
        var stack = new StackPanel { Spacing = Theme.SpaceS };
        stack.Children.Add(new TextBlock
        {
            Text = "Aperte cada botão, direcional, analógico e gatilho: cada pressão mostra o controle físico e a ação que ele produz no ControlFS. " +
                "Nada é executado aqui. No controle, segure Confirmar 1 s para copiar o relatório e segure Voltar 1 s para sair; no teclado, Enter copia e Esc sai.",
            FontSize = Theme.FontBody,
            Foreground = Theme.TextMuted,
            TextWrapping = TextWrapping.Wrap,
        });

        var devices = app.ControllerTestDevices(modal);
        stack.Children.Add(SectionHeading($"Controles ({devices.Count})", first: false));
        if (devices.Count == 0)
            stack.Children.Add(new TextBlock { Text = "Nenhum controle detectado. Conecte um controle (USB, Bluetooth ou receptor).", FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        foreach (var device in devices)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS };
            line.Children.Add(Glyph(ActionIcon.Controller, Theme.FontBody, device.IsActive ? Theme.Accent : device.IsConnected ? Theme.TextMuted : Theme.TextDisabled));
            line.Children.Add(new TextBlock
            {
                Text = $"{device.Number}. {AppController.DescribeDevice(device)}",
                FontSize = Theme.FontCaption,
                FontWeight = device.IsActive ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = device.IsActive ? Theme.Accent : device.IsConnected ? Theme.Text : Theme.TextDisabled,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(line);
        }

        var last = modal.Lines.Count > 0 ? modal.Lines[^1] : null;
        stack.Children.Add(new Border
        {
            Background = Theme.ModalInset,
            CornerRadius = Theme.RowRadius,
            Padding = new Thickness(Theme.SpaceM, Theme.Space(12), Theme.SpaceM, Theme.Space(12)),
            Margin = new Thickness(0, Theme.SpaceS, 0, 0),
            Child = new TextBlock
            {
                Text = last is null ? "Aperte um botão…" : $"#{last.Device} {AppController.DescribeInput(last, english: false)} → {last.Action?.ToString() ?? "nenhuma ação"}",
                FontSize = Theme.FontTitle,
                Foreground = Theme.Accent,
                TextWrapping = TextWrapping.Wrap,
            },
        });
        foreach (var line in modal.Lines.AsEnumerable().Reverse().Skip(1).Take(8))
            stack.Children.Add(new TextBlock { Text = $"#{line.Device} {AppController.DescribeInput(line, english: false)} → {line.Action?.ToString() ?? "nenhuma ação"}", FontSize = Theme.FontCaption, Foreground = Theme.TextMuted });
        if (modal.Notice is { } notice)
            stack.Children.Add(new TextBlock { Text = notice, FontSize = Theme.FontBody, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(Row($"Copiar relatório ({modal.Lines.Count} pressões)", ActionIcons.Glyph(ActionIcon.Copy), false, true, false, app.CopyControllerReport));
        return Panel(app, Header(modal), stack, 760);
    }
}
