using System.Globalization;
using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Preview;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ControlFS.App.Views;

/// <summary>
/// Camada do reprodutor de vídeo (#61, #170): fica sob a camada modal e sobre todo o resto, enquanto houver um vídeo na
/// pilha. O elemento de vídeo é criado uma vez e mantido (um menu por cima não o recria); a sobreposição é atualizada no
/// lugar a cada desenho. Só desenha o estado do AppController.
/// </summary>
public sealed class VideoPlayerView
{
    public const string SurfaceId = "ControlFS.VideoSurface";

    private readonly MediaPlayerElement _element = new()
    {
        AreTransportControlsEnabled = false,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        IsTabStop = false,
    };

    private readonly Grid _overlay = new();
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, Foreground = Theme.Text, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock _tracks = new() { Foreground = Theme.TextMuted, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock _state = new() { FontWeight = FontWeights.SemiBold, Foreground = Theme.Text };
    private readonly TextBlock _times = new() { Foreground = Theme.Text };
    private readonly TextBlock _volume = new() { Foreground = Theme.TextMuted, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _center = new() { Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _centerCard = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(16) };
    private readonly Grid _track = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _played = new() { Background = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _target = new() { Background = Theme.Text, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _prompts = new();
    private VideoPlayerModal? _current;
    private object? _surface;

    public VideoPlayerView()
    {
        Root = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(_element, SurfaceId);
        Root.Children.Add(_element);

        _overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _overlay.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new StackPanel { Background = Shade(top: true) };
        top.Children.Add(_title);
        top.Children.Add(_tracks);
        _overlay.Children.Add(top);

        _track.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)) });
        _track.Children.Add(_played);
        _track.Children.Add(_target);
        var bottom = new StackPanel { Background = Shade(top: false) };
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(_state);
        Grid.SetColumn(_times, 1);
        line.Children.Add(_times);
        Grid.SetColumn(_volume, 2);
        line.Children.Add(_volume);
        bottom.Children.Add(line);
        bottom.Children.Add(_track);
        bottom.Children.Add(_prompts);
        Grid.SetRow(bottom, 2);
        _overlay.Children.Add(bottom);
        Root.Children.Add(_overlay);

        _centerCard.Child = _center;
        _centerCard.Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x02, 0x07, 0x0C));
        Root.Children.Add(_centerCard);
        AutomationProperties.SetLiveSetting(_center, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
    }

    public Grid Root { get; }

    /// <summary>O vídeo entrou ou saiu da pilha (a janela entra/sai de tela cheia).</summary>
    public event Action<bool>? ActiveChanged;

    private static LinearGradientBrush Shade(bool top)
    {
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, top ? 0 : 1), EndPoint = new Windows.Foundation.Point(0, top ? 1 : 0) };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xD0, 0, 0, 0), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x00, 0, 0, 0), Offset = 1 });
        return brush;
    }

    public void Render(AppController app)
    {
        var modal = app.Modals.OfType<VideoPlayerModal>().LastOrDefault();
        if (modal is null)
        {
            if (_current is null) return;
            _current = null;
            _surface = null;
            _element.SetMediaPlayer(null);
            Root.Visibility = Visibility.Collapsed;
            ActiveChanged?.Invoke(false);
            return;
        }
        if (!ReferenceEquals(modal, _current))
        {
            var wasActive = _current is not null;
            _current = modal;
            _surface = null;
            _element.SetMediaPlayer(null);
            Root.Visibility = Visibility.Visible;
            if (!wasActive) ActiveChanged?.Invoke(true);
        }
        if (modal.VideoSurface is Windows.Media.Playback.MediaPlayer player && !ReferenceEquals(player, _surface))
        {
            _surface = player;
            _element.SetMediaPlayer(player);
        }
        UpdateOverlay(app, modal);
    }

    private void UpdateOverlay(AppController app, VideoPlayerModal modal)
    {
        var status = modal.Status;
        var pad = Theme.SpaceXl;
        foreach (var panel in _overlay.Children.OfType<StackPanel>())
        {
            panel.Padding = new Thickness(pad, Theme.SpaceL, pad, Theme.SpaceL);
            panel.Spacing = Theme.SpaceS;
        }
        _title.FontSize = Theme.FontTitle;
        _title.Text = modal.Entry.Name;
        _tracks.FontSize = Theme.FontBody;
        _tracks.Text = TracksLine(status, modal);
        _state.FontSize = Theme.FontItem;
        _state.Margin = new Thickness(0, 0, Theme.SpaceL, 0);
        _state.Text = status.State switch
        {
            MediaPlaybackState.Opening => "Abrindo…",
            MediaPlaybackState.Buffering => "Carregando…",
            MediaPlaybackState.Playing => "▶",
            MediaPlaybackState.Paused => "⏸ Pausado",
            MediaPlaybackState.Ended => "Fim",
            _ => string.Empty,
        };
        _times.FontSize = Theme.FontItem;
        _times.Text = status.Duration > TimeSpan.Zero
            ? $"{MediaPreviewPolicy.FormatTime(status.Position)} / {MediaPreviewPolicy.FormatTime(status.Duration)}   (−{MediaPreviewPolicy.FormatTime(status.Duration - status.Position)})"
            : MediaPreviewPolicy.FormatTime(status.Position);
        _volume.FontSize = Theme.FontBody;
        _volume.Text = status.IsMuted ? "Sem som" : string.Create(CultureInfo.CurrentCulture, $"Volume {status.Volume * 100:0}%");

        // Barra: parte tocada em ciano; o destino da busca em preparo é um traço branco.
        var width = Math.Max(100, Theme.Viewport.Width - (2 * pad));
        var height = Theme.Scaled(8);
        _track.Height = height;
        _track.CornerRadius = new CornerRadius(height / 2);
        var fraction = status.Duration > TimeSpan.Zero ? Math.Clamp(status.Position / status.Duration, 0, 1) : 0;
        _played.Width = width * fraction;
        _played.CornerRadius = new CornerRadius(height / 2);
        if (modal.SeekTarget is { } target && status.Duration > TimeSpan.Zero)
        {
            _target.Visibility = Visibility.Visible;
            _target.Width = Theme.Scaled(6);
            _target.Height = height * 3;
            _target.Margin = new Thickness(Math.Clamp(width * (target / status.Duration), 0, width - _target.Width), -height, 0, -height);
        }
        else
        {
            _target.Visibility = Visibility.Collapsed;
        }

        // Legendas do controle só quando o vídeo é o topo (com um menu por cima, o menu mostra as dele).
        _prompts.Child = ReferenceEquals(app.TopModal, modal) ? ModalView.PromptBar(app.Prompts, Math.Round(Theme.FontBody * 1.8), Theme.FontBody) : null;

        var showOverlay = modal.OverlayVisible || modal.DisplayError is not null || !ReferenceEquals(app.TopModal, modal);
        if (_overlay.OpacityTransition is null && !Theme.ReduceMotion) _overlay.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(250) };
        _overlay.Opacity = showOverlay ? 1 : 0;

        // Centro: erro, abertura ou o destino da busca em letras grandes.
        var center = modal.DisplayError is { } error ? "⚠ " + error
            : modal.SeekTarget is { } seek ? "→ " + MediaPreviewPolicy.FormatTime(seek)
            : status.State == MediaPlaybackState.Opening ? "Abrindo vídeo…"
            : null;
        _centerCard.Visibility = center is null ? Visibility.Collapsed : Visibility.Visible;
        _centerCard.Padding = new Thickness(Theme.SpaceXl, Theme.SpaceL, Theme.SpaceXl, Theme.SpaceL);
        _centerCard.MaxWidth = Theme.Viewport.Width * 0.7;
        _center.FontSize = modal.SeekTarget is not null && modal.DisplayError is null ? Theme.Font(56) : Theme.FontTitle;
        _center.Foreground = modal.DisplayError is not null ? Theme.Danger : Theme.Text;
        _center.Text = center ?? string.Empty;
        AutomationProperties.SetName(_element, $"Vídeo {modal.Entry.Name}, {_state.Text} {_times.Text}");
    }

    /// <summary>Legendas e faixa de áudio: só o que existe (nada de controles que não fazem nada).</summary>
    private static string TracksLine(MediaStatus status, VideoPlayerModal modal)
    {
        var parts = new List<string>();
        var subtitles = status.Subtitles ?? [];
        if (subtitles.Count > 0)
            parts.Add(status.Subtitle >= 0 && subtitles.FirstOrDefault(s => s.Index == status.Subtitle) is { } on ? "Legendas: " + on.Label : "Legendas desligadas");
        else if (modal.SubtitlePath is not null)
            parts.Add("Legenda " + Path.GetFileName(modal.SubtitlePath));
        var audio = status.AudioTracks ?? [];
        if (audio.Count > 1 && audio.FirstOrDefault(a => a.Index == status.AudioTrack) is { } track) parts.Add("Áudio: " + track.Label);
        return string.Join(" · ", parts);
    }
}
