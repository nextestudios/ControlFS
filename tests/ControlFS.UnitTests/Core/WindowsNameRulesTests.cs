using ControlFS.Core.Policies;

namespace ControlFS.UnitTests.Core;

public class WindowsNameRulesTests
{
    [Theory]
    [InlineData("Relatório de ação")]
    [InlineData("Músicas 2026")]
    [InlineData("a.b.c")]
    [InlineData("CONSOLE")]
    [InlineData("COM10")]
    [InlineData(".gitignore")]
    public void Accepts_valid_names(string name) => Assert.True(WindowsNameRules.ValidateComponent(name).IsValid, name);

    [Theory]
    [InlineData("", NameProblem.Empty)]
    [InlineData(".", NameProblem.DotOrDotDot)]
    [InlineData("..", NameProblem.DotOrDotDot)]
    [InlineData("CON", NameProblem.ReservedDeviceName)]
    [InlineData("con.txt", NameProblem.ReservedDeviceName)]
    [InlineData("NUL .log", NameProblem.ReservedDeviceName)]
    [InlineData("COM1", NameProblem.ReservedDeviceName)]
    [InlineData("LPT¹", NameProblem.ReservedDeviceName)]
    [InlineData("nome.", NameProblem.TrailingDotOrSpace)]
    [InlineData("nome ", NameProblem.TrailingDotOrSpace)]
    [InlineData("a<b", NameProblem.InvalidCharacter)]
    [InlineData("a|b", NameProblem.InvalidCharacter)]
    [InlineData("a?b", NameProblem.InvalidCharacter)]
    [InlineData("a/b", NameProblem.InvalidCharacter)]
    [InlineData("a\\b", NameProblem.InvalidCharacter)]
    [InlineData("file.txt:Zone.Identifier", NameProblem.StreamSyntax)]
    [InlineData("a\u0001b", NameProblem.ControlCharacter)]
    public void Rejects_invalid_names(string name, NameProblem expected) => Assert.Equal(expected, WindowsNameRules.ValidateComponent(name).Problem);

    [Fact]
    public void Rejects_names_longer_than_255() => Assert.Equal(NameProblem.TooLong, WindowsNameRules.ValidateComponent(new string('a', 256)).Problem);

    [Fact]
    public void Collision_key_unifies_case_and_unicode_normalization()
    {
        var composed = "ação";
        var decomposed = "ac\u0327a\u0303o";
        Assert.NotEqual(composed, decomposed);
        Assert.Equal(WindowsNameRules.CollisionKey(composed), WindowsNameRules.CollisionKey(decomposed.ToUpperInvariant()));
    }

    [Fact]
    public void Unique_names_preserve_extension()
    {
        var existing = new HashSet<string> { "a.txt", "a (2).txt" };
        Assert.Equal("a (3).txt", UniqueNames.Next("a.txt", existing.Contains));
        Assert.Equal("pasta.v2 (2)", UniqueNames.Next("pasta.v2", n => n == "pasta.v2", isDirectory: true));
    }
}
