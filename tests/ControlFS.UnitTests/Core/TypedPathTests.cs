using ControlFS.Core.Text;

namespace ControlFS.UnitTests.Core;

public class TypedPathTests
{
    [Fact]
    public void Accepts_quoted_paths_and_environment_variables()
    {
        var temp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        Assert.Equal(temp, TypedPath.Normalize($"  \"{temp}\"  ").Path);
        Environment.SetEnvironmentVariable("CONTROLFS_TYPED_PATH_TEST", temp);
        Assert.Equal(Path.Join(temp, "x"), TypedPath.Normalize(@"%CONTROLFS_TYPED_PATH_TEST%\x").Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"Jogos\ROMs")]
    [InlineData(@"\\?\C:\Windows")]
    [InlineData(@"\\.\PhysicalDrive0")]
    public void Rejects_empty_relative_and_device_paths_with_a_message(string text)
    {
        var (path, error) = TypedPath.Normalize(text);
        Assert.Null(path);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
