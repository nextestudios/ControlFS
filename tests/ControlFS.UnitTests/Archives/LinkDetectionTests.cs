using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives.Engines;

namespace ControlFS.UnitTests.Archives;

public class LinkDetectionTests
{
    [Theory]
    [InlineData(ArchiveFormat.Zip, 0x81A40000u, false)]  // arquivo regular Unix 0644 (bits altos)
    [InlineData(ArchiveFormat.Zip, 0x41ED0010u, false)]  // diretório Unix
    [InlineData(ArchiveFormat.Zip, 0xA1ED0000u, true)]   // symlink Unix (observado em fixture do Info-ZIP)
    [InlineData(ArchiveFormat.Zip, 0x21B60000u, true)]   // dispositivo de caractere
    [InlineData(ArchiveFormat.Zip, 0x00000020u, false)]  // arquivo Windows (ARCHIVE)
    [InlineData(ArchiveFormat.Zip, 0x00000420u, true)]   // Windows com REPARSE_POINT
    [InlineData(ArchiveFormat.SevenZip, 0x81ED8020u, false)] // 7z: extensão Unix, arquivo 0755
    [InlineData(ArchiveFormat.SevenZip, 0xA1FF8020u, true)]  // 7z: symlink
    [InlineData(ArchiveFormat.Rar, 0x000081EDu, false)] // RAR de Unix: arquivo 0755 (não pode ser confundido com DEVICE 0x40)
    [InlineData(ArchiveFormat.Rar, 0x000041EDu, false)] // RAR de Unix: diretório 0755
    [InlineData(ArchiveFormat.Rar, 0x0000A1FFu, true)]  // RAR de Unix: symlink
    [InlineData(ArchiveFormat.Rar, 0x00000020u, false)] // RAR de Windows: ARCHIVE
    [InlineData(ArchiveFormat.Rar, 0x00000420u, true)]  // RAR de Windows: REPARSE_POINT
    [InlineData(ArchiveFormat.Tar, 0x000001EDu, false)] // TAR: só permissões
    [InlineData(ArchiveFormat.Tar, 0x000081A4u, false)]
    public void Interprets_attributes_per_format(ArchiveFormat format, uint attrib, bool expected) =>
        Assert.Equal(expected, SharpCompressEngine.IsLinkOrSpecial(format, unchecked((int)attrib), null));

    [Theory]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.Tar)]
    [InlineData(ArchiveFormat.Rar)]
    public void Link_target_always_blocks(ArchiveFormat format) => Assert.True(SharpCompressEngine.IsLinkOrSpecial(format, null, "../x"));
}
