using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Painel de detalhes da lista (redesenho, fase C): dados reais por tipo de item, calculados só com o painel à mostra.</summary>
public class DetailsPanelJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    private sealed class FakeDecoder : IImageDecoder
    {
        public List<(string Name, int MaxSide)> Decoded { get; } = [];

        public Task<PreviewImage> DecodeAsync(string path, int maxSide, CancellationToken cancellationToken)
        {
            Decoded.Add((Path.GetFileName(path), maxSide));
            return Task.FromResult(new PreviewImage(2, 2, new byte[16]));
        }
    }

    private static string? Value(ItemDetails details, DetailsIcon icon, string? label = null) =>
        details.Lines.FirstOrDefault(l => l.Icon == icon && (label is null || l.Label == label))?.Value;

    private async Task<(Driver Driver, TestFileSystem Fs, FakeDecoder Decoder)> Open(bool panel = true)
    {
        var fs = new TestFileSystem(_tmp.Path);
        var decoder = new FakeDecoder();
        var app = new AppController(fs, new ArchiveService(), imageDecoder: decoder) { DetailsDelay = TimeSpan.Zero };
        app.Start();
        app.SetDetailsPanelVisible(panel);
        var d = new Driver(app);
        await d.Idle();
        d.Press(InputAction.Confirm);
        await d.Idle();
        return (d, fs, decoder);
    }

    [Fact]
    public void Folder_details_show_the_real_count_and_size_computed_off_the_ui_thread_only_for_the_focused_folder() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("Jogos", "salvos");
        File.WriteAllBytes(_tmp.Sub("Jogos", "a.bin"), new byte[1000]);
        File.WriteAllBytes(_tmp.Sub("Jogos", "salvos", "b.bin"), new byte[234]);
        _tmp.MakeDir("A-vazia"); // primeiro da lista: focado (e medido) ao abrir
        var (d, fs, _) = await Open();
        var app = d.App;

        // Pasta focada: "Calculando…" enquanto soma (presa aqui), depois os valores reais (2 arquivos + 1 pasta).
        fs.HoldMeasureUntilCancelled = true;
        await d.FocusItem("Jogos");
        await UiContext.WaitUntil(() => fs.MeasureCalls > 0, "soma iniciada");
        var calculating = app.Details!;
        Assert.Equal((DetailsKind.Folder, "Jogos", "Pasta"), (calculating.Kind, calculating.Title, calculating.Subtitle));
        Assert.Equal(_tmp.Sub("Jogos"), Value(calculating, DetailsIcon.Location));
        Assert.Equal("Calculando…", Value(calculating, DetailsIcon.Items));

        // Mudar o foco cancela a soma do item anterior (nada fica guardado como pronto).
        d.Press(InputAction.NavigateUp); // "A-vazia" (sem esperar a soma presa)
        Assert.Equal("A-vazia", app.Browser.List.Focused!.Name);
        await d.Idle();
        Assert.Equal(FolderStatsState.Calculating, app.FolderStatsFor(_tmp.Sub("Jogos")).State);

        fs.HoldMeasureUntilCancelled = false;
        await d.FocusItem("Jogos");
        await d.Idle();
        var ready = app.Details!;
        Assert.Equal("3 itens", Value(ready, DetailsIcon.Items));
        Assert.Equal("1,2 KB", Value(ready, DetailsIcon.Size));
        Assert.NotNull(Value(ready, DetailsIcon.Date, "Modificado em"));

        // Guardado: voltar ao item não relê o disco; com o painel escondido nada é medido.
        var calls = fs.MeasureCalls;
        await d.FocusItem("A-vazia");
        await d.Idle();
        await d.FocusItem("Jogos");
        await d.Idle();
        Assert.Equal(calls, fs.MeasureCalls);
        app.SetDetailsPanelVisible(false);
        _tmp.MakeDir("Outra");
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Atualizar");
        await d.FocusItem("Outra");
        await d.Idle();
        Assert.Equal(calls, fs.MeasureCalls);

        // Marcados: quantos e quanto somam os arquivos.
        app.SetDetailsPanelVisible(true);
        File.WriteAllBytes(_tmp.Sub("x.bin"), new byte[2048]);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Atualizar");
        await d.FocusItem("x.bin");
        d.Press(InputAction.ToggleSelection);
        Assert.Equal("1 item marcado · 2 KB", Value(app.Details!, DetailsIcon.Marked));
    });

    [Fact]
    public void File_image_and_archive_details_come_from_the_content_with_the_preview_limits() => UiContext.Run(async () =>
    {
        ZipFixtures.Create(_tmp.Sub("pacote.zip"), ZipFixtures.Dir("docs"), ZipFixtures.Text("docs/um.txt", "1"), ZipFixtures.Text("dois.txt", "2"));
        File.WriteAllBytes(_tmp.Sub("foto.png"), ImageFixtures.Png(640, 480));
        File.WriteAllBytes(_tmp.Sub("gigante.png"), ImageFixtures.Png(100_000, 100_000));
        File.WriteAllText(_tmp.Sub("notas.txt"), "texto");
        File.WriteAllText(_tmp.Sub("falso.zip"), "não é um zip");
        var (d, _, decoder) = await Open();
        var app = d.App;

        await d.FocusItem("notas.txt");
        await d.Idle();
        var text = app.Details!;
        Assert.Equal((DetailsKind.File, "Arquivo TXT"), (text.Kind, text.Subtitle));
        Assert.Equal(_tmp.Sub("notas.txt"), Value(text, DetailsIcon.Location));
        Assert.Equal("5 B", Value(text, DetailsIcon.Size));

        // Compactado: formato pelo conteúdo e arquivos contados pelo índice do ZIP (sem extrair).
        await d.FocusItem("pacote.zip");
        await d.Idle();
        var zip = app.Details!;
        Assert.Equal((DetailsKind.Archive, "Arquivo ZIP"), (zip.Kind, zip.Subtitle));
        Assert.Equal("ZIP", Value(zip, DetailsIcon.Format, "Formato"));
        Assert.Equal("2 arquivos", Value(zip, DetailsIcon.Items));
        await d.FocusItem("falso.zip");
        await d.Idle();
        Assert.Equal("não reconhecido pelo conteúdo", Value(app.Details!, DetailsIcon.Warning, "Formato"));

        // Imagem: formato e dimensões do cabeçalho e a miniatura pelo decodificador da visualização.
        await d.FocusItem("foto.png");
        await d.Idle();
        var image = app.Details!;
        Assert.Equal(DetailsKind.Image, image.Kind);
        Assert.Equal("PNG", Value(image, DetailsIcon.Format, "Formato"));
        Assert.Equal("640 × 480 px", Value(image, DetailsIcon.Dimensions, "Dimensões"));
        Assert.NotNull(image.Thumbnail);
        Assert.Equal([("foto.png", AppController.ThumbnailSide)], decoder.Decoded);

        // Imagem acima dos limites: recusada antes de decodificar (o decodificador nunca a recebe).
        await d.FocusItem("gigante.png");
        await d.Idle();
        var bomb = app.Details!;
        Assert.Null(bomb.Thumbnail);
        Assert.StartsWith("Sem miniatura: Resolução alta demais", bomb.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(decoder.Decoded, x => x.Name == "gigante.png");

        // Dentro do compactado: pasta com a contagem da árvore e arquivo com o tamanho descompactado.
        await d.FocusItem("pacote.zip");
        d.Press(InputAction.Confirm);
        await d.Idle();
        await d.FocusItem("docs");
        var folder = app.Details!;
        Assert.Equal((DetailsKind.Folder, "Pasta no compactado"), (folder.Kind, folder.Subtitle));
        Assert.Equal("pacote.zip › docs", Value(folder, DetailsIcon.Location, "No compactado"));
        Assert.Equal("1 item", Value(folder, DetailsIcon.Items));
        await d.FocusItem("dois.txt");
        Assert.Equal(DetailsKind.ArchiveEntry, app.Details!.Kind);
        Assert.Equal("1 B", Value(app.Details!, DetailsIcon.Size));
    });

    [Fact]
    public void Home_places_and_drives_show_system_folder_kind_real_sums_and_volume_usage() => UiContext.Run(async () =>
    {
        File.WriteAllBytes(_tmp.Sub("a.bin"), new byte[1000]);
        var fs = new TestFileSystem(_tmp.Path);
        fs.Drives.Add(new FileEntry("drive:D:\\", "Jogos (D:)", EntryKind.Drive, FullPath: _tmp.MakeDir("D"), Drive: DriveKind.Fixed,
            Volume: new VolumeInfo(1000L << 30, 250L << 30, "NTFS")));
        var store = new JsonSettingsStore(_data.Path);
        store.Save(new AppSettings { RememberRecents = false });
        var app = new AppController(fs, new ArchiveService(), store) { DetailsDelay = TimeSpan.Zero };
        app.Start();
        app.SetDetailsPanelVisible(true);
        await app.WhenIdleAsync();

        // Pasta principal no início (lista): a mesma soma real dos cartões da grade.
        var home = app.Details!;
        Assert.Equal((DetailsKind.Folder, "Pasta de teste", "Pasta do sistema"), (home.Kind, home.Title, home.Subtitle));
        Assert.Equal("2 itens", Value(home, DetailsIcon.Items)); // a.bin e a pasta D
        Assert.Equal("1000 B", Value(home, DetailsIcon.Size));

        var d = new Driver(app);
        d.Press(InputAction.NavigateDown);
        var drive = app.Details!;
        Assert.Equal((DetailsKind.Drive, "Jogos (D:)", "Unidade local"), (drive.Kind, drive.Title, drive.Subtitle));
        Assert.Equal("NTFS", Value(drive, DetailsIcon.FileSystem, "Sistema de arquivos"));
        Assert.Equal("1000 GB", Value(drive, DetailsIcon.Size, "Capacidade"));
        Assert.Equal("250 GB (25%)", Value(drive, DetailsIcon.Free, "Livre"));
        Assert.Equal(0.75, drive.Volume!.UsedFraction, 3);
    });

    [Fact]
    public void Grid_panel_follows_focus_summarizes_marks_without_a_focused_item_and_the_menu_toggle_is_saved_per_view() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("Jogos");
        File.WriteAllBytes(_tmp.Sub("Jogos", "a.bin"), new byte[1000]);
        File.WriteAllBytes(_tmp.Sub("b.bin"), new byte[2048]);
        File.WriteAllBytes(_tmp.Sub("c.bin"), new byte[1024]);
        var fs = new TestFileSystem(_tmp.Path);
        var store = new JsonSettingsStore(_data.Path);
        store.Save(new AppSettings { RememberRecents = false });
        var app = new AppController(fs, new ArchiveService(), store) { DetailsDelay = TimeSpan.Zero };
        app.Start();
        var d = new Driver(app);
        await d.Idle();
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.ChangeView);
        app.SetGridLayout(2, 3);
        app.SetDetailsPanelVisible(true, fits: true); // a view: grade com espaço para o painel ao lado
        await d.Idle();

        // Grade: o item focado é descrito (a pasta medida em segundo plano, como na lista) e o painel segue as setas 2D.
        Assert.True(app.IsGrid);
        var folder = app.Details!;
        Assert.Equal((DetailsKind.Folder, "Jogos"), (folder.Kind, folder.Title));
        Assert.Equal("1 item", Value(folder, DetailsIcon.Items));
        Assert.Equal("1000 B", Value(folder, DetailsIcon.Size));
        d.Press(InputAction.NavigateRight);
        Assert.Equal(("b.bin", "2 KB"), (app.Details!.Title, Value(app.Details!, DetailsIcon.Size)));
        Assert.Equal(PaneRegion.List, app.FocusRegion); // o painel nunca tira o foco da grade

        // Marcados: o painel diz se o item em foco está entre eles.
        d.Press(InputAction.ToggleSelection);
        d.Press(InputAction.NavigateDown); // c.bin, na linha de baixo
        d.Press(InputAction.ToggleSelection);
        var marked = app.Details!.Lines.Single(l => l.Icon == DetailsIcon.Marked);
        Assert.Equal(("c.bin", "Este item está marcado", "2 itens marcados · 3 KB"), (app.Details!.Title, marked.Label, marked.Value));
        await d.FocusItem("Jogos");
        // Caixa vazia, não o símbolo de marcado (auditoria de UX, P3).
        Assert.Equal("Este item não está marcado", app.Details!.Lines.Single(l => l.Icon == DetailsIcon.Unmarked).Label);
        Assert.DoesNotContain(app.Details!.Lines, l => l.Icon == DetailsIcon.Marked);

        // Foco fora da grade (barra superior): só o resumo dos marcados, sem apontar nenhum item como focado.
        d.Press(InputAction.PreviousRegion);
        Assert.NotEqual(PaneRegion.List, app.FocusRegion);
        var summary = app.Details!;
        Assert.Equal((DetailsKind.Selection, "2 itens marcados", "Nenhum item em foco"), (summary.Kind, summary.Title, summary.Subtitle));
        Assert.Equal(("2 arquivos", "3 KB"), (Value(summary, DetailsIcon.Items), Value(summary, DetailsIcon.Size)));
        for (var i = 0; i < 5 && app.FocusRegion != PaneRegion.List; i++) d.Press(InputAction.Back);
        Assert.Equal(PaneRegion.List, app.FocusRegion);

        // Configurações: o valor segue o estado; esconder guarda a escolha da grade sem mexer no foco nem nas marcas.
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Painel de detalhes: visível");
        Assert.False(app.DetailsPanelVisible);
        Assert.Equal(("Jogos", 2), (app.Browser.List.Focused!.Name, app.Browser.List.SelectionCount));
        Assert.Equal((false, (bool?)null), (store.Load().Settings.GridDetails, store.Load().Settings.ListDetails));
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Configurações"); // o ajuste mora em Menu → Configurações (#193)
        Assert.Contains((await d.WaitMenu()).Items, i => i.Label == "Painel de detalhes: oculto");
        await d.ChooseMenu("Painel de detalhes: oculto"); // de volta ao que cabe: automático
        d.Press(InputAction.Back); // Configurações continua aberto depois de alternar
        Assert.True(app.DetailsPanelVisible);
        Assert.Null(store.Load().Settings.GridDetails);

        // Portátil/janela estreita (sem espaço: escondido por padrão): mostrar pelo menu fica salvo e vale ao reabrir.
        app.SetDetailsPanelVisible(false, fits: false);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Painel de detalhes: oculto");
        Assert.True(app.DetailsPanelVisible);
        Assert.Equal("Jogos", app.Browser.List.Focused!.Name);
        var reopened = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), store);
        reopened.Start();
        Assert.True(reopened.DetailsPanelPreference);

        // A lista tem a própria escolha (automática aqui).
        d.Press(InputAction.ChangeView);
        Assert.Null(app.DetailsPanelPreference);
    });
}
