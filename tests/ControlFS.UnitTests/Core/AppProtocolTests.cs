using ControlFS.Core.Automation;

namespace ControlFS.UnitTests.Core;

public class AppProtocolTests
{
    [Theory]
    [InlineData("controlfs://start", "start")]
    [InlineData("controlfs://start/", "start")]
    [InlineData("CONTROLFS://Start", "start")]
    [InlineData("controlfs:start", "start")]
    [InlineData("controlfs://start?foo=bar", "start")]
    [InlineData("controlfs://start#section", "start")]
    [InlineData("controlfs://open", "start")]
    [InlineData("controlfs://open/", "start")]
    [InlineData("controlfs:open", "start")]
    [InlineData("--start", "start")]
    [InlineData("--open", "start")]
    [InlineData("  --START  ", "start")]
    public void Start_actions_are_parsed_correctly(string input, string expected)
    {
        Assert.Equal(expected, AppProtocol.ParseAction(input));
    }

    [Theory]
    [InlineData("controlfs://stop", "stop")]
    [InlineData("controlfs://stop/", "stop")]
    [InlineData("CONTROLFS://Stop", "stop")]
    [InlineData("controlfs:stop", "stop")]
    [InlineData("controlfs://stop?force=1", "stop")]
    [InlineData("controlfs://close", "stop")]
    [InlineData("controlfs://close/", "stop")]
    [InlineData("controlfs:close", "stop")]
    [InlineData("controlfs://quit", "stop")]
    [InlineData("controlfs://quit/", "stop")]
    [InlineData("controlfs:quit", "stop")]
    [InlineData("--stop", "stop")]
    [InlineData("--close", "stop")]
    [InlineData("--quit", "stop")]
    [InlineData("  --STOP  ", "stop")]
    public void Stop_actions_are_parsed_correctly(string input, string expected)
    {
        Assert.Equal(expected, AppProtocol.ParseAction(input));
    }

    [Theory]
    [InlineData("controlfs://show", "show")]
    [InlineData("controlfs://show/", "show")]
    [InlineData("CONTROLFS://Show", "show")]
    [InlineData("controlfs:show", "show")]
    [InlineData("controlfs://show?target=1", "show")]
    [InlineData("--show", "show")]
    [InlineData("  --SHOW  ", "show")]
    public void Show_actions_are_parsed_correctly(string input, string expected)
    {
        Assert.Equal(expected, AppProtocol.ParseAction(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("controlfs://")]
    [InlineData("controlfs:")]
    [InlineData("controlfs://unknown")]
    [InlineData("controlfs://invalid_action")]
    [InlineData("consolemode://start")]
    [InlineData("http://controlfs://start")]
    [InlineData("steam://rungameid/123")]
    [InlineData("--unknown-flag")]
    [InlineData("C:\\Some\\Path\\File.txt")]
    public void Invalid_or_unsupported_inputs_return_null(string? input)
    {
        Assert.Null(AppProtocol.ParseAction(input));
    }

    [Fact]
    public void ParseActionFromArgs_finds_first_matching_action()
    {
        Assert.Null(AppProtocol.ParseActionFromArgs(null));
        Assert.Null(AppProtocol.ParseActionFromArgs([]));
        Assert.Null(AppProtocol.ParseActionFromArgs(["--no-onboarding", "--unknown"]));

        Assert.Equal("stop", AppProtocol.ParseActionFromArgs(["--no-onboarding", "--stop"]));
        Assert.Equal("start", AppProtocol.ParseActionFromArgs(["--no-onboarding", "controlfs://start"]));
        Assert.Equal("show", AppProtocol.ParseActionFromArgs(["controlfs://show", "--stop"]));
    }
}
