using System.Text;
using ControlFS.Core.Preview;

namespace ControlFS.UnitTests.Core;

/// <summary>Edição leve (#62): só o que volta byte a byte igual é editável; linhas não tocadas nunca mudam.</summary>
public class TextEditDocumentTests
{
    [Fact]
    public void Keeps_bom_mixed_line_endings_and_missing_final_newline_so_only_edited_lines_change()
    {
        var original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("nome=ação\r\nporta=80\n\tfim")).ToArray();
        var document = TextEditDocument.Load(new MemoryStream(original));
        Assert.Equal(["nome=ação", "porta=80", "\tfim"], document.Lines.Select(l => l.Text));
        Assert.Equal(["\r\n", "\n", ""], document.Lines.Select(l => l.Ending));
        Assert.Equal(original, document.Encode(document.Lines));

        var edited = document.Lines.ToList();
        edited[1] = edited[1] with { Text = "porta=8080" };
        var expected = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("nome=ação\r\nporta=8080\n\tfim")).ToArray();
        Assert.Equal(expected, document.Encode(edited));
    }

    [Fact]
    public void Refuses_binary_oversized_and_files_that_would_not_round_trip()
    {
        Assert.Contains("binário", Assert.Throws<PreviewException>(() => TextEditDocument.Load(new MemoryStream([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]))).Message, StringComparison.Ordinal);
        var big = new byte[TextEditDocument.MaxBytes + 1];
        Array.Fill(big, (byte)'a');
        Assert.Contains("grande demais", Assert.Throws<PreviewException>(() => TextEditDocument.Load(new MemoryStream(big))).Message, StringComparison.Ordinal);
        // UTF-8 cortado no fim: a prévia aceita, mas regravar mudaria bytes.
        var cut = Encoding.UTF8.GetBytes("linha com acento é").SkipLast(1).ToArray();
        Assert.Contains("codificação", Assert.Throws<PreviewException>(() => TextEditDocument.Load(new MemoryStream(cut))).Message, StringComparison.Ordinal);
    }
}
