using System.Globalization;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Media.Pdf;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>O Windows.Data.Pdf de verdade desenhando um PDF gerado aqui (sem arquivos binários no repositório).</summary>
public sealed class PdfRendererIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-pdf-tests", Guid.NewGuid().ToString("N"));

    public PdfRendererIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task Renders_page_one_of_a_two_page_pdf_to_bgra_pixels_with_the_page_aspect_and_releases_the_file()
    {
        var path = Path.Join(_root, "duas-paginas.pdf");
        File.WriteAllBytes(path, TwoPagePdf());
        var renderer = new WindowsPdfRenderer();

        var document = await renderer.OpenAsync(path, null, TestContext.Current.CancellationToken);
        Assert.Equal(2, document.PageCount);
        var page = await document.RenderPageAsync(0, 400, TestContext.Current.CancellationToken);

        // Página 1: 200 × 100 pt, metade esquerda preta (o conteúdo), metade direita no fundo branco.
        Assert.Equal((400, 200), (page.Width, page.Height));
        Assert.Equal(page.Width * page.Height * 4, page.Pixels.Length);
        Assert.Equal((0, 0, 0), Pixel(page, 100, 100));
        Assert.Equal((255, 255, 255), Pixel(page, 300, 100));

        var second = await document.RenderPageAsync(1, 400, TestContext.Current.CancellationToken);
        Assert.Equal((200, 400), (second.Width, second.Height)); // 100 × 200 pt, em pé

        document.Dispose();
        // Liberar fecha o arquivo logo depois (sem travar quem chamou): dá para apagar em seguida.
        for (var i = 0; i < 50 && File.Exists(path); i++)
        {
            try { File.Delete(path); }
            catch (IOException) { await Task.Delay(20, TestContext.Current.CancellationToken); }
        }
        Assert.False(File.Exists(path));
    }

    private static (int B, int G, int R) Pixel(PreviewImage image, int x, int y)
    {
        var offset = ((y * image.Width) + x) * 4;
        var span = image.Pixels.Span;
        return (span[offset], span[offset + 1], span[offset + 2]);
    }

    /// <summary>PDF mínimo de duas páginas com a tabela xref correta: 200×100 pt (quadrado preto à esquerda) e 100×200 pt.</summary>
    internal static byte[] TwoPagePdf()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << >> /Contents 4 0 R >>",
            Stream("0 0 0 rg 0 0 100 100 re f"),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 200] /Resources << >> /Contents 6 0 R >>",
            Stream("1 0 0 rg 0 0 100 100 re f"),
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());

        static string Stream(string content) => string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
    }
}
