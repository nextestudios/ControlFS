using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ControlFS.Core.Contracts;
using ControlFS.Core.Preview;
using Windows.Graphics.Imaging;

namespace ControlFS.App.Preview;

/// <summary>
/// Decodifica imagens com o Windows Imaging Component numa thread do pool: só o primeiro quadro, reduzido na própria
/// decodificação para caber no lado máximo, e depois girado conforme o EXIF. Nunca abre programas nem executa nada.
/// </summary>
public sealed class WicImageDecoder : IImageDecoder
{
    private const string OrientationProperty = "System.Photo.Orientation";

    public Task<PreviewImage> DecodeAsync(string path, int maxSide, CancellationToken cancellationToken) =>
        Task.Run(() => DecodeCoreAsync(path, maxSide, cancellationToken), cancellationToken);

    private static async Task<PreviewImage> DecodeCoreAsync(string path, int maxSide, CancellationToken cancellationToken)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
            using var stream = file.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
            var (width, height) = ImagePreviewPolicy.DecodedSize((int)decoder.PixelWidth, (int)decoder.PixelHeight, maxSide);
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)width,
                ScaledHeight = (uint)height,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            // A orientação é aplicada à parte (PixelOrientation): assim a redução acontece sempre no espaço original.
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken);
            var pixels = data.DetachPixelData();
            var oriented = PixelOrientation.Apply(pixels, width, height, await ReadOrientationAsync(decoder));
            return new PreviewImage(oriented.Width, oriented.Height, oriented.Pixels);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new PreviewException("O Windows não conseguiu decodificar esta imagem (arquivo danificado ou formato sem suporte neste PC).", ex);
        }
    }

    private static async Task<int> ReadOrientationAsync(BitmapDecoder decoder)
    {
        try
        {
            var properties = await decoder.BitmapProperties.GetPropertiesAsync([OrientationProperty]);
            return properties.TryGetValue(OrientationProperty, out var value) && value.Value is ushort orientation ? orientation : 1;
        }
        catch (Exception ex) when (ex is COMException or NotSupportedException or UnauthorizedAccessException)
        {
            return 1; // formatos sem metadados (BMP, GIF) não têm orientação
        }
    }
}
