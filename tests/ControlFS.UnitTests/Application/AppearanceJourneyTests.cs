using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Appearance;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class AppearanceJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    [Fact]
    public void Theme_and_accent_are_chosen_in_settings_apply_at_once_and_persist() => UiContext.Run(async () =>
    {
        var store = new JsonSettingsStore(_data.Path);
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
        var applied = new List<(ThemeMode, AccentColor)>();
        app.SettingsChanged += s => applied.Add((s.Theme, s.Accent));
        app.Start();
        var d = new Driver(app);
        Assert.Equal((ThemeMode.System, AccentColor.Cyan), (app.Settings.Theme, app.Settings.Accent)); // segue o Windows

        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Configurações");
        await d.ChoosePick("Tema", "escuro");
        Assert.Equal("Configurações", app.TopModal?.Title); // continua aberto para ver o resultado e seguir trocando
        await d.ChoosePick("Tema", "claro");
        await d.ChoosePick("Cor de destaque", "azul");
        Assert.Equal((ThemeMode.Light, AccentColor.Blue), (app.Settings.Theme, app.Settings.Accent));
        Assert.Equal((ThemeMode.Light, AccentColor.Blue), applied[^1]); // a janela recebe cada troca na hora

        var relaunched = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
        relaunched.Start();
        Assert.Equal((ThemeMode.Light, AccentColor.Blue), (relaunched.Settings.Theme, relaunched.Settings.Accent));
    });
}
