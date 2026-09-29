using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Lista (redesenho, fase C): datas amigáveis, cabeçalho das colunas, foco ao abrir outro local e troca de exibição.</summary>
public class ListModeJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Friendly_dates_say_today_and_yesterday_and_otherwise_the_full_date()
    {
        var now = new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Local);
        DateTimeOffset At(int day, int hour, int minute, int month = 9, int year = 2026) => new(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local));

        Assert.Equal("Hoje, 00:00", EntryText.FriendlyDate(At(27, 0, 0), now));
        Assert.Equal("Hoje, 14:32", EntryText.FriendlyDate(At(27, 14, 32), now)); // mais tarde hoje (relógio adiantado): ainda hoje
        Assert.Equal("Ontem, 23:59", EntryText.FriendlyDate(At(26, 23, 59), now));
        Assert.Equal("Ontem, 18:05", EntryText.FriendlyDate(At(26, 18, 5), now));
        Assert.Equal("25/09/2026, 20:11", EntryText.FriendlyDate(At(25, 20, 11), now));
        Assert.Equal("31/12/2025, 09:45", EntryText.FriendlyDate(At(31, 9, 45, month: 12, year: 2025), now));
        Assert.Equal("28/09/2026, 08:00", EntryText.FriendlyDate(At(28, 8, 0), now)); // amanhã: data completa
        // Virada do ano: 1º de janeiro, "ontem" é 31 de dezembro.
        Assert.Equal("Ontem, 22:10", EntryText.FriendlyDate(At(31, 22, 10, month: 12, year: 2025), new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void Column_header_follows_the_sorting_from_the_menu_and_from_the_pointer_and_shows_the_marks() => UiContext.Run(async () =>
    {
        File.WriteAllBytes(_tmp.Sub("b-grande.bin"), new byte[3000]);
        File.WriteAllBytes(_tmp.Sub("a-medio.bin"), new byte[2000]);
        File.WriteAllBytes(_tmp.Sub("c-pequeno.txt"), new byte[10]);
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);

        // Início: ordem fixa (sem seta) e nada para marcar (sem caixa).
        Assert.Equal(new ListHeader(null, false, MarkAllState.None), app.ListHeader);

        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(new ListHeader(new SortOrder(SortField.Name), true, MarkAllState.None), app.ListHeader);

        // Menu → Ordenar por / Ordem: a seta vai para a coluna escolhida e vira.
        d.Press(InputAction.OpenAppMenu);
        await d.ChoosePick("Ordenar por", "tipo");
        Assert.Equal(SortField.Type, app.ListHeader.Sort!.Field);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Ordem");
        Assert.Equal(new SortOrder(SortField.Type, Descending: true), app.ListHeader.Sort);

        // Toque no título "Tamanho": ordena por tamanho (crescente) com o foco no mesmo item; de novo, inverte.
        await d.FocusItem("a-medio.bin");
        app.PointerSortBy(SortField.Size);
        Assert.Equal(new SortOrder(SortField.Size), app.ListHeader.Sort);
        Assert.Equal(["c-pequeno.txt", "a-medio.bin", "b-grande.bin"], app.Browser.List.Items.Select(i => i.Name));
        Assert.Equal("a-medio.bin", app.Browser.List.Focused!.Name);
        app.PointerSortBy(SortField.Size);
        Assert.Equal(new SortOrder(SortField.Size, Descending: true), app.ListHeader.Sort);
        Assert.Equal("b-grande.bin", app.Browser.List.Items[0].Name);
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Configurações"); // a ordenação mora em Menu → Configurações (#193)
        Assert.Contains(((MenuModal)app.TopModal!).Items, i => i.Label == "Ordenar por: tamanho");
        Assert.Contains(((MenuModal)app.TopModal!).Items, i => i.Label == "Ordem: decrescente");
        d.Press(InputAction.Back);

        // Caixa do cabeçalho: nenhuma, algumas, todas; tocar nela marca todos e, com todos marcados, limpa.
        d.Press(InputAction.ToggleSelection);
        Assert.Equal(MarkAllState.Some, app.ListHeader.Marks);
        app.PointerToggleMarkAll();
        Assert.Equal(MarkAllState.All, app.ListHeader.Marks);
        Assert.Equal(3, app.Browser.List.SelectionCount);
        app.PointerToggleMarkAll();
        Assert.Equal(MarkAllState.None, app.ListHeader.Marks);
    });

    [Fact]
    public void Opening_another_location_focuses_its_first_item_while_back_and_up_restore_the_previous_one() => UiContext.Run(async () =>
    {
        // A pasta de dentro tem itens com os nomes dos vizinhos de fora: o foco não pode "pular" para eles nem ficar na
        // mesma posição da lista anterior.
        foreach (var name in new[] { "a.txt", "b.txt", "d.txt", "e.txt" }) File.WriteAllText(_tmp.Sub(name), name);
        _tmp.MakeDir("c");
        foreach (var name in new[] { "1.txt", "2.txt", "d.txt", "e.txt", "f.txt" }) File.WriteAllText(_tmp.Sub("c", name), name);
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(["c", "a.txt", "b.txt", "d.txt", "e.txt"], app.Browser.List.Items.Select(i => i.Name));
        Assert.Equal(0, app.Browser.List.FocusIndex); // pasta aberta pelo início: primeiro item

        // Abrir "c" (no topo por ser pasta) a partir de "d.txt": a lista nova começa no primeiro item.
        await d.FocusItem("d.txt");
        await d.FocusItem("c");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(0, app.Browser.List.FocusIndex);
        Assert.Equal("1.txt", app.Browser.List.Focused!.Name);

        // Voltar restaura a pasta aberta; subir (Esquerda) também foca a pasta de onde se veio.
        await d.FocusItem("e.txt");
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal("c", app.Browser.List.Focused!.Name);
        d.Press(InputAction.Confirm);
        await d.Idle();
        await d.FocusItem("f.txt");
        d.Press(InputAction.NavigateLeft);
        await d.Idle();
        Assert.Equal("c", app.Browser.List.Focused!.Name);

        // Atualizar o mesmo local mantém o item focado.
        await d.FocusItem("e.txt");
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Atualizar");
        await d.Idle();
        Assert.Equal("e.txt", app.Browser.List.Focused!.Name);
    });

    [Fact]
    public void Switching_views_inside_an_archive_and_in_search_results_keeps_focus_and_marks_without_reading_again() => UiContext.Run(async () =>
    {
        ZipFixtures.Create(_tmp.Sub("pacote.zip"), ZipFixtures.Text("um.txt", "1"), ZipFixtures.Text("dois.txt", "2"), ZipFixtures.Text("tres.txt", "3"));
        File.WriteAllText(_tmp.Sub("relatorio-a.txt"), "a");
        File.WriteAllText(_tmp.Sub("relatorio-b.txt"), "b");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();

        // Dentro do compactado: marca dois, foca o terceiro e troca lista → grade → lista.
        await d.FocusItem("pacote.zip");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<ArchiveLocation>(app.Browser.Location);
        Assert.Equal(0, app.Browser.List.FocusIndex);
        await d.FocusItem("dois.txt");
        d.Press(InputAction.ToggleSelection);
        await d.FocusItem("tres.txt");
        d.Press(InputAction.ToggleSelection);
        await d.FocusItem("um.txt");
        var items = app.Browser.List.Items;
        d.Press(InputAction.ChangeView);
        Assert.True(app.IsGrid);
        d.Press(InputAction.ChangeView);
        Assert.False(app.IsGrid);
        Assert.Same(items, app.Browser.List.Items);
        Assert.Equal("um.txt", app.Browser.List.Focused!.Name);
        Assert.Equal(["dois.txt", "tres.txt"], app.Browser.List.SelectedEntries.Select(e => e.Name).Order());
        d.Press(InputAction.Back); // limpa a marcação
        d.Press(InputAction.Back); // sai do compactado
        await d.Idle();

        // Resultados da busca: o mesmo resultado continua focado e a busca não roda de novo.
        d.Press(InputAction.Search);
        var kb = await d.WaitKeyboard();
        d.TypeOnKeyboard(kb, "relatorio");
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => app.TopModal is null, "teclado fechado");
        await UiContext.WaitUntil(() => app.Browser.ActiveSearch is { IsRunning: false } && app.Browser.List.Items.Count == 2, "busca concluída");
        await d.Idle();
        Assert.False(app.ListHeader.CanMark); // na busca não se marca: sem caixa no cabeçalho
        await d.FocusItem("relatorio-b.txt");
        var results = app.Browser.List.Items;
        d.Press(InputAction.ChangeView);
        d.Press(InputAction.ChangeView);
        Assert.Same(results, app.Browser.List.Items);
        Assert.Equal("relatorio-b.txt", app.Browser.List.Focused!.Name);
        Assert.IsType<SearchLocation>(app.Browser.Location);
    });
}
