using ControlFS.Core.Policies;

namespace ControlFS.UnitTests.Core;

public class ArchivePathPolicyTests
{
    private static SanitizedPath S(string key) => ArchivePathPolicy.Sanitize(key, maxDepth: 8, maxRelativeLength: 200);

    [Theory]
    [InlineData("a.txt", "a.txt")]
    [InlineData("dir/sub/a.txt", "dir/sub/a.txt")]
    [InlineData("dir\\sub\\a.txt", "dir/sub/a.txt")]
    [InlineData("dir/", "dir")]
    [InlineData("./dir/./a.txt", "dir/a.txt")]
    [InlineData("Relatórios/ação.txt", "Relatórios/ação.txt")]
    public void Accepts_contained_paths(string key, string expected)
    {
        var result = S(key);
        Assert.True(result.IsAccepted, result.Message);
        Assert.Equal(expected, result.RelativePath);
    }

    [Theory]
    [InlineData("../escape.txt", PathRejection.Traversal)]
    [InlineData("a/../../b.txt", PathRejection.Traversal)]
    [InlineData("a/..", PathRejection.Traversal)]
    [InlineData("..\\x", PathRejection.Traversal)]
    [InlineData("/etc/passwd", PathRejection.Absolute)]
    [InlineData("\\Windows\\x", PathRejection.Absolute)]
    [InlineData("C:\\Windows\\x", PathRejection.DriveQualified)]
    [InlineData("C:arquivo.txt", PathRejection.DriveQualified)]
    [InlineData("c:/x", PathRejection.DriveQualified)]
    [InlineData("\\\\server\\share\\x", PathRejection.UncOrDeviceNamespace)]
    [InlineData("//server/share/x", PathRejection.UncOrDeviceNamespace)]
    [InlineData("\\\\?\\C:\\x", PathRejection.UncOrDeviceNamespace)]
    [InlineData("\\\\.\\PhysicalDrive0", PathRejection.UncOrDeviceNamespace)]
    [InlineData("a\0b", PathRejection.NullByte)]
    [InlineData("a//b", PathRejection.EmptyComponent)]
    [InlineData("file.txt:Zone.Identifier", PathRejection.InvalidComponent)]
    [InlineData("dir/stream:$DATA", PathRejection.InvalidComponent)]
    [InlineData("CON", PathRejection.InvalidComponent)]
    [InlineData("dir/aux.txt", PathRejection.InvalidComponent)]
    [InlineData("dir./a", PathRejection.InvalidComponent)]
    [InlineData("a /b", PathRejection.InvalidComponent)]
    [InlineData("a*b", PathRejection.InvalidComponent)]
    [InlineData("", PathRejection.Empty)]
    [InlineData("./", PathRejection.Empty)]
    public void Rejects_escaping_or_invalid_paths(string key, PathRejection expected)
    {
        var result = S(key);
        Assert.False(result.IsAccepted);
        Assert.Equal(expected, result.Rejection);
    }

    [Fact]
    public void Enforces_depth_and_length_limits()
    {
        Assert.Equal(PathRejection.TooDeep, S(string.Join('/', Enumerable.Repeat("d", 9))).Rejection);
        Assert.Equal(PathRejection.TooLong, S(string.Join('/', Enumerable.Repeat(new string('x', 60), 4))).Rejection);
    }
}
