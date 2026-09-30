using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Sistema único dos modais (#172): ícones, ações perigosas, escopo de entrada e foco ao fechar.</summary>
public class ModalSystemJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>Operações de arquivo disponíveis (com Lixeira); nada é executado nestes testes.</summary>
    private sealed class NoOpFileOperations : IFileOperationService
    {
        public bool CanRecycle(string path) => true;
        public FileEntry Rename(string path, string newName) => throw new InvalidOperationException("não usado");
        public Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("não usado");
    }

    private Driver Boot()
    {
        Directory.CreateDirectory(_tmp.Sub("Pasta"));
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        File.WriteAllText(_tmp.Sub("b.txt"), "b");
        ZipFixtures.Create(_tmp.Sub("c.zip"), ZipFixtures.Text("dentro.txt", "x"));
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: new NoOpFileOperations());
        app.Start();
        return new Driver(app);
    }

    private static void AssertIcons(Modal? modal, string context)
    {
        switch (modal)
        {
            case MenuModal menu:
                Assert.NotEqual(ActionIcon.None, menu.Icon);
                Assert.All(menu.Items, i => Assert.True(i.Icon != ActionIcon.None, $"{context}: \"{i.Label}\" sem ícone"));
                break;
            case DialogModal dialog:
                Assert.All(dialog.Options, o => Assert.True(o.Icon != ActionIcon.None, $"{context}: \"{o.Label}\" sem ícone"));
                break;
            default:
                Assert.Fail($"{context}: esperava um menu ou diálogo, veio {modal?.GetType().Name ?? "nada"}");
                break;
        }
    }

    [Fact]
    public void Every_menu_option_and_dialog_button_has_an_icon_with_a_glyph() => UiContext.Run(async () =>
    {
        foreach (var icon in Enum.GetValues<ActionIcon>().Where(i => i != ActionIcon.None))
            Assert.False(string.IsNullOrEmpty(ActionIcons.Glyph(icon)), $"{icon} sem símbolo");

        var d = Boot();
        d.Press(InputAction.OpenAppMenu);
        AssertIcons(d.App.TopModal, "Menu no início");
        d.Press(InputAction.Back);
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(d.App.TopModal, "Ações de um local");
        d.Press(InputAction.Back);

        d.Press(InputAction.Confirm);
        await d.FocusItem("Pasta");
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(await d.WaitMenu(), "Ações de pasta");
        d.Press(InputAction.Back);
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(await d.WaitMenu(), "Ações de arquivo");
        await d.ChooseMenu("Excluir…");
        AssertIcons(await d.WaitDialog("Mover 1 item para a Lixeira?"), "Confirmação de exclusão");
        d.Press(InputAction.Back);
        await d.FocusItem("c.zip");
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(await d.WaitMenu(), "Ações de compactado");
        await d.ChooseMenu("Extrair aqui");
        AssertIcons(await d.WaitDialog("Extrair"), "Resumo da extração");
        d.Press(InputAction.Back);
        await d.FocusItem("b.txt");
        d.Press(InputAction.ToggleSelection);
        d.Press(InputAction.OpenContextMenu);
        AssertIcons(await d.WaitMenu(), "Itens marcados");
        await d.ChooseMenu("Compactar");
        AssertIcons(await d.WaitDialog("Compactar"), "Compactar");
        d.Press(InputAction.Back);
        d.Press(InputAction.OpenAppMenu);
        AssertIcons(d.App.TopModal, "Menu na pasta");
        d.Press(InputAction.Back);
        d.Press(InputAction.OpenAppMenu); // uma aba só: a faixa não aparece; Menu → Abas
        await d.ChooseMenu("Abas");
        AssertIcons(await d.WaitMenu(), "Abas");
    });

    [Fact]
    public void Destructive_actions_are_flagged_and_never_the_initial_focus() => UiContext.Run(async () =>
    {
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        var menu = await d.WaitMenu();
        Assert.True(menu.Items.Single(i => i.Label == "Excluir…").IsDestructive);
        Assert.All(menu.Items.Where(i => i.Label != "Excluir…"), i => Assert.False(i.IsDestructive, i.Label));
        Assert.False(menu.Items[menu.FocusIndex].IsDestructive);
        d.Press(InputAction.Back);

        // Quem monta um menu ou diálogo não consegue abrir com o foco numa ação perigosa.
        var app = d.App;
        app.PushModal(new MenuModal("Teste", [new MenuItem("Excluir…", () => { }, Icon: ActionIcon.Delete), new MenuItem("Copiar", () => { }, Icon: ActionIcon.Copy)]));
        Assert.Equal("Copiar", ((MenuModal)app.TopModal!).Items[((MenuModal)app.TopModal!).FocusIndex].Label);
        d.Press(InputAction.Back);
        // Na grade de ações rápidas também (#193): o bloco perigoso vai para o fim e nunca é o foco inicial.
        var quick = new MenuModal("Teste", [new MenuItem("Excluir…", () => { }, Icon: ActionIcon.Delete, Placement: MenuPlacement.Quick),
            new MenuItem("Copiar", () => { }, Icon: ActionIcon.Copy, Placement: MenuPlacement.Quick)]);
        app.PushModal(quick);
        Assert.Equal(["Copiar", "Excluir…"], quick.Items.Select(i => i.Label));
        Assert.Equal("Copiar", quick.Items[quick.FocusIndex].Label);
        d.Press(InputAction.Back);
        var dialog = new DialogModal("Teste?", [], sensitive: true);
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => app.CloseModal(dialog));
        dialog.Options.Add(new DialogOption("Excluir permanentemente", DialogOptionKind.Danger, () => { }));
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        app.PushModal(dialog);
        Assert.Equal("Cancelar", dialog.Options[dialog.FocusIndex].Label);
        Assert.Equal(ActionIcon.Warning, dialog.Icon);
        app.TakeAnnouncement();
        d.Press(InputAction.NavigateUp);
        Assert.Contains("ação perigosa", app.TakeAnnouncement(), StringComparison.Ordinal);
    });

    [Fact]
    public void Input_under_an_active_modal_never_reaches_the_screen_below() => UiContext.Run(async () =>
    {
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.txt");
        var app = d.App;
        var location = app.Browser.Location;
        var grid = app.IsGrid;
        d.Press(InputAction.OpenAppMenu);
        var menu = await d.WaitMenu();

        foreach (var action in new[] { InputAction.NavigateDown, InputAction.PageDown, InputAction.ToggleSelection, InputAction.ChangeView, InputAction.Search, InputAction.PreviousRegion, InputAction.NextRegion })
            d.Press(action);
        app.PointerActivateListItem(0); // clique numa linha atrás do modal
        app.PointerActivateTab(0);

        Assert.Same(menu, app.TopModal);
        Assert.Equal("a.txt", app.Browser.List.Focused?.Name);
        Assert.Equal(0, app.Browser.List.SelectionCount);
        Assert.Equal(PaneRegion.List, app.FocusRegion);
        Assert.Same(location, app.Browser.Location);
        Assert.Equal(grid, app.IsGrid);
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
    });

    [Fact]
    public void Closing_nested_modals_restores_focus_step_by_step() => UiContext.Run(async () =>
    {
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("b.txt");
        var app = d.App;
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Compactar…");
        var compress = await d.WaitDialog("Compactar");
        d.ChooseOption(compress, "Nome:"); // teclado por cima do diálogo
        await d.WaitKeyboard();
        var nameOption = compress.FocusIndex;

        d.Press(InputAction.Back); // fecha só o teclado
        Assert.Same(compress, app.TopModal);
        Assert.Equal(nameOption, compress.FocusIndex);
        d.Press(InputAction.Back); // Cancelar
        Assert.Null(app.TopModal);
        Assert.Equal("b.txt", app.Browser.List.Focused?.Name);
        Assert.Equal(PaneRegion.List, app.FocusRegion);
    });

    [Fact]
    public void Quick_action_grid_moves_in_two_dimensions_and_activates_a_tile() => UiContext.Run(async () =>
    {
        var d = Boot();
        var app = d.App;
        d.Press(InputAction.Confirm);
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        var menu = await d.WaitMenu();
        // Arquivo comum: 7 blocos em 2 linhas de 4; Excluir no fim da grade. O foco abre no primeiro bloco (antes abria
        // na lista, abaixo da grade, e as ações rápidas ficavam fora do caminho).
        Assert.Equal(["Abrir", "Recortar", "Copiar", "Renomear", "Compactar", "Propriedades", "Excluir"], menu.Items.Take(menu.QuickCount).Select(i => i.TileLabel));
        Assert.Equal((4, 2), (menu.QuickColumns, menu.QuickRows));
        string Focused() => menu.Items[menu.FocusIndex].Label;
        Assert.False(menu.Items[0].IsEnabled); // Abrir: sem o shell do Windows neste teste
        Assert.Equal("Recortar", Focused()); // o primeiro bloco que funciona
        // As ações sobre a pasta aberta têm um grupo com o nome dela, separado das ações do arquivo.
        Assert.Equal($"Nesta pasta ({Path.GetFileName(_tmp.Path)})", menu.TitledSection);
        Assert.Equal(menu.TitledSection, menu.Items.Single(i => i.Label == "Nova pasta aqui").Section);

        d.Press(InputAction.NavigateLeft);
        d.Press(InputAction.NavigateDown); // primeira linha da grade para a segunda (coluna 1)
        Assert.Equal("Compactar…", Focused());
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight); // borda: fica
        Assert.Equal("Excluir…", Focused());
        Assert.Same(menu, app.TopModal); // Direita num bloco nunca escolhe
        d.Press(InputAction.NavigateLeft);
        app.TakeAnnouncement();
        d.Press(InputAction.NavigateRight);
        Assert.Contains("Excluir…, ação perigosa, ação rápida 7 de 7", app.TakeAnnouncement(), StringComparison.Ordinal);
        d.Press(InputAction.NavigateDown); // última linha: entra na lista
        Assert.Equal("Visualizar como texto", Focused());
        d.Press(InputAction.NavigateUp); // volta à coluna de onde saiu
        Assert.Equal("Excluir…", Focused());
        d.Press(InputAction.NavigateUp);
        d.Press(InputAction.NavigateUp); // primeira linha: dá a volta para o último item da lista
        Assert.Equal(menu.Items.Count - 1, menu.FocusIndex);
        d.Press(InputAction.PageUp);
        d.Press(InputAction.NavigateRight); // Recortar
        d.Press(InputAction.Confirm);
        Assert.Null(app.TopModal);
        Assert.Equal(FileOperationKind.Move, app.Clipboard?.Kind);
    });

    [Fact]
    public void Settings_live_in_Configuracoes_and_a_toggle_keeps_it_open() => UiContext.Run(async () =>
    {
        var d = Boot();
        var app = d.App;
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        var menu = await d.WaitMenu();
        Assert.DoesNotContain(menu.Items, i => i.Label.StartsWith("Densidade", StringComparison.Ordinal));
        d.FocusMenu(menu, menu.Items.ToList().FindIndex(i => i.Label == "Configurações…"));
        d.Press(InputAction.Confirm);
        var settings = await d.WaitMenu();
        Assert.Equal("Configurações", settings.Title);
        // Todo ajuste que saiu do Menu continua alcançável aqui.
        foreach (var label in new[] { "Exibição:", "Densidade da lista:", "Painel de detalhes:", "Ordenar por:", "Ordem:", "Itens ocultos:", "Busca em subpastas:",
                     "Recentes:", "Sugestões do teclado:", "Confirmar com:", "Legendas:", "Fluidez:", "Controle ativo:", "Teste de controles…", "Controles sem perfil…", "Atualizações" })
            Assert.True(settings.Items.Any(i => i.Label.StartsWith(label, StringComparison.Ordinal)), label);

        // Um ajuste com várias alternativas abre o seletor (#261) e, escolhida uma, volta a Configurações no mesmo ajuste.
        var density = settings.Items.ToList().FindIndex(i => i.Label == "Densidade da lista: confortável");
        d.FocusMenu(settings, density);
        d.Press(InputAction.Confirm);
        var picker = await d.WaitMenu();
        Assert.True(picker.IsPicker);
        Assert.Equal("confortável", picker.Items[picker.FocusIndex].Label); // abre na alternativa atual
        d.FocusMenu(picker, picker.Items.ToList().FindIndex(i => i.Label == "compacta"));
        d.Press(InputAction.Confirm);
        Assert.Same(settings, app.TopModal);
        Assert.Equal(ListDensity.Compact, app.Settings.Density);
        Assert.Equal("Densidade da lista: compacta", settings.Items[settings.FocusIndex].Label);
        d.Press(InputAction.Back);
        Assert.Null(app.TopModal);
    });

    [Fact]
    public void Settings_is_one_column_of_icon_only_grids_per_section_navigated_in_two_dimensions() => UiContext.Run(async () =>
    {
        // #293: todos os ajustes são blocos só de ícone na grade do seu grupo, numa coluna larga; o foco revela nome e valor.
        var d = Boot();
        var app = d.App;
        d.Press(InputAction.Confirm);
        await d.Idle();
        app.ShowSettings();
        var settings = await d.WaitMenu();
        Assert.True(settings.IconOnly);
        Assert.Equal(ModalSize.Wide, settings.Size); // uma coluna larga (sem colunas paralelas)
        Assert.Equal(4, settings.Grids.Count);
        Assert.Equal(settings.Items.Count, settings.Grids.Sum(g => g.Count)); // nenhum ajuste fora da grade (nada de linhas de texto)
        var view = settings.Grids[0];
        Assert.Equal(9, view.Count);
        Assert.Equal((5, 2), (view.Columns, view.Rows));
        Assert.Equal(4, settings.Grids[3].Columns); // grupo curto (2 ajustes): o bloco não estica
        string Focused() => settings.Items[settings.FocusIndex].Label;
        Assert.Equal(0, settings.FocusIndex);

        d.Press(InputAction.NavigateRight);
        d.Press(InputAction.NavigateRight); // Detalhes (coluna 3)
        d.Press(InputAction.NavigateDown);
        Assert.StartsWith("Itens ocultos:", Focused(), StringComparison.Ordinal); // linha de baixo, mesma coluna
        d.Press(InputAction.NavigateDown); // última linha da grade: a grade do grupo seguinte, mesma coluna
        Assert.StartsWith("Restaurar abas:", Focused(), StringComparison.Ordinal);
        d.Press(InputAction.NavigateUp); // volta à coluna de onde saiu
        Assert.StartsWith("Itens ocultos:", Focused(), StringComparison.Ordinal);
        d.Press(InputAction.NavigateDown);
        app.TakeAnnouncement();
        d.Press(InputAction.NavigateRight); // Sugestões: rótulo por extenso (com o valor) e a posição no bloco
        Assert.Matches(@"^Sugestões do teclado: (ligado|desligado), .*bloco 4 de 5$", app.TakeAnnouncement());
        d.Press(InputAction.NavigateUp); // da primeira linha da grade: a última linha da grade de cima, na coluna de onde saiu
        Assert.StartsWith("Tela cheia:", Focused(), StringComparison.Ordinal);
        d.Press(InputAction.NavigateLeft);
        var hidden = settings.FocusIndex;
        Assert.StartsWith("Itens ocultos:", Focused(), StringComparison.Ordinal);
        Assert.Equal("escondidos", settings.Items[hidden].Value);
        // Focado, o bloco só de ícone revela o nome, o valor e o que faz, na leitura fixa do painel (nada de texto sempre visível).
        Assert.StartsWith("Itens ocultos: escondidos\nArquivos e pastas marcados como ocultos", settings.TileCaption, StringComparison.Ordinal);
        Assert.Equal(settings.TileCaption, settings.Description);

        d.Press(InputAction.Confirm); // alterna e continua no mesmo bloco, com o valor novo
        Assert.Same(settings, app.TopModal);
        Assert.True(app.Settings.ShowHidden);
        Assert.Equal(hidden, settings.FocusIndex);
        Assert.Equal(("Itens ocultos: visíveis", "visíveis"), (Focused(), settings.Items[hidden].Value));

        app.PointerChooseModalOption(1); // toque num bloco com alternativas: abre o seletor, como Confirmar nele
        Assert.True(app.TopModal is MenuModal { IsPicker: true });
        app.PointerChooseModalOption(1); // toque numa alternativa do seletor: "compacta"
        Assert.Same(settings, app.TopModal);
        Assert.Equal(ListDensity.Compact, app.Settings.Density);
        Assert.Equal(("Densidade da lista: compacta", "compacta"), (settings.Items[1].Label, settings.Items[1].Value));
    });

    [Fact]
    public void Settings_is_the_first_tile_of_the_menu_and_bumpers_jump_between_its_groups() => UiContext.Run(async () =>
    {
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        var menu = await d.WaitMenu();
        Assert.StartsWith("Configurações", menu.Items[0].Label, StringComparison.Ordinal); // #288: primeiro bloco do Menu
        Assert.Equal(0, menu.FocusIndex); // e já em foco: Menu + Confirmar abre
        d.Press(InputAction.Confirm);
        var settings = await d.WaitMenu();

        // R1/L1 pulam de grupo em grupo (e dão a volta).
        var starts = settings.SectionStarts;
        Assert.True(starts.Count >= 3);
        Assert.Equal(0, settings.FocusIndex);
        d.Press(InputAction.NextRegion);
        Assert.Equal(starts[1], settings.FocusIndex);
        d.Press(InputAction.NextRegion);
        Assert.Equal(starts[2], settings.FocusIndex);
        d.Press(InputAction.PreviousRegion);
        Assert.Equal(starts[1], settings.FocusIndex);
        d.Press(InputAction.PreviousRegion);
        d.Press(InputAction.PreviousRegion);
        Assert.Equal(starts[^1], settings.FocusIndex); // volta ao último grupo
    });

    [Fact]
    public void Menu_size_class_and_reserved_descriptions_do_not_depend_on_the_focused_option() => UiContext.Run(async () =>
    {
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Configurações");
        var settings = (MenuModal)d.App.TopModal!;
        Assert.Equal(ModalSize.Wide, settings.Size); // #293: uma coluna larga
        var reserved = settings.AllDescriptions.ToList();
        for (var i = 0; i < settings.Items.Count; i++)
        {
            settings.FocusIndex = i;
            Assert.Equal(ModalSize.Wide, settings.Size);
            // Toda descrição que a tela pode mostrar tem o lugar reservado: focar a mais longa não redimensiona o painel (#227).
            if (settings.Description.Length > 0) Assert.Contains(settings.Description, reserved);
        }
        Assert.Contains(reserved, t => t.Length > 100);
        d.Press(InputAction.Back);
        d.Press(InputAction.OpenContextMenu); // ações de um local, só lista ou com grade: a classe também é fixa
        Assert.True(d.App.TopModal is MenuModal { Size: ModalSize.Compact or ModalSize.Medium });
    });

    [Fact]
    public void Menus_never_open_focused_on_an_unavailable_option() => UiContext.Run(async () =>
    {
        var d = Boot();
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu); // nada copiado: "Colar" está indisponível (Configurações é o primeiro bloco, #288)
        var menu = Assert.IsType<MenuModal>(d.App.TopModal);
        Assert.False(menu.Items.Single(i => i.Label.StartsWith("Colar", StringComparison.Ordinal)).IsEnabled);
        Assert.True(menu.Items[menu.FocusIndex].IsEnabled);
        Assert.False(menu.Items[menu.FocusIndex].IsDestructive);
    });
}
