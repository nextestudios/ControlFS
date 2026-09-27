using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.Settings;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Application;

/// <summary>Histórico de operações (#20): sobrevive a um novo lançamento, é limitado e nunca guarda senhas.</summary>
public class HistoryJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();
    private readonly TempDir _data = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _data.Dispose();
    }

    private Driver Boot(JsonOperationHistoryStore store)
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), history: store);
        app.Start();
        return new Driver(app);
    }

    [Fact]
    public void Password_extraction_is_listed_after_relaunch_and_the_history_file_never_holds_the_password() => UiContext.Run(async () =>
    {
        File.Copy(FixturePath("zip/zipcrypto-senha-certa.zip"), _tmp.Sub("cofre.zip"));
        var store = new JsonOperationHistoryStore(_data.Path);
        var d = Boot(store);
        d.Press(InputAction.Confirm);
        await d.FocusItem("cofre.zip");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Extrair para \"cofre\"");
        d.ChooseOption(await d.WaitDialog("Extrair"), "Extrair");
        var kb = await d.WaitKeyboard();
        Assert.Equal(TextFieldKind.Password, kb.Keyboard.Kind);
        d.TypeOnKeyboard(kb, "errada");
        Assert.Equal("••••••", kb.Keyboard.DisplayText);
        d.PressKey(kb, KeyKind.Done);
        var retry = await d.WaitKeyboard();
        Assert.NotSame(kb, retry);
        Assert.Equal("Senha incorreta. Tente novamente.", retry.Keyboard.ErrorMessage);
        Assert.Equal(0, kb.Keyboard.Length); // senha anterior zerada
        d.TypeOnKeyboard(retry, "certa");
        d.PressKey(retry, KeyKind.Done);
        await d.WaitDialog("Extração concluída");
        await d.Idle();
        Assert.Equal("conteúdo protegido\n", File.ReadAllText(_tmp.Sub("cofre", "segredo.txt")));
        Assert.False(Directory.Exists(_tmp.Sub("cofre (2)")), "a tentativa com senha errada não deixou pasta para trás");
        var extracted = d.App.Operations.Items[^1].Result!.Count(ItemOutcome.Succeeded);

        var saved = File.ReadAllText(store.FilePath);
        Assert.DoesNotContain("errada", saved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certa", saved, StringComparison.OrdinalIgnoreCase);

        // Novo lançamento com a mesma pasta de dados: as duas tentativas aparecem com o desfecho real.
        var relaunched = Boot(new JsonOperationHistoryStore(_data.Path));
        var entries = relaunched.App.History.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(OperationErrorKind.WrongPassword, entries[0].Error);
        Assert.Equal(OperationState.Completed, entries[1].FinalState);
        Assert.Equal(extracted, entries[1].Count(ItemOutcome.Succeeded));
        Assert.Equal(_tmp.Sub("cofre.zip"), entries[1].Source);

        relaunched.Press(InputAction.OpenAppMenu);
        await relaunched.ChooseMenu("Operações");
        await relaunched.ChooseMenu("Extrair cofre.zip — concluída");
        var details = await relaunched.WaitDialog("Extrair cofre.zip");
        Assert.Contains(details.Lines, l => l == ("Concluídos", extracted.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Contains(details.Lines, l => l.Label == "Destino" && l.Value.EndsWith("cofre", StringComparison.Ordinal));
    });

    [Fact]
    public void History_keeps_only_the_newest_entries_and_survives_a_corrupt_file() => UiContext.Run(async () =>
    {
        var store = new JsonOperationHistoryStore(_data.Path);
        var start = DateTimeOffset.Now.AddDays(-1);
        store.Save([.. Enumerable.Range(0, 250).Select(i => Entry(i, start.AddMinutes(i)))]);
        var d = Boot(store);
        Assert.Equal(200, d.App.History.Entries.Count);
        Assert.Equal("op 50", d.App.History.Entries[0].Title); // as 50 mais antigas saíram

        File.WriteAllText(store.FilePath, "{ não é json");
        var recovered = Boot(new JsonOperationHistoryStore(_data.Path));
        await recovered.Idle();
        Assert.Empty(recovered.App.History.Entries);
        Assert.Contains("corrompido", recovered.App.StatusMessage, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(_data.Path, "history.json.corrupt-*")); // o original é preservado, não apagado
    });

    private static OperationHistoryEntry Entry(int i, DateTimeOffset at) =>
        OperationHistoryEntry.From(OperationKind.Copy, $"op {i}", at, at, new OperationResult(OperationState.Completed, []), "C:\\a", "C:\\b");
}
