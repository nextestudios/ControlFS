using ControlFS.Core.Policies;

namespace ControlFS.UnitTests.Core;

public class ReparseTagsTests
{
    [Theory]
    [InlineData(0x9000001Au)] // IO_REPARSE_TAG_CLOUD
    [InlineData(0x9000101Au)] // CLOUD_1
    [InlineData(0x9000601Au)] // CLOUD_6 (OneDrive)
    [InlineData(0x9000F01Au)] // CLOUD_F
    public void Cloud_files_folders_are_traversed(uint tag) => Assert.True(ReparseTags.IsTraversableFolder(tag));

    [Theory]
    [InlineData(ReparseTags.MountPoint)]
    [InlineData(ReparseTags.SymbolicLink)]
    [InlineData(0x80000017u)] // WOF
    [InlineData(0x8000001Bu)] // APPEXECLINK
    [InlineData(0x80000013u)] // DEDUP
    [InlineData(0x9000701Bu)] // não-nuvem com bits parecidos
    public void Links_mount_points_and_other_tags_are_refused(uint tag) => Assert.False(ReparseTags.IsTraversableFolder(tag));
}
