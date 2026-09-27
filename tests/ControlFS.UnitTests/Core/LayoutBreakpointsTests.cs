using ControlFS.Core.Layout;

namespace ControlFS.UnitTests.Core;

public class LayoutBreakpointsTests
{
    [Theory]
    [InlineData(1280, 720, 1.0, LayoutTier.Compact, 1.0)]    // portátil 720p
    [InlineData(1280, 800, 1.0, LayoutTier.Compact, 1.0)]    // portátil 800p
    [InlineData(1280, 720, 1.5, LayoutTier.Compact, 1.0)]    // 1080p a 150% (portátil 1080p de 7")
    [InlineData(1920, 1080, 1.0, LayoutTier.Regular, 1.0)]   // desktop / TV 1080p
    [InlineData(1920, 1080, 2.0, LayoutTier.Regular, 1.0)]   // TV 4K a 200%
    [InlineData(1280, 720, 3.0, LayoutTier.Compact, 1.0)]    // TV 4K a 300%
    [InlineData(3840, 2160, 1.0, LayoutTier.Large, 2.0)]     // TV 4K a 100%: proporções de 1080p (legível a ~3 m)
    [InlineData(2560, 1440, 1.5, LayoutTier.Large, 1.25)]    // TV 4K a 150%
    public void Target_screens_map_to_the_expected_tier(double width, double height, double scale, LayoutTier tier, double fontScale)
    {
        var profile = LayoutBreakpoints.Select(width, height, scale);

        Assert.Equal(tier, profile.Tier);
        Assert.Equal(fontScale, profile.FontScale);
    }

    [Fact]
    public void Compact_starts_right_below_the_height_and_width_thresholds()
    {
        Assert.Equal(LayoutTier.Compact, LayoutBreakpoints.Select(1920, LayoutBreakpoints.CompactBelowHeight - 1).Tier);
        Assert.Equal(LayoutTier.Regular, LayoutBreakpoints.Select(1920, LayoutBreakpoints.CompactBelowHeight).Tier);
        Assert.Equal(LayoutTier.Compact, LayoutBreakpoints.Select(LayoutBreakpoints.CompactBelowWidth - 1, 1080).Tier);
    }

    [Fact]
    public void Windows_text_scaling_takes_space_and_can_make_1080p_compact()
    {
        // 1080p com texto a 150% só tem espaço de 720p para texto: o layout fica enxuto para nada ser cortado.
        var profile = LayoutBreakpoints.Select(1920, 1080, 1.0, textScaleFactor: 1.5);

        Assert.Equal(LayoutTier.Compact, profile.Tier);
        Assert.Equal(1.0, profile.FontScale); // o próprio WinUI aplica o fator de texto; não multiplicar de novo
    }

    [Fact]
    public void Large_screens_never_grow_beyond_what_the_width_allows()
    {
        // Tela alta e estreita (retrato 4K): cresce pela largura, senão rodapé e cabeçalho não caberiam.
        var profile = LayoutBreakpoints.Select(2400, 3840);

        Assert.Equal(1.25, profile.FontScale);
    }
}
