using System.Text;
using ControlFS.Core.Preview;

namespace ControlFS.UnitTests.Core;

public class TextPreviewTests
{
    private static TextDocument Read(byte[] bytes, PreviewLimits? limits = null) => TextPreview.Read(new MemoryStream(bytes), limits ?? PreviewLimits.Default);

    public static TheoryData<string, byte[]> Encodings() => new()
    {
        { "UTF-8", Encoding.UTF8.GetBytes("café\nação") },
        { "UTF-8 com BOM", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("café\nação")] },
        { "UTF-16 LE", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("café\nação")] },
        { "UTF-16 LE", Encoding.Unicode.GetBytes("café\nação sem BOM, como o Bloco de Notas antigo") },
        { "ANSI", Encoding.Latin1.GetBytes("café\nação") }, // não é UTF-8 válido: cai no ANSI do sistema
    };

    [Theory]
    [MemberData(nameof(Encodings))]
    public void Detects_the_encoding(string expected, byte[] bytes)
    {
        var document = Read(bytes);
        Assert.StartsWith(expected, document.EncodingName, StringComparison.Ordinal);
        Assert.Equal("café", document.Lines[0]);
        Assert.StartsWith("ação", document.Lines[1], StringComparison.Ordinal);
        Assert.False(document.IsTruncated);
    }

    [Fact]
    public void Binary_files_are_refused()
    {
        byte[] executable = [(byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xB8, 0x00];
        var ex = Assert.Throws<PreviewException>(() => Read(executable));
        Assert.Contains("binário", ex.Message, StringComparison.Ordinal);
        var controls = Enumerable.Range(0, 200).Select(i => (byte)(i % 8 + 1)).ToArray(); // sem NUL, só caracteres de controle
        Assert.Throws<PreviewException>(() => Read(controls));
    }

    [Fact]
    public void Reads_at_most_the_byte_and_line_limits_and_says_so()
    {
        var lines = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"linha {i}"));
        var byLines = Read(Encoding.UTF8.GetBytes(lines), new PreviewLimits { MaxTextLines = 10 });
        Assert.Equal(10, byLines.Lines.Count);
        Assert.True(byLines.IsTruncated);
        Assert.Contains("primeiras 10 linhas", TextPreview.TruncationNotice(byLines), StringComparison.Ordinal);

        // O limite de bytes corta no meio de "ç" (2 bytes): o caractere incompleto some, sem virar "�".
        var text = Encoding.UTF8.GetBytes("aaaaaaaaaç" + new string('b', 100));
        var byBytes = Read(text, new PreviewLimits { MaxTextBytes = 10 });
        Assert.Equal("aaaaaaaaa", Assert.Single(byBytes.Lines));
        Assert.True(byBytes.IsTruncated);
        Assert.Equal(10, byBytes.BytesRead);
        Assert.Equal(text.Length, byBytes.FileBytes);
    }
}
