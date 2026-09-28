using ControlFS.App.Resources;
using ControlFS.Application;
using ControlFS.Core.Actions;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ControlFS.App.Views;

/// <summary>
/// Barra de título na cor do tema (#230), como no Discord: o conteúdo se estende até o topo da janela e o cabeçalho (logo,
/// abas, controle em uso) ocupa a faixa do título. Minimizar, maximizar e fechar continuam sendo os botões do Windows
/// (layouts de ajuste ao passar o mouse em maximizar, clique duplo para maximizar/restaurar), só que com as cores do tema;
/// com alto contraste, voltam às cores do sistema. Ao lado deles, o botão de tela cheia.
/// <para>
/// A área de arrastar é o cabeçalho inteiro, menos as abas e o botão de tela cheia (que continuam clicáveis). Com um modal
/// ou o vídeo na tela, nada é arrastável: o painel pode passar por cima do cabeçalho e o clique tem de chegar a ele. As
/// áreas são refeitas quando o cabeçalho muda de tamanho, a janela muda de DPI ou entra/sai de tela cheia.
/// </para>
/// <para>
/// Também troca o modo da janela (<see cref="AppController.WindowFullScreen"/>): tela cheia ou a janela comum de antes
/// (maximizada ou não). Sem suporte a personalizar a barra (Windows antigo), a barra do sistema fica e só o botão aparece.
/// </para>
/// </summary>
internal sealed class TitleBarView
{
    /// <summary>Tamanho de um botão da barra do Windows em pixels efetivos (46 × 32 no Windows 10 e 11).</summary>
    private const double ButtonWidth = 46;
    private const double DefaultHeight = 32;

    /// <summary>Três botões do sistema: a reserva usada nas capturas, que não têm a barra real.</summary>
    private const double CaptureCaptionWidth = 3 * ButtonWidth;

    private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";

    private readonly Window _window;
    private readonly FrameworkElement _header;
    private readonly FrameworkElement _tabs;
    private readonly AppController _app;
    private readonly bool _live;
    private readonly bool _custom;
    private readonly Border _fullScreenButton = new() { Width = ButtonWidth, Height = DefaultHeight, Background = Theme.Transparent };
    private readonly TextBlock _glyph = new()
    {
        FontFamily = new FontFamily(IconFont),
        FontSize = 10,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = Theme.Text,
    };
    private readonly Border _captionSpace = new() { Height = DefaultHeight };
    private readonly OverlappedPresenter? _overlapped;
    private InputNonClientPointerSource? _regions;
    private bool _ready;
    private bool _fullScreen;
    private bool _hover;
    private bool _pressed;
    private bool _active = true;
    private double _captionWidth;
    private string? _shownRegions;

    public TitleBarView(Window window, FrameworkElement header, FrameworkElement tabs, AppController app, bool live)
    {
        _window = window;
        _header = header;
        _tabs = tabs;
        _app = app;
        _live = live;
        _fullScreenButton.Child = _glyph;
        Buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _fullScreenButton, _captionSpace },
        };
        _fullScreenButton.PointerEntered += (_, _) => SetPointer(hover: true, _pressed);
        _fullScreenButton.PointerExited += (_, _) => SetPointer(hover: false, pressed: false);
        _fullScreenButton.PointerPressed += (_, _) => SetPointer(_hover, pressed: true);
        _fullScreenButton.PointerReleased += (_, _) => SetPointer(_hover, pressed: false);
        _fullScreenButton.PointerCanceled += (_, _) => SetPointer(hover: false, pressed: false);
        _fullScreenButton.Tapped += (_, e) =>
        {
            e.Handled = true;
            _app.ToggleFullScreen();
        };
        _header.SizeChanged += (_, _) => UpdateRegions();
        _tabs.SizeChanged += (_, _) => UpdateRegions();
        _captionWidth = live ? 0 : CaptureCaptionWidth;
        ShowGlyph();
        if (!live)
        {
            // Capturas: sem a barra real, os três botões do Windows são imitados com os mesmos símbolos e a cor do tema,
            // só para conferir o conjunto; as cores reais (passar o mouse, janela inativa) são conferência manual.
            var mock = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var glyph in new[] { "\uE921", "\uE922", "\uE8BB" })
                mock.Children.Add(new Border
                {
                    Width = ButtonWidth,
                    Child = new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 10, Foreground = Theme.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
            _captionSpace.Width = CaptureCaptionWidth;
            _captionSpace.Child = mock;
        }

        if (!live) return;
        _overlapped = window.AppWindow.Presenter as OverlappedPresenter;
        try
        {
            _custom = AppWindowTitleBar.IsCustomizationSupported();
            if (_custom)
            {
                window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                _regions = InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Sem a barra personalizada, a do sistema continua (Windows 10 antigo, sessão sem composição).
            AppLog.Info($"Barra de título do sistema mantida: {ex.Message}");
            _custom = false;
            _regions = null;
        }
        window.AppWindow.Changed += (_, e) =>
        {
            if (e.DidPresenterChange || e.DidSizeChange) Update();
        };
    }

    /// <summary>Botão de tela cheia e o espaço dos botões do Windows, no canto superior direito da janela.</summary>
    public StackPanel Buttons { get; }

    /// <summary>Largura que o cabeçalho reserva à direita: tela cheia + minimizar/maximizar/fechar + um respiro.</summary>
    public double ReservedWidth => ButtonWidth + _captionWidth;

    /// <summary>Depois de cada mudança de estado: modo da janela, ícone do botão e áreas de arrastar (com ou sem modal).</summary>
    public void Render()
    {
        ApplyWindowMode();
        UpdateRegions();
    }

    /// <summary>Janela carregada, DPI, tamanho ou modo mudou: largura dos botões do sistema e as áreas de arrastar.</summary>
    public void Update()
    {
        _ready = _live && _header.XamlRoot is not null;
        ApplyWindowMode();
        UpdateCaptionSpace();
        UpdateRegions();
    }

    /// <summary>Janela ativa ou não: o ícone esmaece como os botões do Windows.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        ShowGlyph();
    }

    /// <summary>Cores dos botões do Windows pelos tokens do tema; com alto contraste, as do sistema.</summary>
    public void ApplyColors(bool highContrast)
    {
        SetPointer(_hover, _pressed);
        if (!_custom) return;
        var bar = _window.AppWindow.TitleBar;
        if (highContrast)
        {
            bar.BackgroundColor = bar.InactiveBackgroundColor = bar.ForegroundColor = bar.InactiveForegroundColor = null;
            bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = bar.ButtonForegroundColor = bar.ButtonInactiveForegroundColor = null;
            bar.ButtonHoverBackgroundColor = bar.ButtonHoverForegroundColor = bar.ButtonPressedBackgroundColor = bar.ButtonPressedForegroundColor = null;
            return;
        }
        var palette = Theme.Palette;
        var text = Theme.ToColor(palette.Text);
        var muted = Theme.ToColor(palette.TextMuted);
        bar.BackgroundColor = bar.InactiveBackgroundColor = Theme.BackgroundColor;
        bar.ForegroundColor = text;
        bar.InactiveForegroundColor = muted;
        // Fundo transparente: aparece o cabeçalho (a cor do tema) atrás dos botões. Fechar fica vermelho ao passar o
        // mouse (o próprio Windows desenha); minimizar e maximizar usam a borda e o destaque suave do tema.
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = bar.ButtonHoverForegroundColor = bar.ButtonPressedForegroundColor = text;
        bar.ButtonInactiveForegroundColor = muted;
        bar.ButtonHoverBackgroundColor = Theme.ToColor(palette.Border);
        bar.ButtonPressedBackgroundColor = Theme.ToColor(palette.AccentSoft);
    }

    private void SetPointer(bool hover, bool pressed)
    {
        _hover = hover;
        _pressed = pressed;
        _fullScreenButton.Background = pressed ? Theme.AccentSoft : hover ? Theme.Border : Theme.Transparent;
    }

    private void ShowGlyph()
    {
        var on = _live ? _fullScreen : _app.WindowFullScreen;
        _glyph.Text = ActionIcons.Glyph(on ? ActionIcon.ExitFullScreen : ActionIcon.FullScreen);
        _glyph.Foreground = _active ? Theme.Text : Theme.TextMuted;
        var name = on ? "Sair da tela cheia (F11)" : "Tela cheia (F11)";
        AutomationProperties.SetName(_fullScreenButton, name);
        ToolTipService.SetToolTip(_fullScreenButton, name);
    }

    /// <summary>
    /// Tela cheia ou a janela comum: só depois de a janela carregar e só no app de verdade (as capturas não mudam de modo).
    /// Volta ao mesmo presenter de antes, então a janela maximizada continua maximizada.
    /// </summary>
    private void ApplyWindowMode()
    {
        if (!_ready) return;
        var want = _app.WindowFullScreen;
        if (want == _fullScreen) return;
        _fullScreen = want;
        try
        {
            if (want) _window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            else if (_overlapped is not null) _window.AppWindow.SetPresenter(_overlapped);
            else _window.AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            AppLog.Info($"Tela cheia indisponível: {ex.Message}");
        }
        ShowGlyph();
        UpdateCaptionSpace();
    }

    /// <summary>Largura dos botões do Windows (em tela cheia eles somem) e altura da faixa deles.</summary>
    private void UpdateCaptionSpace()
    {
        if (!_live || _header.XamlRoot is not { } root) return;
        var scale = root.RasterizationScale;
        double width = 0, height = DefaultHeight;
        if (_custom && !_fullScreen)
        {
            var bar = _window.AppWindow.TitleBar;
            width = Math.Round(bar.RightInset / scale);
            if (bar.Height > 0) height = Math.Round(bar.Height / scale);
        }
        _fullScreenButton.Height = _captionSpace.Height = height;
        _captionSpace.Width = width;
        if (width == _captionWidth) return;
        _captionWidth = width;
        (_window as MainWindow)?.OnTitleBarInsetChanged();
    }

    /// <summary>
    /// Arrastar: o cabeçalho sem o canto dos botões. Clicáveis: as abas e o botão de tela cheia. Com modal/vídeo ou em tela
    /// cheia, o cabeçalho inteiro fica clicável (nada arrasta). Só chama o Windows quando algo mudou.
    /// </summary>
    private void UpdateRegions()
    {
        if (_regions is null || _header.XamlRoot is not { } root || _header.ActualWidth <= 0) return;
        var scale = root.RasterizationScale;
        var header = Bounds(_header, scale, trimRight: Buttons.ActualWidth * scale);
        RectInt32[] caption, passthrough;
        if (_fullScreen || _app.TopModal is not null)
        {
            caption = [];
            passthrough = [Bounds(_header, scale, 0)];
        }
        else
        {
            caption = [header];
            passthrough = [.. new[] { _tabs, _fullScreenButton }
                .Where(e => e.Visibility == Visibility.Visible && e.ActualWidth > 0 && e.ActualHeight > 0)
                .Select(e => Bounds(e, scale, 0))];
        }
        var key = string.Join(";", caption.Concat(passthrough).Select(r => $"{r.X},{r.Y},{r.Width},{r.Height}")) + "|" + caption.Length;
        if (key == _shownRegions) return;
        _shownRegions = key;
        try
        {
            _regions.SetRegionRects(NonClientRegionKind.Caption, caption);
            _regions.SetRegionRects(NonClientRegionKind.Passthrough, passthrough);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            AppLog.Info($"Áreas da barra de título: {ex.Message}");
        }
    }

    /// <summary>Retângulo do elemento na janela em pixels físicos (o que o Windows espera).</summary>
    private static RectInt32 Bounds(FrameworkElement element, double scale, double trimRight)
    {
        var box = element.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return new RectInt32(
            (int)Math.Round(box.X * scale),
            (int)Math.Round(box.Y * scale),
            Math.Max(0, (int)Math.Round((box.Width * scale) - trimRight)),
            (int)Math.Round(box.Height * scale));
    }
}
