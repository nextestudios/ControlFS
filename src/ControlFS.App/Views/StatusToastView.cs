using ControlFS.App.Resources;
using ControlFS.Core.Actions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ControlFS.App.Views;

/// <summary>
/// Aviso flutuante (P2-1/P2-2 da auditoria de UX): os avisos do momento ("Exibição em grade.", "3 itens copiados · Menu →
/// Desfazer") aparecem no canto inferior direito do conteúdo, por cima da lista, em vez de uma linha no rodapé que mudava
/// a altura dele e encolhia a lista. Some sozinho depois de <see cref="Duration"/> (a próxima ação não o apaga antes);
/// um aviso novo, mesmo com o mesmo texto, reinicia o tempo. Nunca recebe foco nem clique; é uma região viva do Narrador.
/// <para>
/// O esmaecimento (≤ 200 ms) é só enfeite: o aviso já está todo visível no primeiro quadro e some de verdade (Collapsed)
/// por um temporizador, sem depender de animação. Nas capturas (<c>pinned</c>) ele acompanha o aviso do AppController, sem
/// tempo, para a imagem não depender do relógio.
/// </para>
/// </summary>
internal sealed class StatusToastView
{
    /// <summary>Quanto o aviso fica na tela.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(200);
    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.WrapWholeWords, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _icon = new() { FontFamily = new FontFamily(IconFont), Text = ActionIcons.Glyph(ActionIcon.Info), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherQueueTimer _hide;
    private readonly DispatcherQueueTimer _collapse;
    private int _shownSerial = -1;
    private bool _top;

    public StatusToastView(DispatcherQueue dispatcher)
    {
        var row = new Grid { ColumnSpacing = Theme.SpaceS };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(_icon);
        Grid.SetColumn(_text, 1);
        row.Children.Add(_text);
        Root = new Border
        {
            Child = row,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        AutomationProperties.SetLiveSetting(_text, AutomationLiveSetting.Polite);
        _collapse = dispatcher.CreateTimer();
        _collapse.IsRepeating = false;
        _collapse.Interval = Fade + TimeSpan.FromMilliseconds(50);
        _collapse.Tick += (_, _) => Root.Visibility = Visibility.Collapsed;
        _hide = dispatcher.CreateTimer();
        _hide.IsRepeating = false;
        _hide.Interval = Duration;
        _hide.Tick += (_, _) =>
        {
            Root.Opacity = 0; // esmaece com a transição, se houver; o Collapsed abaixo vale de qualquer jeito
            _collapse.Start();
        };
    }

    public Border Root { get; }

    /// <summary>Aviso à mostra (inclui o esmaecimento final).</summary>
    public bool IsShown => Root.Visibility == Visibility.Visible;

    /// <summary>Medidas e cores da faixa de layout e do tema atuais.</summary>
    public void ApplyLayout()
    {
        Root.Background = Theme.SurfaceRaised;
        Root.BorderBrush = Theme.ModalEdge;
        Root.BorderThickness = Theme.Hairline;
        Root.CornerRadius = Theme.RowRadius;
        Root.Padding = new Thickness(Theme.SpaceM, Theme.SpaceS + Theme.SpaceXs, Theme.SpaceM + Theme.SpaceXs, Theme.SpaceS + Theme.SpaceXs);
        Root.MaxWidth = Math.Max(Theme.Scaled(320), Math.Min(Theme.Scaled(640), Theme.Viewport.Width * 0.45));
        Place(_top);
        _text.FontSize = _icon.FontSize = Theme.FontBody;
        _text.Foreground = Theme.Text;
        _icon.Foreground = Theme.Accent;
        Root.OpacityTransition = Theme.ReduceMotion ? null : new ScalarTransition { Duration = Fade };
    }

    /// <summary>
    /// Mostra um aviso novo (<paramref name="serial"/> mudou) e reinicia o tempo. Com um modal aberto o aviso aparece no
    /// painel do modal, então este fica escondido. <paramref name="pinned"/>: capturas, sem tempo.
    /// </summary>
    public void Render(string? message, int serial, bool modalOpen, bool pinned)
    {
        if (modalOpen || (pinned && string.IsNullOrEmpty(message)))
        {
            Hide();
            _shownSerial = serial;
            return;
        }
        if (serial == _shownSerial || string.IsNullOrEmpty(message)) return; // o mesmo aviso continua até o tempo acabar
        _shownSerial = serial;
        _collapse.Stop();
        _text.Text = message;
        Root.Visibility = Visibility.Visible;
        Root.Opacity = 1;
        AutomationProperties.SetName(Root, message);
        (FrameworkElementAutomationPeer.FromElement(_text) ?? FrameworkElementAutomationPeer.CreatePeerForElement(_text))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        _hide.Stop();
        if (!pinned) _hide.Start();
    }

    private void Hide()
    {
        _hide.Stop();
        _collapse.Stop();
        Root.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Nunca cobre o item em foco: embaixo à direita, ou em cima à direita quando o item focado passa por baixo do aviso.
    /// <paramref name="focused"/> é o retângulo do item em foco no mesmo sistema de coordenadas do conteúdo (null: nenhum).
    /// </summary>
    public void AvoidFocus(Windows.Foundation.Rect? focused, Windows.Foundation.Size content)
    {
        if (!IsShown || Root.ActualHeight <= 0) return;
        var width = Root.ActualWidth + Root.Margin.Right;
        var height = Root.ActualHeight + Root.Margin.Bottom;
        var bottom = new Windows.Foundation.Rect(content.Width - width, content.Height - height, width, height);
        var top = focused is { } rect && Intersects(rect, bottom);
        if (top != _top) Place(top);
    }

    private void Place(bool top)
    {
        _top = top;
        Root.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        Root.Margin = top
            ? new Thickness(0, Theme.Space(20) + Theme.SpaceS, Theme.SpaceL + Theme.SpaceS, 0)
            : new Thickness(0, 0, Theme.SpaceL + Theme.SpaceS, Theme.Space(24) + Theme.SpaceS);
    }

    private static bool Intersects(Windows.Foundation.Rect a, Windows.Foundation.Rect b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
