using ControlFS.Core.Icons;
using Xunit;

namespace ControlFS.UnitTests.Core;

public sealed class ArchiveGlyphTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(48)]
    [InlineData(256)]
    public void The_fallback_archive_icon_is_drawn_at_the_requested_size_with_transparent_corners_and_visible_body(int size)
    {
        var icon = ArchiveGlyph.Render(size);
        var pixels = icon.Pixels.Span;

        Assert.Equal(size, icon.Width);
        Assert.Equal(size, icon.Height);
        Assert.Equal(size * size * 4, pixels.Length);
        Assert.Equal(0, pixels[3]); // canto de cima à esquerda: transparente
        var centre = (((size / 2) * size) + (size / 2)) * 4;
        Assert.Equal(255, pixels[centre + 3]); // o corpo no centro: opaco
        for (var i = 0; i < pixels.Length; i += 4)
            Assert.True(pixels[i] <= pixels[i + 3] && pixels[i + 1] <= pixels[i + 3] && pixels[i + 2] <= pixels[i + 3], "alfa pré-multiplicado");
    }
}
