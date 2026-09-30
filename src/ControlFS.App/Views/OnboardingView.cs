using ControlFS.App.Controls;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.Prompts;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ControlFS.App.Views;

/// <summary>
/// Boas-vindas em tela cheia (#231): logo e progresso no topo; à esquerda o título e a explicação do passo, à direita o
/// conteúdo (botões do controle em uso, ajustes com efeito imediato ou as opções); embaixo as legendas, que também podem
/// ser tocadas. Mesmos tokens do tema (claro/escuro, destaque) e das linhas de opção dos modais. Se algo falhar ao desenhar,
/// as boas-vindas são puladas: nunca prendem o app.
/// </summary>
public static partial class ModalView
{
    /// <summary>Largura dos botões da janela no topo direito (barra de título do tema): as boas-vindas não desenham embaixo deles.</summary>
    public static double CaptionReserve { get; set; }

    private static Grid? BuildOnboarding(AppController app, OnboardingModal modal)
    {
        try
        {
            return OnboardingScreen(app, modal);
        }
        catch (Exception ex)
        {
            AppLog.Info($"Boas-vindas não desenhadas ({ex.GetType().Name}: {ex.Message}); puladas");
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(app.SkipOnboarding);
            return null;
        }
    }

    private static Grid OnboardingScreen(AppController app, OnboardingModal modal)
    {
        var compact = Theme.Layout.Tier == Core.Layout.LayoutTier.Compact;
        var margin = compact ? Theme.SpaceL : Theme.SpaceXl;
        var root = new Grid { Background = Theme.Background, Padding = new Thickness(margin, Theme.SpaceL, margin, Theme.SpaceM), RowSpacing = compact ? Theme.SpaceM : Theme.SpaceL };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Tag = CardTag;

        // Topo: logo à esquerda; passo e pontos de progresso à direita.
        var top = new Grid { Margin = new Thickness(0, 0, CaptionReserve, 0) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (Branding.LogoFor(Theme.IsDark) is { } logo)
        {
            // Tema claro: a variante com o nome escuro, sem placa atrás (a mesma do cabeçalho).
            var image = new Image { Source = logo, Height = Theme.Layout.LogoHeight, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
            AutomationProperties.SetName(image, "ControlFS");
            top.Children.Add(image);
        }
        var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Theme.SpaceS, VerticalAlignment = VerticalAlignment.Center };
        progress.Children.Add(new TextBlock
        {
            Text = $"Passo {modal.StepIndex + 1} de {modal.StepCount}",
            FontSize = Theme.FontBody,
            Foreground = Theme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Theme.SpaceS, 0),
        });
        for (var i = 0; i < modal.StepCount; i++)
        {
            var current = i == modal.StepIndex;
            progress.Children.Add(new Border
            {
                Width = Theme.Scaled(current ? 28 : 10),
                Height = Theme.Scaled(10),
                CornerRadius = new CornerRadius(Theme.Scaled(5)),
                Background = current || i < modal.StepIndex ? Theme.Accent : Theme.Border,
                Opacity = i < modal.StepIndex ? 0.55 : 1,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        AutomationProperties.SetName(progress, $"Passo {modal.StepIndex + 1} de {modal.StepCount}");
        Grid.SetColumn(progress, 1);
        top.Children.Add(progress);
        root.Children.Add(top);

        // Centro: texto do passo e o conteúdo, lado a lado (empilhados numa janela estreita).
        var narrow = Theme.Viewport.Width < 1000;
        // Largura fixa (a janela menos as margens, até 1240): as colunas em estrela precisam de uma largura definida, e o
        // conteúdo nunca passa da borda nem se desloca de um passo para o outro.
        var body = new Grid
        {
            Width = Math.Min(Theme.Scaled(1240), Theme.Viewport.Width - (2 * margin)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnSpacing = Theme.Space(56),
            RowSpacing = Theme.SpaceL,
        };
        if (narrow)
        {
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        }
        var intro = OnboardingIntro(modal, compact);
        body.Children.Add(intro);
        var content = OnboardingContent(app, modal, compact);
        if (narrow) Grid.SetRow(content, 1);
        else Grid.SetColumn(content, 1);
        body.Children.Add(content);
        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // Rodapé: as legendas do contexto (Escolher, Passo anterior, Próximo passo, Pular); tocar numa faz o mesmo.
        var footer = new Border
        {
            BorderBrush = Theme.Border,
            BorderThickness = new Thickness(0, Theme.Hairline.Top, 0, 0),
            Padding = new Thickness(0, Theme.SpaceM, 0, 0),
        };
        var bar = PromptBar(app.Prompts, PromptGlyphHeight, Theme.FontBody);
        bar.HorizontalSpacing = compact ? Theme.SpaceXl : Theme.Space(52);
        foreach (var child in bar.Children)
            if (child is FrameworkElement { Tag: ControllerPrompt prompt } chip) chip.Tapped += (_, _) => app.Handle(prompt.Action);
        footer.Child = bar;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private static StackPanel OnboardingIntro(OnboardingModal modal, bool compact)
    {
        var intro = new StackPanel { Spacing = Theme.SpaceM, VerticalAlignment = VerticalAlignment.Center };
        var icon = modal.Step switch
        {
            OnboardingStep.Controls => ActionIcon.Controller,
            OnboardingStep.Theme => ActionIcon.Theme,
            OnboardingStep.Basics => ActionIcon.Settings,
            OnboardingStep.Privacy => ActionIcon.Password,
            OnboardingStep.Tutorial => ActionIcon.Tutorial,
            _ => ActionIcon.Help,
        };
        var size = Theme.Scaled(compact ? 56 : 72);
        intro.Children.Add(new Border
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(Theme.Scaled(compact ? 16 : 20)),
            Background = Theme.AccentSoft,
            Child = Glyph(icon, Theme.Font(compact ? 26 : 34), Theme.Accent),
        });
        var title = new TextBlock
        {
            Text = modal.StepTitle,
            FontSize = Theme.Font(compact ? 32 : 44),
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(title, TitleId);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        intro.Children.Add(title);
        intro.Children.Add(new TextBlock
        {
            Text = modal.StepBody,
            FontSize = compact ? Theme.FontBody : Theme.FontItem,
            Foreground = Theme.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = Math.Round((compact ? Theme.FontBody : Theme.FontItem) * 1.45),
        });
        return intro;
    }

    private static StackPanel OnboardingContent(AppController app, OnboardingModal modal, bool compact)
    {
        var content = new StackPanel { Spacing = Theme.SpaceM, VerticalAlignment = VerticalAlignment.Center };
        if (modal.ControlLegend.Count > 0)
        {
            // Botões do controle em uso (ou teclas): duas colunas, glifos grandes para ler de longe.
            var legend = new Grid { ColumnSpacing = Theme.SpaceL, RowSpacing = compact ? Theme.SpaceS : Theme.Space(14) };
            legend.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            legend.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var glyph = Math.Round(Theme.FontBody * (compact ? 1.8 : 2.2));
            for (var i = 0; i < modal.ControlLegend.Count; i++)
            {
                if (i % 2 == 0) legend.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var hint = modal.ControlLegend[i];
                var chip = PromptChip(app.PromptProvider.For(hint.Action, hint.Label), glyph, compact ? Theme.FontBody : Theme.FontItem);
                Grid.SetRow(chip, i / 2);
                Grid.SetColumn(chip, i % 2);
                legend.Children.Add(chip);
            }
            content.Children.Add(new Border
            {
                Background = Theme.SurfaceRaised,
                BorderBrush = Theme.Border,
                BorderThickness = Theme.Hairline,
                CornerRadius = Theme.ModalRadius,
                Padding = new Thickness(compact ? Theme.SpaceM : Theme.SpaceL),
                Child = legend,
            });
        }
        else if (modal.Step == OnboardingStep.Welcome)
        {
            content.Children.Add(Feature(ActionIcon.Controller, "Feito para o controle", "Xbox, PlayStation, Nintendo e genéricos; teclado e mouse também funcionam.", compact));
            content.Children.Add(Feature(ActionIcon.Extract, "Extrator embutido", "ZIP, 7z, RAR e mais; nada é gravado fora da pasta de destino.", compact));
            content.Children.Add(Feature(ActionIcon.Password, "Tudo local", "Sem conta e sem telemetria.", compact));
        }
        // Folga dos lados: a opção em foco cresce um pouco (a mesma escala dos modais) e não pode encostar na borda.
        var inset = Theme.Scaled(10);
        var options = new StackPanel { Spacing = compact ? 0 : Theme.Space(2), Padding = new Thickness(inset, 0, inset, 0) };
        for (var i = 0; i < modal.Options.Count; i++)
        {
            var option = modal.Options[i];
            var focused = i == modal.FocusIndex;
            var index = i;
            options.Children.Add(Row(option.Label, ActionIcons.Glyph(option.Icon), focused, enabled: true, destructive: false,
                () => app.PointerChooseModalOption(index), compact: compact));
        }
        content.Children.Add(options);
        // O que a opção em foco faz, no tamanho do texto normal (legível de longe), sempre no mesmo lugar.
        if (modal.Options.Any(o => o.Detail is not null))
            content.Children.Add(new TextBlock
            {
                Text = modal.FocusedOption?.Detail ?? string.Empty,
                FontSize = Theme.FontBody,
                Foreground = Theme.TextMuted,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = Math.Ceiling(Theme.FontBody * 2.8),
                Margin = new Thickness(Theme.Space(14), 0, Theme.Space(14), 0),
            });
        return content;
    }

    private static Grid Feature(ActionIcon icon, string title, string text, bool compact)
    {
        var grid = new Grid { ColumnSpacing = Theme.SpaceM };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var size = Theme.Scaled(compact ? 40 : 48);
        grid.Children.Add(new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Theme.Scaled(12)),
            Background = Theme.SurfaceRaised,
            VerticalAlignment = VerticalAlignment.Top,
            Child = Glyph(icon, Theme.Font(compact ? 18 : 22), Theme.Accent),
        });
        var texts = new StackPanel { Spacing = Theme.Space(2), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, FontSize = compact ? Theme.FontBody : Theme.FontItem, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(new TextBlock { Text = text, FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        return grid;
    }
}
