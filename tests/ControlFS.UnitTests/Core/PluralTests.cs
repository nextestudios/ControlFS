using ControlFS.Core.Text;

namespace ControlFS.UnitTests.Core;

public class PluralTests
{
    [Fact]
    public void Agrees_number_with_singular_for_one_and_plural_for_zero_and_many()
    {
        Assert.Equal("1 item", Plural.Of(1, "item", "itens"));
        Assert.Equal("3 itens", Plural.Of(3, "item", "itens"));
        Assert.Equal("0 itens", Plural.Of(0, "item", "itens"));
        Assert.Equal("foi omitido", Plural.Word(1, "foi omitido", "foram omitidos"));
        Assert.Equal("foram omitidos", Plural.Word(2, "foi omitido", "foram omitidos"));
    }
}
