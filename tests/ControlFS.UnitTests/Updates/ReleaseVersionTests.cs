using ControlFS.Core.Models;

namespace ControlFS.UnitTests.Updates;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("0.1.0-alpha.1", "0.1.0-alpha.2")]
    [InlineData("0.9.9", "0.10.0")]
    [InlineData("v1.2.3", "1.2.4+build.7")]
    public void Orders_by_semver_precedence(string lower, string higher)
    {
        Assert.True(ReleaseVersion.Parse(lower) < ReleaseVersion.Parse(higher));
        Assert.True(ReleaseVersion.Parse(higher) > ReleaseVersion.Parse(lower));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-alpha..1")]
    [InlineData("1.2.3-01")]
    [InlineData("")]
    public void Rejects_invalid_versions(string text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void Build_metadata_is_ignored() => Assert.Equal(ReleaseVersion.Parse("1.0.0+abc"), ReleaseVersion.Parse("1.0.0"));
}
