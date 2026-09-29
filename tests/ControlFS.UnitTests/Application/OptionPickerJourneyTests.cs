using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Appearance;
using ControlFS.Core.Contracts;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Seletor de opções (#261): valores com várias alternativas mostram todas, com a atual marcada; Voltar não muda nada.</summary>
public class OptionPickerJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private async Task<(Driver D, MenuModal Settings)> BootSettings()
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.Idle();
        app.ShowSettings();
        return (d, await d.WaitMenu());
    }

    [Fact]
    public void Every_multi_value_setting_opens_a_picker_with_all_choices_the_current_marked_and_back_changes_nothing() => UiContext.Run(async () =>
    {
        var (d, settings) = await BootSettings();
        var app = d.App;
        (string Row, string[] Choices, int Current, string Pick, Func<bool> Applied)[] rows =
        [
            ("Exibição:", ["lista", "grade"], 0, "grade", () => app.Settings.View == ViewMode.Grid),
            ("Densidade da lista:", ["confortável", "compacta"], 0, "compacta", () => app.Settings.Density == ListDensity.Compact),
            ("Tema:", ["automático", "escuro", "claro"], 0, "claro", () => app.Settings.Theme == ThemeMode.Light),
            ("Cor de destaque:", ["ciano", "azul", "verde", "âmbar", "magenta", "laranja"], 0, "laranja", () => app.Settings.Accent == AccentColor.Orange),
            ("Ordenar por:", ["nome", "tipo", "tamanho", "data"], 0, "data", () => app.ListHeader.Sort!.Field == ControlFS.Core.Models.SortField.Modified),
            ("Confirmar com:", ["botão inferior", "botão direito"], 0, "botão direito", () => app.Settings.Convention == ConfirmBackConvention.EastConfirms),
            ("Legendas:", ["automáticas", "Xbox", "PlayStation", "Nintendo", "genéricas"], 0, "Nintendo", () => app.Settings.LabelStyle == ButtonLabelStyle.Nintendo),
            ("Fluidez:", ["máxima", "economia"], 0, "economia", () => !app.Settings.SyncInputToDisplay),
        ];
        foreach (var (row, choices, current, pick, applied) in rows)
        {
            var index = settings.Items.ToList().FindIndex(i => i.Label.StartsWith(row, StringComparison.Ordinal));
            Assert.True(index >= 0, row);
            d.FocusMenu(settings, index);
            var before = settings.Items[index].Label;
            d.Press(InputAction.Confirm);

            var picker = await d.WaitMenu();
            Assert.NotSame(settings, picker);
            Assert.True(picker.IsPicker, row);
            Assert.Equal(choices, picker.Items.Select(i => i.Label)); // todas as alternativas, nenhuma some
            Assert.Equal(current, picker.FocusIndex);                 // abre na atual
            Assert.Equal(current, picker.PickerCurrent);
            Assert.Equal(["atual"], picker.Items.Where(i => i.Value is not null).Select(i => i.Value)); // marcada em texto…
            Assert.Equal(ActionIcon.RadioOn, picker.Items[current].Icon);                                 // …e em ícone
            Assert.All(picker.Items.Where((_, i) => i != current), i => Assert.Equal(ActionIcon.RadioOff, i.Icon));
            Assert.Contains("selecionada", app.DescribeFocus().Item, StringComparison.Ordinal);

            d.Press(InputAction.Back); // cancelar: nada muda e Configurações volta com o foco no mesmo ajuste
            Assert.Same(settings, app.TopModal);
            Assert.Equal((index, before), (settings.FocusIndex, settings.Items[index].Label));
            Assert.False(applied(), row);

            d.Press(InputAction.Confirm);
            var again = await d.WaitMenu();
            d.FocusMenu(again, again.Items.ToList().FindIndex(i => i.Label == pick));
            d.Press(InputAction.Confirm);
            Assert.Same(settings, app.TopModal); // escolher aplica e volta
            Assert.True(applied(), row);
            Assert.Equal(index, settings.FocusIndex);
            Assert.EndsWith(pick, settings.Items[index].Label, StringComparison.Ordinal); // o texto da linha já mostra o valor novo
        }
    });

    [Fact]
    public void Simple_toggles_and_direct_actions_stay_one_press() => UiContext.Run(async () =>
    {
        var (d, settings) = await BootSettings();
        var app = d.App;
        foreach (var row in new[] { "Ordem:", "Itens ocultos:", "Recentes:", "Sugestões do teclado:", "Restaurar abas ao abrir:" })
        {
            var index = settings.Items.ToList().FindIndex(i => i.Label.StartsWith(row, StringComparison.Ordinal));
            var before = settings.Items[index].Label;
            d.FocusMenu(settings, index);
            d.Press(InputAction.Confirm);
            Assert.Same(settings, app.TopModal); // um toque, sem seletor
            Assert.NotEqual(before, settings.Items[index].Label);
        }
    });

    [Fact]
    public void Picker_works_with_the_mouse_and_confirm_convention_never_applies_on_open() => UiContext.Run(async () =>
    {
        var (d, settings) = await BootSettings();
        var app = d.App;
        var theme = settings.Items.ToList().FindIndex(i => i.Label.StartsWith("Tema:", StringComparison.Ordinal));
        app.PointerChooseModalOption(theme); // clique no ajuste: abre o seletor, não troca o valor
        var picker = await d.WaitMenu();
        Assert.True(picker.IsPicker);
        Assert.Equal(ThemeMode.System, app.Settings.Theme);
        app.PointerChooseModalOption(1); // clique numa alternativa: "escuro"
        Assert.Same(settings, app.TopModal);
        Assert.Equal(ThemeMode.Dark, app.Settings.Theme);
        Assert.Equal(new Hint(InputAction.Confirm, "Escolher"), app.Hints[0]);
    });

    [Fact]
    public void Picker_prompts_say_cancel_and_the_status_names_the_new_value() => UiContext.Run(async () =>
    {
        var (d, settings) = await BootSettings();
        var app = d.App;
        d.FocusMenu(settings, settings.Items.ToList().FindIndex(i => i.Label.StartsWith("Tema:", StringComparison.Ordinal)));
        d.Press(InputAction.Confirm);
        Assert.Contains(app.Hints, h => h is { Action: InputAction.Back, Label: "Cancelar" });
        d.Press(InputAction.NavigateDown);
        d.Press(InputAction.Confirm);
        Assert.Equal("Tema escuro.", app.StatusMessage);
    });
}
