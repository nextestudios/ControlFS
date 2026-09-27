using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class AboutJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void About_shows_version_license_and_source_and_closes_with_back() => UiContext.Run(async () =>
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Sobre o ControlFS");

        var about = Assert.IsType<AboutModal>(app.TopModal);
        Assert.Contains(about.Lines, l => l.Label == "Versão" && l.Value == app.AppVersion);
        Assert.Contains(about.Lines, l => l.Label == "Licença" && l.Value.Contains("AGPL-3.0-only", StringComparison.Ordinal));
        Assert.Contains(about.Lines, l => l.Value == AppController.SourceUrl);
        Assert.Contains(app.Hints, h => h is { Action: InputAction.Back, Label: "Fechar" });

        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
    });
}
