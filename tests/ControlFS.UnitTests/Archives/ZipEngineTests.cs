using ControlFS.Infrastructure.Archives.Engines;

namespace ControlFS.UnitTests.Archives;

public class ZipEngineTests
{
    [Theory]
    [InlineData(0x81A40000, false)]  // arquivo regular Unix 0644
    [InlineData(0x41ED0010, false)]  // diretório Unix
    [InlineData(0xA1ED0000, true)]   // symlink Unix (observado em fixture do Info-ZIP)
    [InlineData(0x21B60000, true)]   // dispositivo de caractere
    [InlineData(0x00000020, false)]  // arquivo Windows (ARCHIVE)
    [InlineData(0x00000420, true)]   // Windows com REPARSE_POINT
    public void Detects_links_and_special_types_from_attributes(uint attrib, bool expected) =>
        Assert.Equal(expected, SharpCompressZipEngine.IsLinkOrSpecial(unchecked((int)attrib), null));

    [Fact]
    public void Link_target_always_blocks() => Assert.True(SharpCompressZipEngine.IsLinkOrSpecial(null, "../x"));
}
