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

    /// <summary>Rolagem do corpo do modal desenhado por último (o analógico direito rola diálogos e "Sobre", #175).</summary>
    private static WeakReference<ScrollViewer>? _body;

    /// <summary>Distância de um passo do analógico direito no corpo de um modal (pixels efetivos, antes da escala).</summary>
    private const double BodyScrollStep = 40;

    /// <summary>Rola o corpo do modal do topo em passos (negativo: para cima), no lugar e sem animação (a taxa já é contínua).</summary>
    public static void ScrollBody(int steps)
    {
        if (_body is null || !_body.TryGetTarget(out var scroll) || scroll.XamlRoot is null) return;
        var offset = Math.Clamp(scroll.VerticalOffset + (steps * Theme.Scaled(BodyScrollStep)), 0, scroll.ScrollableHeight);
        scroll.ChangeView(null, offset, null, disableAnimation: true);
    }

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
        if (modal is OnboardingModal onboarding)
        {
            // Boas-vindas (#231): tela cheia própria, sem painel fosco; refeita a cada mudança (poucos elementos).
            _retained = null;
            _shown = new WeakReference<Modal>(modal);
            return BuildOnboarding(app, onboarding);
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
            PromoModal promo => BuildPromo(app, promo),
            MappingWizardModal wizard => BuildMappingWizard(app, wizard),
            ControllerTestModal test => BuildControllerTest(app, test),
            ImagePreviewModal preview => BuildImagePreview(app, preview),
            PdfPreviewModal pdf => BuildPdfPreview(app, pdf),
            AudioPreviewModal audio => BuildAudioPreview(app, audio),
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
        var screen = $"{Theme.Viewport.Width:0}x{Theme.Viewport.Height:0}|{Theme.Layout.Tier}|{Theme.SolidSurfaces}|{Theme.Revision}|{modal.Title}|{modal.Subtitle}|{modal.Icon}";
        return modal switch
        {
            MenuModal menu => screen + "|m|" + string.Join("\u0001", menu.Items.Select(i => $"{i.Label}|{i.IsEnabled}|{i.DisabledReason}|{i.Detail}|{i.Section}|{i.Icon}|{i.IsDestructive}|{i.Placement}|{i.TileLabel}|{i.Value}")),
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

    /// <summary>Menus (#193): painel mais justo que o dos diálogos, para caber em 1280×720 sem rolar.</summary>
    private static double MenuPadding => Theme.Space(20);

    private static double PanelMargin => Theme.SpaceM;

    /// <summary>
    /// Largura fixa de cada classe de tamanho (#227, px lógicos antes da escala). Nunca depende do conteúdo em foco; a janela
    /// (720p, portátil, 4K) sempre limita (<see cref="PanelWidthFor"/>).
    /// </summary>
    private static double BaseWidth(ModalSize size) => size switch
    {
        ModalSize.Compact => 460,
        ModalSize.Medium => 540,
        ModalSize.Standard => 640,
        ModalSize.Wide => 960,
        _ => double.PositiveInfinity,
    };

    /// <summary>Largura real do painel: a da classe (escalada) ou, no máximo, a janela menos as margens; Fill ocupa tudo isso.</summary>
    public static double PanelWidthFor(ModalSize size) => Math.Min(Theme.Scaled(BaseWidth(size)), Theme.Viewport.Width - (2 * PanelMargin));

    /// <summary>
    /// Painel do modal: cabeçalho, corpo (rolável; a altura nunca passa da janela), a área de descrição (menus, se houver) e,
    /// embaixo, o aviso do rodapé e as legendas do controle em uso. A largura é fixa pela classe de tamanho (#227): mínimo =
    /// máximo, então nada no conteúdo a muda. <paramref name="scroll"/> false: o corpo já cabe (visualizações).
    /// </summary>
    private static Border Panel(AppController app, FrameworkElement header, UIElement body, ModalSize size, bool scroll = true, Action<Border>? footerSink = null, bool compact = false, bool fadedHints = false, UIElement? below = null)
    {
        var padding = compact ? MenuPadding : PanelPadding;
        var grid = new Grid { RowSpacing = compact ? Theme.Space(12) : Theme.SpaceM };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // Cabeçalho separado do corpo por um fio: o conteúdo que rola nunca encosta no título.
        var top = new StackPanel { Spacing = compact ? Theme.Space(12) : Theme.SpaceM };
        top.Children.Add(header);
        top.Children.Add(new Border { Height = Theme.Hairline.Top, Background = Theme.ModalDivider });
        grid.Children.Add(top);

        UIElement content = body;
        if (scroll)
        {
            // A opção focada cresce um pouco: a folga (dentro do conteúdo, que a rolagem não recorta) mantém as bordas dela.
            var inset = Theme.Scaled(8);
            var bodyScroll = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(inset, Theme.SpaceXs, inset, Theme.SpaceXs), Child = body },
                Margin = new Thickness(-inset, -Theme.SpaceXs, -inset, -Theme.SpaceXs),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            _body = new WeakReference<ScrollViewer>(bodyScroll);
            content = bodyScroll;
        }
        Grid.SetRow((FrameworkElement)content, 1);
        grid.Children.Add(content);

        if (below is FrameworkElement belowElement)
        {
            Grid.SetRow(belowElement, 2);
            grid.Children.Add(belowElement);
        }

        var footer = new Border { Child = Footer(app, fadedHints, statusBand: true) };
        _hintsFaded = fadedHints;
        footerSink?.Invoke(footer);
        Grid.SetRow(footer, 3);
        grid.Children.Add(footer);

        return new Border
        {
            Background = Theme.ModalPanel(),
            BorderBrush = Theme.ModalEdge,
            BorderThickness = Theme.Hairline,
            CornerRadius = Theme.ModalRadius,
            Padding = compact ? new Thickness(padding, padding - Theme.SpaceXs, padding, Theme.Space(14)) : new Thickness(padding, padding - Theme.SpaceXs, padding, Theme.Space(20)),
            Width = size == ModalSize.Fill ? Theme.Viewport.Width - (2 * PanelMargin) : PanelWidthFor(size),
            MaxHeight = Theme.Viewport.Height - (2 * PanelMargin),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(PanelMargin),
            Child = grid,
        };
    }

    /// <summary>Cabeçalho: ícone num selo com o tom do modal, título (id estável para o UIA) e a linha de contexto.</summary>
    private static Grid Header(string title, ActionIcon icon, string? subtitle = null, bool compact = false)
    {
        var grid = new Grid { ColumnSpacing = compact ? Theme.Space(12) : Theme.SpaceM };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (icon != ActionIcon.None)
        {
            var tone = ToneBrush(icon);
            var size = Theme.Scaled(compact ? 38 : 46);
            grid.Children.Add(new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(Theme.Scaled(compact ? 11 : 13)),
                Background = new SolidColorBrush(WithAlpha(tone.Color, 0x2E)),
                VerticalAlignment = VerticalAlignment.Top,
                Child = Glyph(icon, Theme.Font(compact ? 19 : 22), tone),
            });
        }
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = Theme.Space(2) };
        var heading = new TextBlock
        {
            Text = title,
            FontSize = compact ? Theme.Font(22) : Theme.FontTitle,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = compact ? 2 : 3,
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

    private static Grid Header(Modal modal, bool compact = false) => Header(modal.Title, modal.Icon, modal.Subtitle, compact);

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
    private static StackPanel Footer(AppController app, bool fadedHints = false, bool statusBand = false)
    {
        var footer = new StackPanel { Spacing = Theme.SpaceS };
        footer.Children.Add(new Border { Height = Theme.Hairline.Top, Background = Theme.ModalDivider, Margin = new Thickness(0, 0, 0, Theme.SpaceXs) });
        if (app.StatusMessage is { Length: > 0 } status)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS, MinHeight = StatusLineHeight };
            line.Children.Add(Glyph(ActionIcon.Info, Theme.FontBody, Theme.Accent));
            line.Children.Add(new TextBlock { Text = status, FontSize = Theme.FontBody, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            footer.Children.Add(line);
        }
        else if (statusBand)
        {
            // Um aviso que aparece (ex.: o fim de uma operação em segundo plano) ocupa uma linha já reservada: o painel não cresce (#227).
            footer.Children.Add(new Border { Height = StatusLineHeight });
        }
        // A legenda que nomeia a opção focada ("Confirmar: <opção>") é cortada com reticências em vez de quebrar a linha das legendas: o rodapé não muda de altura (#227).
        var inner = PanelWidthFor(app.TopModal?.Size ?? ModalSize.Standard) - (2 * MenuPadding);
        var bar = PromptBar(app.Prompts, PromptGlyphHeight, Theme.FontBody, KeyboardSecondaryPrompt(app), labelMaxWidth: inner * 0.55);
        if (fadedHints) FadeHints(bar);
        footer.Children.Add(bar);
        return footer;
    }

    /// <summary>Altura da linha de aviso do rodapé, reservada mesmo sem aviso (#227).</summary>
    private static double StatusLineHeight => Math.Ceiling(Theme.FontBody * 1.4);

    /// <summary>
    /// Teclado virtual nos portáteis: as legendas ficam nas essenciais (Selecionar, Apagar, Maiúsculas, Símbolos, Concluir,
    /// Cancelar); mover o cursor e ir ao início/fim continuam funcionando, só não ocupam uma segunda linha.
    /// </summary>
    private static Func<ControllerPrompt, bool>? KeyboardSecondaryPrompt(AppController app) =>
        app.TopModal is KeyboardModal && Theme.Layout.Tier == Core.Layout.LayoutTier.Compact
            ? p => p.Action is InputAction.PreviousRegion or InputAction.NextRegion or InputAction.PageUp or InputAction.PageDown
            : null;

    /// <summary>Se as legendas já estavam recolhidas no quadro anterior (o modal da imagem é refeito a cada quadro).</summary>
    private static bool _hintsFaded;

    /// <summary>Opacidade das legendas recolhidas: ainda legíveis de perto, sem disputar atenção com a imagem.</summary>
    private const double FadedHintOpacity = 0.3;

    /// <summary>
    /// Estado mínimo das legendas (#171): esmaecem sem mudar o tamanho do rodapé (a imagem não pula); Fechar continua em
    /// destaque. Só o quadro em que recolhem anima; qualquer entrada as mostra de novo na hora.
    /// </summary>
    private static void FadeHints(WrapPanel bar)
    {
        var animate = !_hintsFaded && !Theme.ReduceMotion;
        foreach (var child in bar.Children)
        {
            if (child is not StackPanel { Tag: ControllerPrompt prompt } chip || prompt.Action == InputAction.Back) continue;
            if (!animate)
            {
                chip.Opacity = FadedHintOpacity;
                continue;
            }
            chip.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(400) };
            chip.Loaded += (_, _) => chip.Opacity = FadedHintOpacity;
        }
    }

    private static double PromptGlyphHeight => Math.Round(Theme.FontBody * (Theme.Layout.Tier == Core.Layout.LayoutTier.Compact ? 1.6 : 1.8));

    /// <summary>Legendas (glifo do controle ou tecla do teclado + texto) que quebram linha; usadas também pelo rodapé da janela.</summary>
    public static WrapPanel PromptBar(IEnumerable<ControllerPrompt> prompts, double glyphHeight, double labelSize, Func<ControllerPrompt, bool>? skip = null, double labelMaxWidth = 0)
    {
        var bar = new WrapPanel { HorizontalSpacing = Theme.SpaceL, VerticalSpacing = Theme.SpaceS };
        foreach (var prompt in prompts)
        {
            if (skip?.Invoke(prompt) == true) continue;
            bar.Children.Add(PromptChip(prompt, glyphHeight, labelSize, labelMaxWidth));
        }
        return bar;
    }

    public static StackPanel PromptChip(ControllerPrompt prompt, double glyphHeight, double labelSize, double labelMaxWidth = 0)
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
        var label = new TextBlock { Text = prompt.Label, FontSize = labelSize, Foreground = Theme.Text, VerticalAlignment = VerticalAlignment.Center };
        if (labelMaxWidth > 0)
        {
            label.MaxWidth = labelMaxWidth;
            label.MaxLines = 1;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
        }
        chip.Children.Add(label);
        AutomationProperties.SetName(chip, prompt.AccessibilityText);
        chip.Tag = prompt;
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
    private static Border Row(string label, string glyph, bool focused, bool enabled, bool destructive, Action onTap, string? secondary = null, bool compact = false, string? trailing = null)
    {
        var fill = !focused ? Theme.Transparent : !enabled ? Theme.DisabledFill : destructive ? Theme.DangerFill : Theme.FocusFill;
        var ink = focused && enabled ? Theme.FocusText : !enabled ? Theme.TextDisabled : destructive ? Theme.Danger : Theme.Text;
        var iconInk = focused && enabled ? Theme.FocusText : !enabled ? Theme.TextDisabled : destructive ? Theme.Danger : Theme.Accent;
        if (focused && !enabled) ink = iconInk = Theme.TextMuted;

        var grid = new Grid { ColumnSpacing = Theme.Space(compact ? 12 : 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.Scaled(compact ? 24 : 28)) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (glyph.Length > 0) grid.Children.Add(Glyph(glyph, Theme.Font(compact ? 18 : 20), iconInk));

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock
        {
            Text = label,
            FontSize = compact ? Theme.FontBody : Theme.FontItem,
            FontWeight = focused ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = ink,
            TextWrapping = TextWrapping.Wrap,
        };
        if (focused) AutomationProperties.SetAutomationId(text, FocusedOptionId);
        texts.Children.Add(WeightStable(text, focused));
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
        else if (trailing is { Length: > 0 })
        {
            // Marca em texto ao lado do ícone (a alternativa atual de um seletor, #261): nunca só a cor.
            var mark = new TextBlock
            {
                Text = trailing,
                FontSize = Theme.FontCaption,
                FontWeight = FontWeights.SemiBold,
                Foreground = focused && enabled ? Theme.FocusText : Theme.Accent,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(mark, 2);
            grid.Children.Add(mark);
        }

        var row = new Border
        {
            Child = grid,
            Background = fill,
            CornerRadius = Theme.RowRadius,
            Padding = compact ? new Thickness(Theme.Space(12), Theme.Space(6), Theme.Space(12), Theme.Space(6)) : new Thickness(Theme.Space(14), Theme.Space(11), Theme.Space(14), Theme.Space(11)),
            Margin = new Thickness(0, 1, 0, 1),
            MinHeight = Theme.Scaled(compact ? 40 : 52),
        };
        if (focused)
        {
            row.RenderTransformOrigin = new Point(0.5, 0.5);
            row.RenderTransform = new ScaleTransform { ScaleX = Theme.ModalFocusScale, ScaleY = Theme.ModalFocusScale };
            KeepInView(row);
        }
        AutomationProperties.SetName(row, label + (enabled ? string.Empty : ", indisponível") + (destructive ? ", ação perigosa" : string.Empty) + (!destructive && trailing is { Length: > 0 } ? ", " + trailing : string.Empty));
        row.Tapped += (_, _) => onTap();
        return row;
    }

    /// <summary>
    /// Texto que ocupa o mesmo espaço focado (negrito, mais largo) e não focado (#227): um rótulo que quebra em duas linhas só
    /// quando ganha o foco faria a linha, e o painel, crescerem. A cópia em negrito, invisível, só mede.
    /// </summary>
    private static UIElement WeightStable(TextBlock text, bool focused)
    {
        if (focused) return text;
        var ghost = new TextBlock
        {
            Text = text.Text,
            FontSize = text.FontSize,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = text.TextWrapping,
            MaxLines = text.MaxLines,
            TextTrimming = text.TextTrimming,
            TextAlignment = text.TextAlignment,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        AutomationProperties.SetAccessibilityView(ghost, AccessibilityView.Raw);
        return new Grid { Children = { ghost, text }, HorizontalAlignment = text.HorizontalAlignment };
    }

    /// <summary>
    /// Área de texto de altura fixa (#227): todos os textos possíveis ficam no mesmo lugar e só o atual é visível, então a área
    /// já tem a altura do mais longo e mostrar outro nunca redimensiona o painel. <paramref name="minLines"/> linhas, no mínimo.
    /// </summary>
    private sealed class TextSlot
    {
        private readonly Dictionary<string, FrameworkElement> _blocks = [];
        private readonly Func<string, FrameworkElement> _create;

        public TextSlot(IEnumerable<string> reserved, Func<string, FrameworkElement> create)
        {
            _create = create;
            foreach (var text in reserved) Ensure(text).Opacity = 0;
        }

        public Grid Root { get; } = new();

        private FrameworkElement Ensure(string text)
        {
            if (_blocks.TryGetValue(text, out var block)) return block;
            block = _create(text);
            block.IsHitTestVisible = false;
            AutomationProperties.SetAccessibilityView(block, AccessibilityView.Raw); // o Narrador já ouve o anúncio do foco
            _blocks[text] = block;
            Root.Children.Add(block);
            return block;
        }

        public void Show(string text)
        {
            var current = text.Length > 0 ? Ensure(text) : null;
            foreach (var block in _blocks.Values) block.Opacity = ReferenceEquals(block, current) ? 1 : 0;
        }
    }

    /// <summary>Título de um grupo de opções, com a linha que o separa do grupo anterior.</summary>
    private static StackPanel SectionHeading(string? title, bool first)
    {
        var section = new StackPanel { Spacing = Theme.Space(2), Margin = new Thickness(Theme.Space(12), first ? 0 : Theme.Space(6), 0, Theme.Space(2)) };
        if (!first) section.Children.Add(new Border { Height = Theme.Hairline.Top, Background = Theme.ModalDivider, Margin = new Thickness(-Theme.Space(12), 0, 0, Theme.Space(2)) });
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

    /// <summary>
    /// Menu (#193): grade de ações rápidas no topo (blocos com ícone e rótulo curto, 2D) e, abaixo, a lista compacta das
    /// demais opções, em grupos. Configurações (#227): cada grupo tem título, a grade dos ajustes curtos (com o valor atual)
    /// e a lista dos demais. A descrição da opção em foco (o nome completo e o que faz um bloco; o detalhe de uma linha; ou por
    /// que está indisponível) fica numa área fixa entre a lista e o rodapé, com a altura da descrição mais longa do menu.
    /// Largura (<see cref="Modal.Size"/>) e altura nunca mudam com o foco (#227). Mantido na tela: mudar o foco troca só o
    /// bloco/linha que perde e o que ganha o foco e o texto da descrição.
    /// </summary>
    private static Border BuildMenu(AppController app, MenuModal menu, out Action update)
    {
        var stack = new StackPanel();
        var hasGrid = menu.Grids.Count > 0;
        // Títulos de grupo: em menus só de lista e nas Configurações; com a grade de ações rápidas no topo, só o fio (como o
        // menu de contexto do Windows 11).
        var titles = !hasGrid || menu.HasSectionGrids;
        var grids = new Dictionary<MenuGrid, Grid>();
        var positions = new int[menu.Items.Count];
        string? section = null;
        for (var i = 0; i < menu.Items.Count;)
        {
            var item = menu.Items[i];
            var grid = menu.GridOf(i);
            // Grupo novo: título (ou só o fio). Menu comum: logo abaixo da grade do topo sempre há o fio separando as duas partes.
            var heading = menu.HasSectionGrids || !hasGrid
                ? (i == 0 ? item.Section is not null : item.Section != section)
                : i != 0 && (i == menu.QuickCount || item.Section != section);
            if (heading) stack.Children.Add(SectionHeading(titles || (item.Section is not null && item.Section == menu.TitledSection) ? item.Section : null, first: i == 0));
            section = item.Section;
            if (grid is null)
            {
                positions[i] = stack.Children.Count;
                stack.Children.Add(MenuRow(app, menu, i));
                i++;
                continue;
            }
            var tiles = new Grid { ColumnSpacing = Theme.SpaceS, RowSpacing = Theme.SpaceS, Margin = new Thickness(0, menu.HasSectionGrids ? Theme.Space(4) : 0, 0, menu.HasSectionGrids ? Theme.Space(4) : 0) };
            for (var c = 0; c < grid.Columns; c++) tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var r = 0; r < grid.Rows; r++) tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var t = grid.Start; t < grid.End; t++) tiles.Children.Add(MenuTile(app, menu, t));
            stack.Children.Add(tiles);
            grids[grid] = tiles;
            i = grid.End;
        }
        if (menu.Items.Count == 0)
            stack.Children.Add(new TextBlock { Text = "Nenhuma opção.", FontSize = Theme.FontBody, Foreground = Theme.TextMuted });

        // Descrição fixa da opção em foco (#227): a altura já é a da descrição mais longa; uma linha, no mínimo, se houver alguma.
        var lineHeight = Math.Ceiling(Theme.FontCaption * 1.4);
        var description = new TextSlot(menu.AllDescriptions, text => menu.IconOnly ? IconReadout(text) : (FrameworkElement)new TextBlock { Text = text, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        UIElement? below = null;
        if (!menu.IsPicker && menu.AllDescriptions.Any())
        {
            description.Root.MinHeight = lineHeight;
            // Caixa própria, separada da lista que rola por trás: a descrição não parece parte dela.
            below = new Border
            {
                Background = Theme.ModalInset,
                CornerRadius = Theme.RowRadius,
                Padding = new Thickness(Theme.Space(12), Theme.Space(8), Theme.Space(12), Theme.Space(8)),
                Child = description.Root,
            };
        }
        description.Show(menu.Description);

        Border? footer = null;
        var card = Panel(app, Header(menu, compact: true), stack, menu.Size, footerSink: f => footer = f, compact: true, below: below);
        var shown = menu.FocusIndex;
        void Replace(int index)
        {
            if (index < 0 || index >= positions.Length) return;
            if (menu.GridOf(index) is { } grid) grids[grid].Children[index - grid.Start] = MenuTile(app, menu, index);
            else stack.Children[positions[index]] = MenuRow(app, menu, index);
        }
        update = () =>
        {
            var now = menu.FocusIndex;
            if (now != shown)
            {
                Replace(shown);
                Replace(now);
                shown = now;
                description.Show(menu.Description);
            }
            if (footer is not null) footer.Child = Footer(app, statusBand: true);
        };
        return card;
    }

    /// <summary>
    /// Leitura do ajuste em foco numa grade só de ícones (#293): "Nome: valor" em destaque e, embaixo, o que ele faz. Fica sempre no
    /// mesmo lugar e tem a altura do texto mais longo, então mover o foco nunca redimensiona o painel.
    /// </summary>
    private static StackPanel IconReadout(string text)
    {
        var lines = text.Split('\n', 2);
        var panel = new StackPanel { Spacing = Theme.Space(2) };
        panel.Children.Add(new TextBlock { Text = lines[0], FontSize = Theme.FontItem, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = lines.Length > 1 ? lines[1] : " ", FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    /// <summary>
    /// Bloco da grade: ícone grande e o rótulo curto visível embaixo (legível de longe; nada só em dica de mouse) e, num
    /// ajuste, o valor atual. Focado: preenchido (ciano; vermelho se perigoso; cinza se indisponível), texto escuro em negrito
    /// e um pouco maior. Perigoso: ícone e texto vermelhos. Indisponível: esmaecido. O Narrador ouve o rótulo por extenso,
    /// que já diz o valor.
    /// </summary>
    private static Border MenuTile(AppController app, MenuModal menu, int index)
    {
        var item = menu.Items[index];
        var grid = menu.GridOf(index)!;
        var focused = index == menu.FocusIndex;
        var enabled = item.IsEnabled;
        var destructive = item.IsDestructive;
        var fill = !focused ? Theme.ModalInset : !enabled ? Theme.DisabledFill : destructive ? Theme.DangerFill : Theme.FocusFill;
        var ink = focused && enabled ? Theme.FocusText : !enabled ? (focused ? Theme.TextMuted : Theme.TextDisabled) : destructive ? Theme.Danger : Theme.Text;
        var iconInk = focused && enabled ? Theme.FocusText : !enabled ? (focused ? Theme.TextMuted : Theme.TextDisabled) : destructive ? Theme.Danger : Theme.Accent;

        var content = new StackPanel { Spacing = Theme.Space(6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(Glyph(ActionIcons.Glyph(item.Icon), Theme.Font(menu.IconOnly ? 30 : 24), iconInk));
        if (menu.IconOnly) return IconTile(app, menu, index, content, fill, focused, enabled, destructive, grid);
        var text = new TextBlock
        {
            Text = item.TileLabel,
            FontSize = Theme.Font(16),
            FontWeight = focused ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = ink,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (focused)
        {
            AutomationProperties.SetAutomationId(text, FocusedOptionId);
            AutomationProperties.SetName(text, item.Label);
        }
        content.Children.Add(WeightStable(text, focused));
        if (item.Value is { Length: > 0 } value)
        {
            content.Spacing = Theme.Space(4);
            content.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = Theme.FontCaption,
                Foreground = focused && enabled ? Theme.FocusText : enabled ? Theme.Accent : ink,
                Opacity = focused && enabled ? 0.85 : 1,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.WrapWholeWords,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var tile = new Border
        {
            Child = content,
            Background = fill,
            CornerRadius = Theme.RowRadius,
            Padding = new Thickness(Theme.Space(4), Theme.Space(10), Theme.Space(4), Theme.Space(8)),
            MinHeight = Theme.Scaled(78),
            // Bloco indisponível focado: o preenchimento cinza é discreto, então o contorno ciano mostra onde está o foco.
            BorderBrush = focused && !enabled ? Theme.Accent : null,
            BorderThickness = focused && !enabled ? Theme.FocusRing : default,
        };
        Grid.SetRow(tile, grid.Row(index));
        Grid.SetColumn(tile, grid.Column(index));
        if (focused)
        {
            tile.RenderTransformOrigin = new Point(0.5, 0.5);
            tile.RenderTransform = new ScaleTransform { ScaleX = 1.05, ScaleY = 1.05 };
            KeepInView(tile);
        }
        AutomationProperties.SetName(tile, item.Label + (enabled ? string.Empty : ", indisponível") + (destructive ? ", ação perigosa" : string.Empty));
        tile.Tapped += (_, _) => app.PointerChooseModalOption(index);
        return tile;
    }

    /// <summary>
    /// Bloco só de ícone (#293): nenhum texto fixo. O foco é o preenchimento cheio (ciano; vermelho se perigoso; cinza se
    /// indisponível) mais o leve aumento, e o nome, o valor e a descrição aparecem na leitura fixa do painel. O Narrador ouve o
    /// rótulo por extenso (nome e valor) e quem usa o mouse vê o mesmo numa dica.
    /// </summary>
    private static Border IconTile(AppController app, MenuModal menu, int index, UIElement content, Brush fill, bool focused, bool enabled, bool destructive, MenuGrid grid)
    {
        var item = menu.Items[index];
        var tile = new Border
        {
            Child = content,
            Background = fill,
            CornerRadius = Theme.RowRadius,
            Padding = new Thickness(Theme.Space(4)),
            MinHeight = Theme.Scaled(72),
            BorderBrush = focused && !enabled ? Theme.Accent : null,
            BorderThickness = focused && !enabled ? Theme.FocusRing : default,
        };
        Grid.SetRow(tile, grid.Row(index));
        Grid.SetColumn(tile, grid.Column(index));
        if (focused)
        {
            tile.RenderTransformOrigin = new Point(0.5, 0.5);
            tile.RenderTransform = new ScaleTransform { ScaleX = 1.05, ScaleY = 1.05 };
            AutomationProperties.SetAutomationId(tile, FocusedOptionId);
            KeepInView(tile);
        }
        var name = item.Label + (enabled ? string.Empty : ", indisponível") + (destructive ? ", ação perigosa" : string.Empty);
        AutomationProperties.SetName(tile, name);
        ToolTipService.SetToolTip(tile, item.Label);
        tile.Tapped += (_, _) => app.PointerChooseModalOption(index);
        return tile;
    }

    /// <summary>Linha compacta da lista; o que ela faz (ou por que está indisponível) aparece na área de descrição fixa do painel.</summary>
    private static Border MenuRow(AppController app, MenuModal menu, int index)
    {
        var item = menu.Items[index];
        var focused = index == menu.FocusIndex;
        // Seletor de opções (#261): a descrição de cada alternativa fica sob ela (todas à vista) e a atual leva a marca "atual".
        return Row(item.Label, ActionIcons.Glyph(item.Icon), focused, item.IsEnabled, item.IsDestructive, () => app.PointerChooseModalOption(index),
            menu.IsPicker ? item.Detail : null, compact: true, trailing: item.Value);
    }

    // ---------- Diálogos ----------

    /// <summary>Linhas "rótulo: valor" num quadro discreto (informações do diálogo, do Sobre e do assistente).</summary>
    private static Border InfoLines(IReadOnlyList<(string Label, string Value)> lines, double fontSize, IReadOnlyDictionary<string, IReadOnlyList<string>>? reserve = null, int reservedRows = 0)
    {
        var grid = new Grid { ColumnSpacing = Theme.SpaceM, RowSpacing = Theme.SpaceS };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < lines.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = lines[i].Label, FontSize = fontSize, Foreground = Theme.TextMuted, MaxWidth = Theme.Scaled(260), TextWrapping = TextWrapping.Wrap };
            var value = new TextBlock { Text = lines[i].Value, FontSize = fontSize, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };
            FrameworkElement cell = value;
            if (reserve is not null && reserve.TryGetValue(lines[i].Label, out var alternatives))
            {
                // Textos alternativos (ex.: a descrição de cada formato): invisíveis, ocupam o mesmo lugar e o painel já tem a altura do mais alto (#227).
                var stacked = new Grid();
                foreach (var alternative in alternatives.Where(a => a != lines[i].Value))
                {
                    var ghost = new TextBlock { Text = alternative, FontSize = fontSize, TextWrapping = TextWrapping.Wrap, Opacity = 0, IsHitTestVisible = false };
                    AutomationProperties.SetAccessibilityView(ghost, AccessibilityView.Raw);
                    stacked.Children.Add(ghost);
                }
                stacked.Children.Add(value);
                cell = stacked;
            }
            Grid.SetRow(label, i);
            Grid.SetRow(cell, i);
            Grid.SetColumn(cell, 1);
            grid.Children.Add(label);
            grid.Children.Add(cell);
        }
        // Linhas ainda por vir (#227): ocupam o lugar delas, invisíveis, até aparecerem.
        for (var i = lines.Count; i < reservedRows; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var placeholder = new TextBlock { Text = " ", FontSize = fontSize, Opacity = 0, IsHitTestVisible = false };
            AutomationProperties.SetAccessibilityView(placeholder, AccessibilityView.Raw);
            Grid.SetRow(placeholder, i);
            Grid.SetColumnSpan(placeholder, 2);
            grid.Children.Add(placeholder);
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

    /// <summary>
    /// QR Code (#223): módulos escuros num fundo branco com a zona de silêncio de 4 módulos, em pixels inteiros por módulo
    /// (nítido para a câmera). Cada trecho escuro de uma linha é um retângulo da mesma geometria (sem frestas entre eles).
    /// </summary>
    private static Border QrImage(bool[,] modules)
    {
        var n = modules.GetLength(0);
        var target = Math.Min(Theme.Scaled(280), Theme.Viewport.Height * 0.3); // cabe inteiro com as opções em 1280×720
        var module = Math.Max(3, Math.Floor(target / (n + 8)));
        var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                if (!modules[y, x]) continue;
                var start = x;
                while (x + 1 < n && modules[y, x + 1]) x++;
                geometry.Children.Add(new RectangleGeometry { Rect = new Rect(start * module, y * module, (x - start + 1) * module, module) });
            }
        var path = new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Fill = new SolidColorBrush(Microsoft.UI.Colors.Black), Width = n * module, Height = n * module };
        var code = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Padding = new Thickness(4 * module),
            CornerRadius = new CornerRadius(Theme.Scaled(6)),
            HorizontalAlignment = HorizontalAlignment.Center,
            UseLayoutRounding = true,
            Child = path,
        };
        AutomationProperties.SetName(code, "QR Code para conectar o celular");
        return code;
    }

    private static string CheckGlyph(bool on) => char.ConvertFromUtf32(on ? 0xE73A : 0xE739);

    private static Border BuildDialog(AppController app, DialogModal dialog, out Action update)
    {
        var stack = new StackPanel { Spacing = Theme.Space(12) };
        if (dialog.QrModules is { } qr) stack.Children.Add(QrImage(qr));
        if (dialog.Lines.Count > 0) stack.Children.Add(InfoLines(dialog.Lines, Theme.FontBody, dialog.LineReserve, dialog.ReservedRows));
        if (dialog.Progress is { } progress) stack.Children.Add(ProgressBar(progress));
        if (dialog.Message is { } message)
            stack.Children.Add(new TextBlock { Text = message, FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        var options = new StackPanel { Margin = new Thickness(0, Theme.SpaceXs, 0, 0) };
        for (var i = 0; i < dialog.Options.Count; i++) options.Children.Add(DialogRow(app, dialog, i));
        if (dialog.Options.Count > 0) stack.Children.Add(options);
        Border? footer = null;
        var card = Panel(app, Header(dialog), stack, dialog.Size, footerSink: f => footer = f);
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
            if (footer is not null) footer.Child = Footer(app, statusBand: true);
        };
        return card;
    }

    private static Border DialogRow(AppController app, DialogModal dialog, int index)
    {
        var option = dialog.Options[index];
        var glyph = option.Kind == DialogOptionKind.Toggle ? CheckGlyph(option.IsChecked) : ActionIcons.Glyph(option.Icon);
        return Row(option.Label, glyph, index == dialog.FocusIndex, true, option.IsDestructive, () => app.PointerChooseModalOption(index));
    }

    // ---------- Mais da equipe ----------

    private static Border BuildPromo(AppController app, PromoModal promo)
    {
        var stack = new StackPanel { Spacing = Theme.SpaceM };
        if (promo.Cards.Count == 0)
        {
            // Catálogo vazio: nada quebrado na tela, só o aviso e Fechar.
            stack.Children.Add(new TextBlock { Text = "Nenhum aplicativo da equipe por enquanto. Volte mais tarde.", FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            // Linhas de duas colunas: um novo aplicativo no catálogo entra na próxima posição, sem mexer na tela.
            var cards = new Grid { ColumnSpacing = Theme.SpaceM, RowSpacing = Theme.SpaceM };
            for (var c = 0; c < PromoModal.Columns; c++) cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var r = 0; r < (promo.Cards.Count + PromoModal.Columns - 1) / PromoModal.Columns; r++) cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < promo.Cards.Count; i++)
            {
                var card = PromoCardView(app, promo, i);
                Grid.SetRow(card, i / PromoModal.Columns);
                Grid.SetColumn(card, i % PromoModal.Columns);
                cards.Children.Add(card);
            }
            stack.Children.Add(cards);
        }
        stack.Children.Add(Row("Fechar", ActionIcons.Glyph(ActionIcon.Close), promo.CloseFocused, true, false, () => app.PointerChoosePromo(promo.Cards.Count)));
        return Panel(app, Header(promo), stack, promo.Size);
    }

    /// <summary>Um aplicativo: logo, nome, o que faz e o botão que abre o site no navegador (nada é baixado por aqui).</summary>
    private static Border PromoCardView(AppController app, PromoModal promo, int index)
    {
        var info = promo.Cards[index];
        var focused = !promo.CloseFocused && promo.FocusIndex == index;
        var content = new StackPanel { Spacing = Theme.SpaceS };
        if (Branding.Asset(info.LogoFile) is { } logo)
        {
            var image = new Image { Source = logo, Height = Theme.Scaled(72), HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
            AutomationProperties.SetName(image, info.Name);
            content.Children.Add(image);
        }
        content.Children.Add(new TextBlock { Text = info.Name, FontSize = Theme.FontTitle, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text });
        if (info.Platform.Length > 0) content.Children.Add(new TextBlock { Text = info.Platform, FontSize = Theme.FontCaption, Foreground = Theme.TextMuted });
        content.Children.Add(new TextBlock { Text = info.Tagline, FontSize = Theme.FontBody, Foreground = Theme.Accent, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = info.Description, FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new Border { Margin = new Thickness(0, Theme.SpaceXs, 0, 0), Child = Row(info.ActionLabel, ActionIcons.Glyph(ActionIcon.Open), focused, true, false, () => app.PointerChoosePromo(index)) });
        var card = new Border
        {
            Background = Theme.ModalInset,
            CornerRadius = Theme.RowRadius,
            Padding = new Thickness(Theme.SpaceM),
            BorderBrush = focused ? Theme.Accent : Theme.Transparent,
            BorderThickness = Theme.FocusRing,
            Child = content,
        };
        AutomationProperties.SetName(card, $"{info.Name}: {info.Tagline}");
        return card;
    }

    private static Border BuildAbout(AppController app, AboutModal about)
    {
        var stack = new StackPanel { Spacing = Theme.SpaceM };
        if (Branding.LogoFor(Theme.IsDark) is { } logo)
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
        return Panel(app, Header(about.Title, about.Icon, "Versão " + about.Version), stack, about.Size);
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
        var card = Panel(app, Header(modal), stack, modal.Size, footerSink: f => footer = f);

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
            if (footer is not null) footer.Child = Footer(app, statusBand: true);
        };
        return card;

        void Refresh(int r, int k)
        {
            if (r < 0 || r >= positions.Length || k < 0 || k >= positions[r].Length) return;
            grid.Children[positions[r][k]] = KeyCell(app, kb, r, k, columns[r][k]);
        }
    }

    private static double SuggestionChipHeight => Math.Ceiling(Theme.FontBody * 1.4) + (2 * Theme.SpaceS) + (2 * Theme.Hairline.Top);

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
            // Caminhos (Ir para caminho) passam de uma linha: duas ficam reservadas, digitar não redimensiona o teclado (#227).
            MinHeight = Theme.Space(24) + (Math.Ceiling(Theme.FontItem * 1.4) * (kb.Kind == TextFieldKind.Path ? 2 : 1)) + (2 * Theme.FocusRing.Top),
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
        // Erro de validação: símbolo e texto (nunca só a cor), e o campo continua aberto para corrigir. A linha do erro fica
        // reservada mesmo sem erro: a mensagem aparecer e sumir nunca muda a altura do teclado (#227).
        var errorLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS, MinHeight = StatusLineHeight };
        if (kb.ErrorMessage is { } error)
        {
            errorLine.Children.Add(Glyph(ActionIcon.Error, Theme.FontBody, Theme.Danger));
            errorLine.Children.Add(new TextBlock { Text = error, FontSize = Theme.FontBody, Foreground = Theme.Danger, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        }
        stack.Children.Add(errorLine);

        var gap = Theme.Scaled(6);
        // Com sugestões ligadas, a faixa tem lugar reservado mesmo vazia: ela aparecer ao digitar nunca muda a altura (#227).
        if (kb.SuggestionSource is not null && kb.Suggestions.Count == 0)
            stack.Children.Add(new Border { Height = SuggestionChipHeight, Margin = new Thickness(0, Theme.SpaceS, 0, 0) });
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
                    MinHeight = SuggestionChipHeight,
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
            Background = focused ? (enabled ? Theme.FocusFill : Theme.DisabledFill) : key.IsFunction ? Theme.KeyFunctionFill : Theme.KeyFill,
            // Só a tecla focada tem contorno: Concluir se distingue pelo texto em destaque, nunca por algo parecido com o foco.
            BorderBrush = focused ? Theme.Text : Theme.Transparent,
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
        // Passos e avisos ocupam lugares reservados (#227): trocar de passo, aparecer um aviso ou chegar à revisão não redimensiona o assistente.
        var bodyLine = Math.Ceiling(Theme.FontBody * 1.4);
        stack.Children.Add(new TextBlock { Text = headline, FontSize = Theme.FontTitle, Foreground = Theme.Accent, TextWrapping = TextWrapping.Wrap, MinHeight = Math.Ceiling(Theme.FontTitle * 1.4) });
        stack.Children.Add(new TextBlock { Text = detail, FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap, MinHeight = 3 * bodyLine });
        stack.Children.Add(new TextBlock { Text = wizard.Feedback ?? string.Empty, FontSize = Theme.FontBody, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, MinHeight = bodyLine });

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
            stack.Children.Add(new Border { MinHeight = WizardBottomHeight, Child = options });
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                MinHeight = WizardBottomHeight,
                Text = "Teclado: Esc cancela sem salvar · ← refaz o passo anterior · Enter pula um passo opcional. Sem nenhuma entrada por 20 s, a configuração é cancelada.",
                FontSize = Theme.FontCaption,
                Foreground = Theme.TextMuted,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        return Panel(app, Header($"Configurar {wizard.DeviceName}", modal.Icon, modal.Device.Name != wizard.DeviceName ? modal.Device.Name : null), stack, modal.Size);
    }

    /// <summary>Área de baixo do assistente: as três opções da revisão ou a dica do teclado, com o mesmo espaço (#227).</summary>
    private static double WizardBottomHeight => 3 * (Theme.Scaled(52) + 2);

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
        var captionLine = Math.Ceiling(Theme.FontCaption * 1.4);
        stack.Children.Add(SectionHeading($"Controles ({devices.Count})", first: false));
        // Lugares reservados (#227): conectar um controle, a lista de pressões crescer ou um aviso aparecer não redimensiona a tela.
        var deviceList = new StackPanel { MinHeight = 2 * (captionLine + Theme.SpaceS) };
        stack.Children.Add(deviceList);
        if (devices.Count == 0)
            deviceList.Children.Add(new TextBlock { Text = "Nenhum controle detectado. Conecte um controle (USB, Bluetooth ou receptor).", FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
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
            deviceList.Children.Add(line);
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
        var history = new StackPanel { MinHeight = 8 * captionLine };
        foreach (var line in modal.Lines.AsEnumerable().Reverse().Skip(1).Take(8))
            history.Children.Add(new TextBlock { Text = $"#{line.Device} {AppController.DescribeInput(line, english: false)} → {line.Action?.ToString() ?? "nenhuma ação"}", FontSize = Theme.FontCaption, Foreground = Theme.TextMuted, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(history);
        stack.Children.Add(new TextBlock { Text = modal.Notice ?? string.Empty, FontSize = Theme.FontBody, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, MinHeight = Math.Ceiling(Theme.FontBody * 1.4) });
        stack.Children.Add(Row($"Copiar relatório ({modal.Lines.Count} pressões)", ActionIcons.Glyph(ActionIcon.Copy), false, true, false, app.CopyControllerReport));
        return Panel(app, Header(modal), stack, modal.Size);
    }
}
