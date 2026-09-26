using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ControlFS.App.Resources;

/// <summary>
/// Tokens visuais (tema escuro grafite). Perigo nunca é indicado só por cor: textos levam "⚠".
/// Tema claro e cor de destaque configurável: pendentes (ver PROGRESS.md).
/// </summary>
public static class Theme
{
    public static readonly Color BackgroundColor = ColorHelper.FromArgb(255, 0x17, 0x19, 0x1D);
    public static readonly SolidColorBrush Background = new(BackgroundColor);
    public static readonly SolidColorBrush Surface = new(ColorHelper.FromArgb(255, 0x21, 0x24, 0x29));
    public static readonly SolidColorBrush SurfaceRaised = new(ColorHelper.FromArgb(255, 0x2B, 0x2F, 0x35));
    public static readonly SolidColorBrush Border = new(ColorHelper.FromArgb(255, 0x3A, 0x3F, 0x47));
    public static readonly SolidColorBrush Text = new(ColorHelper.FromArgb(255, 0xEC, 0xEE, 0xF1));
    public static readonly SolidColorBrush TextMuted = new(ColorHelper.FromArgb(255, 0xA3, 0xA9, 0xB3));
    public static readonly SolidColorBrush TextDisabled = new(ColorHelper.FromArgb(255, 0x6C, 0x72, 0x7B));
    public static readonly SolidColorBrush Accent = new(ColorHelper.FromArgb(255, 0x5B, 0xC0, 0xEB));
    public static readonly SolidColorBrush AccentSoft = new(ColorHelper.FromArgb(255, 0x1E, 0x3A, 0x48));
    public static readonly SolidColorBrush Selected = new(ColorHelper.FromArgb(255, 0xF2, 0xC1, 0x4E));
    public static readonly SolidColorBrush Danger = new(ColorHelper.FromArgb(255, 0xFF, 0x7A, 0x6E));
    public static readonly SolidColorBrush Scrim = new(ColorHelper.FromArgb(200, 0x08, 0x09, 0x0B));
    public static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    public const double SpaceXs = 4;
    public const double SpaceS = 8;
    public const double SpaceM = 16;
    public const double SpaceL = 24;
    public const double SpaceXl = 40;

    public const double FontCaption = 14;
    public const double FontBody = 18;
    public const double FontItem = 20;
    public const double FontTitle = 26;

    public static readonly CornerRadius Radius = new(6);
    public static readonly Thickness FocusRing = new(3);
    public static readonly Thickness Hairline = new(1);
}
