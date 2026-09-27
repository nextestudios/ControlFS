using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class FavoritesJourneyTests : IDisposable
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

    private static void FocusPlace(Driver d, int index)
    {
        while (d.App.PlacesFocus != index) d.Press(d.App.PlacesFocus < index ? InputAction.NavigateDown : InputAction.NavigateUp);
    }

    [Fact]
    public void Favorites_persist_come_first_on_home_and_in_the_picker_and_a_missing_one_is_kept_until_removed() => UiContext.Run(async () =>
    {
        var roms = _tmp.MakeDir("ROMs");
        var games = _tmp.MakeDir("Jogos");
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        var store = new JsonSettingsStore(_data.Path);
        store.Save(new AppSettings { RememberRecents = false }); // só favoritos e locais no início

        // Adicionar duas pastas pelo menu de ações do navegador
        var d = Boot(store);
        d.Press(InputAction.Confirm);
        foreach (var name in new[] { "ROMs", "Jogos" })
        {
            await d.FocusItem(name);
            d.Press(InputAction.OpenContextMenu);
            await d.ChooseMenu("Adicionar aos favoritos");
        }
        Assert.Equal([roms, games], store.Load().Settings.Favorites);

        // Novo "lançamento" com o mesmo armazenamento: favoritos primeiro no início
        d = Boot(store);
        var app = d.App;
        Assert.Equal(["ROMs", "Jogos", "Pasta de teste"], app.Places.Select(p => p.Name));

        // Reordenar: "Jogos" sobe e o foco o acompanha
        d.Press(InputAction.NavigateDown);
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Mover favorito para cima");
        Assert.Equal(["Jogos", "ROMs"], app.Places.Take(2).Select(p => p.Name));
        Assert.Equal(0, app.PlacesFocus);
        Assert.Equal([games, roms], store.Load().Settings.Favorites);

        // Seletor de pasta: favoritos aparecem primeiro em "Ir para outro local"
        d.Press(InputAction.NavigateDown);
        d.Press(InputAction.NavigateDown);
        d.Press(InputAction.Confirm); // pasta de teste
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Copiar para…");
        await d.Idle();
        Assert.Equal(Screen.FolderPicker, app.Screen);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Ir para outro local");
        var places = await d.WaitMenu();
        Assert.Equal(["Jogos", "ROMs"], places.Items.Take(2).Select(i => i.Label));
        d.Press(InputAction.Back);
        d.Press(InputAction.Back); // cancela o seletor

        // Pasta sumiu: ao voltar ao início continua na lista como indisponível, sem ser removida
        Directory.Delete(roms);
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(Screen.Home, app.Screen);
        var missing = app.Places[1];
        Assert.Equal("ROMs", missing.Name);
        Assert.True(missing.IsBlocked);
        Assert.Equal([games, roms], store.Load().Settings.Favorites);

        // Abrir o favorito indisponível oferece removê-lo explicitamente
        FocusPlace(d, 1);
        d.Press(InputAction.Confirm);
        var dialog = await d.WaitDialog("Favorito indisponível");
        d.ChooseOption(dialog, "Remover dos favoritos");
        Assert.Equal([games], store.Load().Settings.Favorites);

        // Remover pelo menu de ações do início
        FocusPlace(d, 0);
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Remover dos favoritos");
        Assert.Empty(store.Load().Settings.Favorites);
        Assert.Equal(["Pasta de teste"], app.Places.Select(p => p.Name));
    });
}
