using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Início em grade (redesenho, fase B): contagens reais, seções com navegação 2D e Meu computador.</summary>
public class HomeGridJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    private AppController Boot(TestFileSystem fs, bool recents = true)
    {
        var store = new JsonSettingsStore(_data.Path);
        store.Save(new AppSettings { View = ViewMode.Grid, RememberRecents = recents });
        var app = new AppController(fs, new ArchiveService(), store);
        app.Start();
        return app;
    }

    [Fact]
    public void Main_folder_cards_show_real_counts_computed_off_the_ui_thread_cancelled_when_leaving_home_and_cached() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("salvos");
        File.WriteAllBytes(_tmp.Sub("a.bin"), new byte[1000]);
        File.WriteAllBytes(_tmp.Sub("salvos", "b.bin"), new byte[234]);
        var fs = new TestFileSystem(_tmp.Path) { HoldMeasureUntilCancelled = true };
        var app = Boot(fs, recents: false);
        var d = new Driver(app);
        var folder = app.Places[app.PlacesFocus];
        Assert.Equal("Pasta de teste", folder.Name);

        // Enquanto a soma roda (presa aqui), o cartão diz "Calculando…" e a tela segue respondendo.
        await UiContext.WaitUntil(() => fs.MeasureCalls == 1, "soma iniciada");
        Assert.Equal((_tmp.Path, "Calculando…"), app.DescribePlace(folder));
        Assert.Contains("Calculando…", app.DescribeFocus().Item);

        // Sair do início cancela a soma (nada fica guardado como pronto).
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(Screen.Browser, app.Screen);
        Assert.Equal(FolderStatsState.Calculating, app.FolderStatsFor(_tmp.Path).State);

        // De volta ao início: soma de novo, agora até o fim, com os valores reais (2 arquivos + 1 pasta, 1234 bytes).
        fs.HoldMeasureUntilCancelled = false;
        app.GoHome();
        await d.Idle();
        Assert.Equal(new FolderStats(FolderStatsState.Ready, 3, 1234), app.FolderStatsFor(_tmp.Path));
        Assert.Equal("3 itens • 1,2 KB", app.DescribePlace(folder).Secondary);
        var calls = fs.MeasureCalls;

        // Resultado guardado: voltar ao início não relê o disco.
        d.Press(InputAction.Confirm);
        await d.Idle();
        app.GoHome();
        await d.Idle();
        Assert.Equal(calls, fs.MeasureCalls);

        // Pasta enorme: passado o tempo limite fica o parcial real, marcado com "+".
        fs.HoldMeasureUntilCancelled = true;
        var slow = new AppController(fs, new ArchiveService(), new JsonSettingsStore(_data.Path)) { FolderStatsBudget = TimeSpan.FromMilliseconds(50) };
        slow.Start();
        await slow.WhenIdleAsync();
        Assert.Equal("1 item+ • 1 B+", slow.DescribePlace(slow.Places.Single(p => p.Name == "Pasta de teste")).Secondary);
    });

    [Fact]
    public void Home_sections_navigate_in_2d_switching_views_keeps_focus_and_marks_and_this_pc_lists_drives_with_history() => UiContext.Run(async () =>
    {
        var fs = new TestFileSystem(_tmp.Path);
        var c = _tmp.MakeDir("C");
        File.WriteAllText(Path.Join(c, "a.txt"), "a");
        File.WriteAllText(Path.Join(c, "b.txt"), "b");
        fs.Drives.Add(new FileEntry("drive:C:\\", "Sistema (C:)", EntryKind.Drive, FullPath: c, Drive: DriveKind.Fixed, Volume: new VolumeInfo(1000L << 30, 250L << 30, "NTFS")));
        fs.Drives.Add(new FileEntry("drive:E:\\", "PENDRIVE (E:)", EntryKind.Drive, FullPath: _tmp.MakeDir("E"), Drive: DriveKind.Removable, Volume: new VolumeInfo(32L << 30, 8L << 30, "exFAT")));
        var app = Boot(fs);
        var d = new Driver(app);
        await d.Idle();

        // Visitar uma pasta cria "Recentes" (seção "Outros locais", depois das unidades).
        d.Press(InputAction.Confirm);
        await d.Idle();
        app.GoHome();
        await d.Idle();
        Assert.Equal(["Pastas principais", "Unidades e dispositivos", "Outros locais"], app.HomeSections.Select(s => s.Title));
        app.SetHomeGridLayout(new Dictionary<HomeSectionKind, int> { [HomeSectionKind.Folders] = 3, [HomeSectionKind.Drives] = 2, [HomeSectionKind.Other] = 3 });
        string Focused() => app.Places[app.PlacesFocus].Name;
        Assert.Equal("Pasta de teste", Focused());
        Assert.Equal(("250 GB livres de 1000 GB · NTFS", (string?)null), app.DescribePlace(app.Places.Single(p => p.Name == "Sistema (C:)")));
        Assert.Equal("USB · 8 GB livres de 32 GB · exFAT", app.DescribePlace(app.Places.Single(p => p.Name == "PENDRIVE (E:)")).Primary);

        // Baixo troca de seção; direita anda na linha; baixo de novo cai na coluna possível; cima volta pela mesma coluna.
        d.Press(InputAction.NavigateDown);
        Assert.Equal("Sistema (C:)", Focused());
        d.Press(InputAction.NavigateRight);
        Assert.Equal("PENDRIVE (E:)", Focused());
        d.Press(InputAction.NavigateRight); // fim da seção: próxima seção
        Assert.Equal("Recentes", Focused());
        d.Press(InputAction.NavigateUp);
        Assert.Equal("Sistema (C:)", Focused());
        d.Press(InputAction.NavigateUp);
        Assert.Equal("Pasta de teste", Focused());
        d.Press(InputAction.NavigateLeft); // primeiro cartão: fica
        Assert.Equal("Pasta de teste", Focused());
        d.Press(InputAction.PageDown); // LT/RT: começo da próxima seção
        Assert.Equal("Sistema (C:)", Focused());
        Assert.Contains("250 GB livres de 1000 GB", app.DescribeFocus().Item);

        // Grade ↔ lista no início: o mesmo local continua focado.
        d.Press(InputAction.ChangeView);
        Assert.False(app.IsGrid);
        Assert.Equal("Sistema (C:)", Focused());
        d.Press(InputAction.ChangeView);

        // Numa pasta: marcas e foco sobrevivem à troca de exibição (sem reler o disco).
        d.Press(InputAction.Confirm);
        await d.Idle();
        await d.FocusItem("a.txt");
        d.Press(InputAction.ToggleSelection);
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.ToggleSelection);
        var items = app.Browser.List.Items;
        d.Press(InputAction.ChangeView);
        Assert.Same(items, app.Browser.List.Items);
        Assert.Equal(["a.txt", "b.txt"], app.Browser.List.SelectedEntries.Select(e => e.Name).Order());
        Assert.Equal("b.txt", app.Browser.List.Focused!.Name);
        d.Press(InputAction.ChangeView);
        Assert.Equal(2, app.Browser.List.SelectionCount);
        d.Press(InputAction.Back); // limpa a marcação

        // Meu computador (acesso rápido): as unidades na mesma aba, com histórico; abrir uma e voltar foca a mesma.
        d.Press(InputAction.PreviousRegion);
        while (app.FocusRegion != PaneRegion.QuickAccess || app.QuickAccess[app.QuickAccessFocus].Label != "Meu computador") d.Press(InputAction.NavigateRight);
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<ThisPcLocation>(app.Browser.Location);
        Assert.Equal("Meu computador", AppController.TabTitle(app.Browser));
        Assert.Equal(["Sistema (C:)", "PENDRIVE (E:)"], app.Browser.List.Items.Select(i => i.Name));
        Assert.True(app.IsQuickAccessActive(app.QuickAccess.Single(q => q.Kind == QuickAccessKind.ThisPc)));
        d.Press(InputAction.NavigateRight);
        Assert.Equal("Meu computador", app.DescribeFocus().Context);
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(_tmp.Sub("E"), ((PhysicalLocation)app.Browser.Location!).FullPath);
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.IsType<ThisPcLocation>(app.Browser.Location);
        Assert.Equal("PENDRIVE (E:)", app.Browser.List.Focused!.Name);
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(c, ((PhysicalLocation)app.Browser.Location!).FullPath);
    });
}
