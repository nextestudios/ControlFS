using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>
/// Tutorial guiado (#231): cada passo só avança quando o usuário faz de verdade o que ele pede (ações semânticas pelo
/// Driver), nunca com outra ação; pode voltar um passo ou ser pulado; e nenhum arquivo é alterado.
/// </summary>
public class TutorialJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    private (AppController App, Driver Driver) Launch(bool offerOnboarding)
    {
        _tmp.MakeDir("Documentos");
        _tmp.MakeDir("Fotos");
        File.WriteAllText(_tmp.Sub("notas.txt"), "texto");
        var fs = new TestFileSystem(_tmp.Path);
        fs.Drives.Add(new FileEntry("drive:E:\\", "PENDRIVE (E:)", EntryKind.Drive, FullPath: _tmp.MakeDir("E"), Drive: DriveKind.Removable));
        var app = new AppController(fs, new ArchiveService(), new JsonSettingsStore(_data.Path)) { OfferOnboarding = offerOnboarding };
        app.Start();
        return (app, new Driver(app));
    }

    [Fact]
    public void Tutorial_from_the_onboarding_advances_only_on_the_requested_action_and_changes_no_files() => UiContext.Run(async () =>
    {
        var (app, d) = Launch(offerOnboarding: true);
        var before = _tmp.Snapshot();
        var onboarding = Assert.IsType<OnboardingModal>(app.TopModal);
        while (!onboarding.IsLastStep) d.Press(InputAction.NextRegion);
        Assert.Equal("Começar tutorial", onboarding.FocusedOption!.Label);
        d.Press(InputAction.Confirm);
        Assert.Null(app.TopModal);
        var tutorial = Assert.IsType<GuidedTutorial>(app.Tutorial);
        Assert.Equal(TutorialStepKind.MoveFocus, tutorial.Step);
        Assert.StartsWith("Tutorial, passo 1 de 8", app.TakeAnnouncement(), StringComparison.Ordinal); // Narrador

        // 1. Mover o foco.
        d.Press(InputAction.NavigateDown);
        Assert.Equal(TutorialStepKind.OpenFolder, tutorial.Step);

        // 2. Abrir uma pasta. Marcar abre as opções do tutorial (não marca nada); Continuar não conta como abrir.
        d.Press(InputAction.ToggleSelection);
        var options = await d.WaitDialog("Tutorial guiado");
        d.ChooseOption(options, "Continuar");
        Assert.Equal(TutorialStepKind.OpenFolder, tutorial.Step);
        d.Press(InputAction.NavigateUp);
        d.Press(InputAction.Confirm); // "Pasta de teste"
        await d.Idle();
        Assert.Equal(TutorialStepKind.GoBack, tutorial.Step);

        // 3. Voltar: entrar em outra pasta não conta; Voltar sim.
        await d.FocusItem("Documentos");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(TutorialStepKind.GoBack, tutorial.Step);
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal(TutorialStepKind.OpenActions, tutorial.Step);

        // 4. Abrir Ações e fechar.
        d.Press(InputAction.OpenContextMenu);
        await d.WaitMenu();
        Assert.Equal((TutorialStepKind.OpenActions, 1), (tutorial.Step, tutorial.Phase));
        d.Press(InputAction.Back);
        Assert.Equal(TutorialStepKind.TopBar, tutorial.Step);

        // 5. Barra superior (R1) e um atalho (Favoritos mostra os favoritos).
        d.Press(InputAction.NextRegion);
        Assert.Equal((TutorialStepKind.TopBar, 1), (tutorial.Step, tutorial.Phase));
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(TutorialStepKind.ChangeView, tutorial.Step);
        while (app.TopModal is not null) d.Press(InputAction.Back);
        d.Press(InputAction.Back); // de volta ao conteúdo
        Assert.Equal(TutorialStepKind.ChangeView, tutorial.Step);

        // 6. Lista/grade.
        var view = app.Settings.View;
        d.Press(InputAction.ChangeView);
        Assert.NotEqual(view, app.Settings.View);
        Assert.Equal(TutorialStepKind.Search, tutorial.Step);

        // 7. Buscar: abrir o teclado da busca e cancelar.
        d.Press(InputAction.Search);
        await d.WaitKeyboard();
        Assert.Equal((TutorialStepKind.Search, 1), (tutorial.Step, tutorial.Phase));
        d.Press(InputAction.Back);
        Assert.Equal(TutorialStepKind.AppMenu, tutorial.Step);

        // 8. Menu e Configurações; no fim, o resumo.
        d.Press(InputAction.OpenAppMenu);
        Assert.Equal((TutorialStepKind.AppMenu, 1), (tutorial.Step, tutorial.Phase));
        await d.ChooseMenu("Configurações");
        Assert.Null(app.Tutorial);
        var done = await d.WaitDialog("Tutorial concluído");
        Assert.Equal(ActionIcon.Success, done.Icon);

        Assert.Equal(before, _tmp.Snapshot()); // nenhum arquivo criado, renomeado ou apagado
    });

    [Fact]
    public void Tutorial_from_the_help_menu_goes_back_a_step_and_skips_without_marking_items() => UiContext.Run(async () =>
    {
        var (app, d) = Launch(offerOnboarding: false);
        d.Press(InputAction.Confirm); // "Pasta de teste"
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Ajuda e tutorial");
        await d.ChooseMenu("Tutorial guiado");
        var tutorial = Assert.IsType<GuidedTutorial>(app.Tutorial);
        Assert.Contains(app.Hints, h => h is { Action: InputAction.ToggleSelection, Label: "Tutorial" });

        d.Press(InputAction.NavigateDown);
        Assert.Equal(TutorialStepKind.OpenFolder, tutorial.Step);
        d.Press(InputAction.ToggleSelection);
        Assert.Equal(0, app.ActivePane.List.SelectionCount); // Marcar não marca durante o tutorial
        d.ChooseOption(await d.WaitDialog("Tutorial guiado"), "Voltar passo");
        Assert.Equal(TutorialStepKind.MoveFocus, tutorial.Step);

        d.Press(InputAction.ToggleSelection);
        d.ChooseOption(await d.WaitDialog("Tutorial guiado"), "Pular tutorial");
        Assert.Null(app.Tutorial);
        Assert.Null(app.TopModal);
        Assert.Contains("Tutorial guiado", app.StatusMessage, StringComparison.Ordinal);
        d.Press(InputAction.ToggleSelection); // sem tutorial, Marcar volta a marcar
        Assert.Equal(1, app.ActivePane.List.SelectionCount);
    });

    [Fact]
    public void State_machine_counts_only_the_requested_move_redirects_to_opening_a_folder_on_home_and_restarts_a_step_left_halfway()
    {
        static TutorialObservation At(string? folder, InputAction? last = null, PaneRegion region = PaneRegion.List, TutorialModal modal = TutorialModal.None) =>
            new(last, folder is null, folder is null ? null : new PhysicalLocation(folder), false, "item", region, modal, ViewMode.List);
        var tutorial = new GuidedTutorial();
        tutorial.Start(At(null));
        Assert.True(tutorial.Observe(At(null, InputAction.NavigateDown) with { FocusId = "outro" }));
        Assert.Equal(TutorialStepKind.OpenFolder, tutorial.Step);
        Assert.False(tutorial.Observe(At(@"C:\A", InputAction.Back))); // mudou de local sem Abrir: não conta
        Assert.False(tutorial.Observe(At(@"C:\A", InputAction.Confirm))); // Abrir sem mudar de local (um arquivo): não conta
        Assert.True(tutorial.Observe(At(@"C:\A\B", InputAction.Confirm)));
        Assert.Equal(TutorialStepKind.GoBack, tutorial.Step);
        Assert.True(tutorial.Observe(At(null, InputAction.Back)));
        Assert.Equal(TutorialStepKind.OpenActions, tutorial.Step);

        // Voltar passo no início: "Volte" precisa de uma pasta aberta, então pede para abrir uma.
        tutorial.Previous(At(null));
        Assert.Equal(TutorialStepKind.OpenFolder, tutorial.Step);
        tutorial.Observe(At(@"C:\A", InputAction.Confirm));
        tutorial.Observe(At(null, InputAction.Back));
        tutorial.Observe(At(null, InputAction.OpenContextMenu, modal: TutorialModal.Actions));
        tutorial.Observe(At(null, InputAction.Back));
        Assert.Equal(TutorialStepKind.TopBar, tutorial.Step);

        // Barra superior: entrou (parte 2) e saiu sem abrir nada → pede L1/R1 de novo.
        tutorial.Observe(At(null, InputAction.NextRegion, PaneRegion.QuickAccess));
        Assert.Equal(1, tutorial.Phase);
        tutorial.Observe(At(null, InputAction.Back));
        Assert.Equal((TutorialStepKind.TopBar, 0), (tutorial.Step, tutorial.Phase));
    }
}
