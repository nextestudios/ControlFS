using Microsoft.UI.Xaml.Media.Imaging;

namespace ControlFS.App.Resources;

/// <summary>
/// Logo oficial (assets/controlfs-logo-text-900.png, copiada como controlfs-logo.png) carregada por stream. No tema claro,
/// a variante com o nome escuro (assets/controlfs-logo-text-light-900.png, as mesmas formas com as letras prateadas em
/// azul-marinho): sem a placa escura atrás do logo, que pesava no cabeçalho claro (auditoria de UX, P2-12).
/// </summary>
public static class Branding
{
    private static BitmapImage? _logo;
    private static BitmapImage? _logoLight;

    public static BitmapImage? Logo => _logo ??= Load("controlfs-logo.png");

    /// <summary>Logo do tema: a variante escura no claro (se faltar, a de sempre).</summary>
    public static BitmapImage? LogoFor(bool dark) => dark ? Logo : (_logoLight ??= Load("controlfs-logo-light.png")) ?? Logo;

    private static readonly Dictionary<string, BitmapImage?> Assets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Imagem do pacote ("Mais da equipe"): decodificada uma vez na altura de exibição e guardada.</summary>
    public static BitmapImage? Asset(string file)
    {
        if (!Assets.TryGetValue(file, out var image)) Assets[file] = image = Load(file);
        return image;
    }

    private static BitmapImage? Load(string file)
    {
        var path = Path.Join(AppContext.BaseDirectory, file);
        if (!File.Exists(path)) return null;
        // Decodificada na altura em que aparece (até 128 px lógicos: 64 no "Sobre" na escala máxima de layout), não nos 900 px do arquivo.
        var image = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelHeight = 128 };
        var stream = File.OpenRead(path);
        _ = LoadAsync(image, stream);
        return image;
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
