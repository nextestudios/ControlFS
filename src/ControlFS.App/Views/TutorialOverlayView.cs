using ControlFS.App.Controls;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace ControlFS.App.Views;

/// <summary>
/// Camada do tutorial guiado (#231), acima da tela e dos modais: escurece tudo em volta da região do passo (conteúdo, barra
/// superior ou rodapé), contorna essa região com o anel de destaque e ancora ao lado dela um balão com o passo, o botão exato
/// do controle em uso e as saídas (voltar passo, pular). Não recebe toques fora do balão: a tela por baixo continua sendo
/// usada de verdade. Com um modal aberto, não escurece nada e o balão vai para um canto. Só layout (nada por quadro).
/// </summary>
internal sealed class TutorialOverlayView
{
    private readonly AppController _app;
    private readonly Func<TutorialTarget, FrameworkElement?> _resolve;
    private readonly Canvas _canvas = new() { IsHitTestVisible = true };
    private readonly Rectangle[] _dim = [new(), new(), new(), new()];
    private readonly Border _ring = new() { IsHitTestVisible = false };
    private Border? _card;
    private string? _key;
    private TutorialTarget _target;

    public TutorialOverlayView(AppController app, Func<TutorialTarget, FrameworkElement?> resolve)
    {
        _app = app;
        _resolve = resolve;
        Root = new Grid { Visibility = Visibility.Collapsed };
        foreach (var dim in _dim)
        {
            dim.IsHitTestVisible = false;
            _canvas.Children.Add(dim);
        }
        _canvas.Children.Add(_ring);
        Root.Children.Add(_canvas);
        Root.SizeChanged += (_, _) => Position();
    }

    public Grid Root { get; }

    public void Render()
    {
        try
        {
            RenderCore();
        }
        catch (Exception ex)
        {
            // O tutorial nunca derruba a tela: sem conseguir desenhar, ele é encerrado.
            AppLog.Info($"Tutorial não desenhado ({ex.GetType().Name}: {ex.Message}); encerrado");
            Root.Visibility = Visibility.Collapsed;
            Root.DispatcherQueue.TryEnqueue(_app.SkipTutorial);
        }
    }

    private void RenderCore()
    {
        if (_app.TutorialView is not { } card)
        {
            Root.Visibility = Visibility.Collapsed;
            _key = null;
            return;
        }
        Root.Visibility = Visibility.Visible;
        var modal = _app.TopModal is not null;
        _target = modal ? TutorialTarget.Modal : card.Target;
        var prompts = card.Actions.Select(a => _app.PromptProvider.For(a.Action, a.Label)).ToList();
        var options = _app.PromptProvider.For(InputAction.ToggleSelection, "Opções do tutorial");
        var key = $"{card.Number}|{card.Title}|{_target}|{Theme.Revision}|{Theme.Viewport}|{Theme.Layout.Tier}|{string.Join(",", prompts.Select(p => p.Key + p.Button))}|{options.Key}";
        if (key != _key || _card is null)
        {
            _key = key;
            if (_card is not null) _canvas.Children.Remove(_card);
            _card = BuildCard(card, prompts, options);
            _canvas.Children.Add(_card);
        }
        var dim = Theme.Scrim;
        foreach (var rect in _dim) rect.Fill = dim;
        Position();
        // O layout desta passagem ainda não mediu a região nova: confere de novo depois dele.
        Root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Position);
    }

    private Border BuildCard(TutorialCard card, IReadOnlyList<Application.Prompts.ControllerPrompt> prompts, Application.Prompts.ControllerPrompt options)
    {
        var compact = Theme.Layout.Tier == Core.Layout.LayoutTier.Compact;
        var stack = new StackPanel { Spacing = Theme.SpaceS };
        stack.Children.Add(new TextBlock
        {
            Text = $"TUTORIAL · PASSO {card.Number} DE {card.Count}",
            FontSize = Theme.FontBody,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 40,
            Foreground = Theme.Accent,
        });
        var title = new TextBlock { Text = card.Title, FontSize = compact ? Theme.FontItem : Theme.Font(24), FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        stack.Children.Add(title);
        stack.Children.Add(new TextBlock { Text = card.Instruction, FontSize = Theme.FontBody, Foreground = Theme.TextMuted, TextWrapping = TextWrapping.Wrap });
        var glyph = Math.Round(Theme.FontBody * (compact ? 1.8 : 2.1));
        var actions = ModalView.PromptBar(prompts, glyph, Theme.FontItem);
        actions.Margin = new Thickness(0, Theme.SpaceXs, 0, Theme.SpaceXs);
        stack.Children.Add(actions);
        stack.Children.Add(new Border { Height = Theme.Hairline.Top, Background = Theme.ModalDivider });

        // Saídas: pelo controle, Marcar abre as opções (continuar, voltar passo, pular); com o mouse, os dois botões.
        var exits = new WrapPanel { HorizontalSpacing = Theme.SpaceM, VerticalSpacing = Theme.SpaceXs };
        var chip = ModalView.PromptChip(options, Math.Round(Theme.FontBody * 1.5), Theme.FontBody);
        chip.Tapped += (_, _) => _app.Handle(InputAction.ToggleSelection);
        exits.Children.Add(chip);
        if (card.Number > 1) exits.Children.Add(LinkButton("Voltar passo", _app.PreviousTutorialStep));
        exits.Children.Add(LinkButton("Pular tutorial", _app.SkipTutorial));
        stack.Children.Add(exits);

        var border = new Border
        {
            Child = stack,
            Background = Theme.ModalPanel(),
            BorderBrush = Theme.Accent,
            BorderThickness = Theme.FocusRing,
            CornerRadius = new CornerRadius(Theme.Scaled(18)),
            Padding = new Thickness(Theme.Space(20), Theme.SpaceM, Theme.Space(20), Theme.SpaceM),
            Width = Math.Min(Theme.Scaled(compact ? 400 : 460), Theme.Viewport.Width - (2 * Theme.SpaceM)),
        };
        AutomationProperties.SetName(border, $"Tutorial, passo {card.Number} de {card.Count}: {card.Title}. {card.Instruction}");
        return border;
    }

    private static Border LinkButton(string text, Action onTap)
    {
        var button = new Border
        {
            Background = Theme.SurfaceRaised,
            BorderBrush = Theme.Border,
            BorderThickness = Theme.Hairline,
            CornerRadius = Theme.Radius,
            Padding = new Thickness(Theme.SpaceS + Theme.SpaceXs, Theme.SpaceXs, Theme.SpaceS + Theme.SpaceXs, Theme.SpaceXs),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = Theme.FontBody, Foreground = Theme.Text },
        };
        AutomationProperties.SetName(button, text);
        button.Tapped += (_, _) => onTap();
        return button;
    }

    /// <summary>Recorte escurecido em volta da região e o balão ancorado a ela (ou num canto, com um modal aberto).</summary>
    private void Position()
    {
        if (_card is null || Root.Visibility != Visibility.Visible) return;
        var width = Root.ActualWidth;
        var height = Root.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var margin = Theme.SpaceM;
        Rect? hole = null;
        if (_target != TutorialTarget.Modal && _resolve(_target) is { ActualWidth: > 0, ActualHeight: > 0 } element)
        {
            try
            {
                var bounds = element.TransformToVisual(Root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                var pad = Theme.SpaceXs;
                var x = Math.Max(0, bounds.X - pad);
                var y = Math.Max(0, bounds.Y - pad);
                hole = new Rect(x, y, Math.Max(0, Math.Min(width, bounds.Right + pad) - x), Math.Max(0, Math.Min(height, bounds.Bottom + pad) - y));
            }
            catch (ArgumentException)
            {
                hole = null; // região fora da árvore neste instante
            }
        }
        if (hole is { } r)
        {
            Place(_dim[0], 0, 0, width, r.Y);
            Place(_dim[1], 0, r.Bottom, width, height - r.Bottom);
            Place(_dim[2], 0, r.Y, r.X, r.Height);
            Place(_dim[3], r.Right, r.Y, width - r.Right, r.Height);
            _ring.Visibility = Visibility.Visible;
            _ring.BorderBrush = Theme.Accent;
            _ring.BorderThickness = Theme.GlowRing;
            _ring.CornerRadius = new CornerRadius(Theme.Scaled(14));
            Canvas.SetLeft(_ring, r.X);
            Canvas.SetTop(_ring, r.Y);
            _ring.Width = r.Width;
            _ring.Height = r.Height;
        }
        else
        {
            foreach (var rect in _dim) rect.Visibility = Visibility.Collapsed;
            _ring.Visibility = Visibility.Collapsed;
        }

        _card.Measure(new Size(_card.Width, double.PositiveInfinity));
        var size = _card.DesiredSize;
        double left, top;
        switch (hole)
        {
            case { } area when _target == TutorialTarget.TopBar:
                left = width - size.Width - margin; // logo abaixo da barra, à direita (onde ficam os atalhos)
                top = area.Bottom + margin;
                break;
            case { } area when _target == TutorialTarget.Footer:
                left = margin; // logo acima do rodapé, onde começam as legendas
                top = area.Y - size.Height - margin;
                break;
            case { } area:
                left = area.Right - size.Width - margin; // dentro do conteúdo, no canto inferior direito
                top = area.Bottom - size.Height - margin;
                break;
            default:
                left = margin; // modal aberto: canto inferior esquerdo, fora do painel centralizado
                top = height - size.Height - margin;
                break;
        }
        Canvas.SetLeft(_card, Math.Clamp(left, margin, Math.Max(margin, width - size.Width - margin)));
        Canvas.SetTop(_card, Math.Clamp(top, margin, Math.Max(margin, height - size.Height - margin)));
    }

    private static void Place(Rectangle rect, double x, double y, double w, double h)
    {
        rect.Visibility = w > 0 && h > 0 ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = Math.Max(0, w);
        rect.Height = Math.Max(0, h);
    }
}
