using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Application;

public class RecentsJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    private Driver Boot(JsonSettingsStore store)
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store, fileOperations: new FileOperationService());
        app.Start();
        return new Driver(app);
    }

    private static void FocusPlace(Driver d, string name)
    {
        var index = d.App.Places.ToList().FindIndex(p => p.Name == name);
        Assert.True(index >= 0, $"Local \"{name}\" ausente: {string.Join(", ", d.App.Places.Select(p => p.Name))}");
        while (d.App.PlacesFocus != index) d.Press(d.App.PlacesFocus < index ? InputAction.NavigateDown : InputAction.NavigateUp);
    }

    [Fact]
    public void Recent_folders_and_archives_persist_reopen_from_home_are_bounded_and_can_be_cleared_or_disabled() => UiContext.Run(async () =>
    {
        var roms = _tmp.MakeDir("ROMs");
        var archive = _tmp.Sub("ROMs", "jogo.zip");
        Create(archive, Text("leia-me.txt", "olá"));
        var store = new JsonSettingsStore(_data.Path);

        // Nada aberto ainda: o início não mostra "Recentes"
        var d = Boot(store);
        Assert.DoesNotContain(d.App.Places, p => p.Name == "Recentes");

        // Abrir a pasta de teste, ROMs e o compactado
        d.Press(InputAction.Confirm);
        await d.FocusItem("ROMs");
        d.Press(InputAction.Confirm);
        await d.FocusItem("jogo.zip");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<ArchiveLocation>(d.App.Browser.Location);
        var saved = store.Load().Settings;
        Assert.Equal(["ROMs", Path.GetFileName(_tmp.Path)], saved.RecentFolders.Select(Path.GetFileName));
        Assert.Equal(["jogo.zip"], saved.RecentFiles.Select(Path.GetFileName));

        // Novo lançamento: Início → Recentes → Confirmar reabre o compactado
        d = Boot(store);
        FocusPlace(d, "Recentes");
        d.Press(InputAction.Confirm);
        await d.ChooseMenu("jogo.zip");
        await d.Idle();
        Assert.Equal(new ArchiveLocation(saved.RecentFiles[0], string.Empty), d.App.Browser.Location);

        // Lista limitada: pastas demais descartam as mais antigas
        for (var i = 0; i < AppController.MaxRecents + 2; i++)
        {
            var folder = _tmp.MakeDir("extra" + i);
            d.App.OpenPhysical(folder);
            await d.Idle();
        }
        Assert.Equal(AppController.MaxRecents, d.App.RecentFolders.Count);
        Assert.Equal("extra" + (AppController.MaxRecents + 1), Path.GetFileName(d.App.RecentFolders[0]));

        // Limpar pelo menu de ações do início
        d.App.GoHome();
        FocusPlace(d, "Recentes");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Limpar recentes");
        Assert.Empty(store.Load().Settings.RecentFolders);
        Assert.Empty(store.Load().Settings.RecentFiles);
        Assert.DoesNotContain(d.App.Places, p => p.Name == "Recentes");

        // Desligado: navegar não grava nada
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Recentes: lembrar");
        Assert.False(store.Load().Settings.RememberRecents);
        d.App.OpenPhysical(roms);
        await d.Idle();
        Assert.Empty(store.Load().Settings.RecentFolders);
    });
}
