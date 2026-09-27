using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ControlFS.Core.Contracts;
using ControlFS.Core.Preview;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ControlFS.Infrastructure.Media.Pdf;

/// <summary>
/// PDF com o Windows.Data.Pdf (#59): o Windows só desenha a página em pixels — não há links, anexos, formulários nem
/// scripts nesse caminho, e nada é aberto em outro programa. Abrir e desenhar acontecem numa thread do pool; o arquivo
/// fica aberto (compartilhado para leitura, escrita e exclusão) enquanto a visualização durar.
/// </summary>
public sealed class WindowsPdfRenderer : IPdfRenderer
{
    /// <summary>ERROR_WRONG_PASSWORD: o Windows.Data.Pdf usa este código para "precisa de senha" e para "senha errada".</summary>
    private const int WrongPasswordHResult = unchecked((int)0x8007052B);

    public Task<IPdfDocument> OpenAsync(string path, string? password, CancellationToken cancellationToken) =>
        Task.Run(() => OpenCoreAsync(path, password, cancellationToken), cancellationToken);

    private static async Task<IPdfDocument> OpenCoreAsync(string path, string? password, CancellationToken cancellationToken)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
        IRandomAccessStream? stream = null;
        try
        {
            stream = file.AsRandomAccessStream();
            var document = password is null
                ? await PdfDocument.LoadFromStreamAsync(stream).AsTask(cancellationToken)
                : await PdfDocument.LoadFromStreamAsync(stream, password).AsTask(cancellationToken);
            return new Document(document, stream, file);
        }
        catch (Exception ex) when (ex.HResult == WrongPasswordHResult)
        {
            Close(stream, file);
            throw new PdfPasswordException(password is not null, ex);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Close(stream, file);
            throw new PreviewException("O Windows não conseguiu abrir este PDF (arquivo danificado ou recurso sem suporte).", ex);
        }
        catch
        {
            Close(stream, file);
            throw;
        }
    }

    private static void Close(IRandomAccessStream? stream, FileStream file)
    {
        stream?.Dispose();
        file.Dispose();
    }

    private sealed class Document(PdfDocument document, IRandomAccessStream stream, FileStream file) : IPdfDocument
    {
        /// <summary>Um desenho por vez; liberar espera o desenho em andamento terminar (o arquivo nunca some no meio).</summary>
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _disposed;

        public int PageCount { get; } = (int)Math.Min(document.PageCount, int.MaxValue);

        public Task<PreviewImage> RenderPageAsync(int index, int maxSide, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, PageCount);
            return Task.Run(() => RenderCoreAsync(index, maxSide, cancellationToken), cancellationToken);
        }

        private async Task<PreviewImage> RenderCoreAsync(int index, int maxSide, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                using var page = document.GetPage((uint)index);
                var (width, height) = PdfPreviewPolicy.RenderSize(page.Size.Width, page.Size.Height, maxSide);
                using var output = new InMemoryRandomAccessStream();
                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)width,
                    DestinationHeight = (uint)height,
                    BackgroundColor = new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 },
                };
                await page.RenderToStreamAsync(output, options).AsTask(cancellationToken).ConfigureAwait(false);
                output.Seek(0);
                var decoder = await BitmapDecoder.CreateAsync(output).AsTask(cancellationToken).ConfigureAwait(false);
                var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false);
                return new PreviewImage((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData());
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
            {
                throw new PreviewException("O Windows não conseguiu desenhar esta página do PDF.", ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            // Não bloqueia a thread de UI: o arquivo é fechado assim que um desenho em andamento terminar.
            _ = Task.Run(async () =>
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_disposed) return;
                    _disposed = true;
                    stream.Dispose();
                    await file.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }
            });
        }
    }
}
