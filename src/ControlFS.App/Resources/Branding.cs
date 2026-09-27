using Microsoft.UI.Xaml.Media.Imaging;

namespace ControlFS.App.Resources;

/// <summary>Logo oficial (assets/controlfs-logo-text-900.png, copiada como controlfs-logo.png) carregada por stream.</summary>
public static class Branding
{
    private static BitmapImage? _logo;

    public static BitmapImage? Logo
    {
        get
        {
            if (_logo is not null) return _logo;
            var path = Path.Join(AppContext.BaseDirectory, "controlfs-logo.png");
            if (!File.Exists(path)) return null;
            var image = new BitmapImage();
            var stream = File.OpenRead(path);
            _ = LoadAsync(image, stream);
            return _logo = image;
        }
    }

    private static async Task LoadAsync(BitmapImage image, Stream stream)
    {
        try
        {
            await image.SetSourceAsync(stream.AsRandomAccessStream());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Info($"Logo não carregada: {ex.GetType().Name}");
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }
}
