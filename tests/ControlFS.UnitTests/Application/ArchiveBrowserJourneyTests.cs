using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Application;

/// <summary>Navegador de compactados (#68): cabeçalho, indicadores, rodapé por contexto e extração da seleção.</summary>
public class ArchiveBrowserJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static string Label(AppController app, InputAction action) => app.Hints.First(h => h.Action == action).Label;

    [Fact]
    public void Header_indicators_and_prompts_lead_to_a_correct_selection_extract() => UiContext.Run(async () =>
    {
        Create(_tmp.Sub("pacote.zip"), Text("leia.txt", new string('l', 4000)), Text("docs/b.txt", "beta"), Text("docs/c.txt", "gama"),
            Symlink("atalho", "/etc/passwd"), Text("../fora.txt", "x"));
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("pacote.zip");
        d.Press(InputAction.Confirm); // Explorar
        await d.Idle();

        Assert.StartsWith("ZIP · 4 arquivos · ", app.ArchiveSummary, StringComparison.Ordinal);
        Assert.EndsWith(" · 2 bloqueadas", app.ArchiveSummary, StringComparison.Ordinal); // "../fora.txt" e o link
        var list = app.ActivePane.List.Items;
        Assert.Contains(list, e => e.Name == "atalho" && e.IsBlocked);
        Assert.Contains(list, e => e.Name.Contains("fora.txt", StringComparison.Ordinal) && e.IsBlocked);
        var leia = Assert.Single(list, e => e.Name == "leia.txt");
        Assert.Matches(@"^compactado: .+ \(\d+%\)$", leia.Detail);

        await d.FocusItem("docs");
        Assert.Equal("Explorar", Label(app, InputAction.Confirm));
        d.Press(InputAction.Confirm);
        await d.FocusItem("c.txt");
        d.Press(InputAction.ToggleSelection);
        Assert.Equal("Extrair seleção (1)", Label(app, InputAction.OpenContextMenu));

        d.Press(InputAction.OpenContextMenu);
        var menu = await d.WaitMenu();
        Assert.StartsWith("Extrair seleção (1) para \"pacote\"", menu.Items[menu.FocusIndex].Label, StringComparison.Ordinal);
        d.Press(InputAction.Confirm);
        var summary = await d.WaitDialog("Extrair");
        Assert.Contains(summary.Lines, l => l.Label == "Entradas" && l.Value.StartsWith("1 selecionada", StringComparison.Ordinal));
        d.ChooseOption(summary, "Extrair");
        await d.WaitDialog("Extração concluída");

        Assert.Equal(["c.txt"], Directory.EnumerateFileSystemEntries(_tmp.Sub("pacote"), "*", SearchOption.AllDirectories).Select(Path.GetFileName));
        Assert.Equal("gama", File.ReadAllText(_tmp.Sub("pacote", "c.txt")));
    });

    [Fact]
    public void Header_encrypted_7z_asks_for_the_password_before_listing_and_again_after_a_wrong_one() => UiContext.Run(async () =>
    {
        // 7z não tem verificador de senha: antes, a senha errada virava "arquivo corrompido" e o usuário não podia tentar de novo.
        File.Copy(FixturePath("7z/cabecalho-protegido.7z"), _tmp.Sub("cofre.7z"));
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem("cofre.7z");
        d.Press(InputAction.Confirm);

        var kb = await d.WaitKeyboard();
        Assert.Equal(TextFieldKind.Password, kb.Keyboard.Kind);
        Assert.Null(app.ActivePane.Archive);
        d.TypeOnKeyboard(kb, "errada");
        d.PressKey(kb, KeyKind.Done);
        var retry = await d.WaitKeyboard();
        Assert.NotSame(kb, retry);
        Assert.Equal("Senha incorreta. Tente novamente.", retry.Keyboard.ErrorMessage);
        d.TypeOnKeyboard(retry, "certa");
        d.PressKey(retry, KeyKind.Done);
        await d.Idle();

        Assert.NotNull(app.ActivePane.Archive);
        Assert.Equal(["docs", "segredo.txt"], app.ActivePane.List.Items.Select(i => i.Name).Order(StringComparer.Ordinal));
    });

    [Fact]
    public async Task Summary_counts_password_protected_entries()
    {
        var info = await new ArchiveService().InspectAsync(FixturePath("zip/zipcrypto-senha-certa.zip"), null, ExtractionLimits.Default, CancellationToken.None);
        var tree = new ArchiveTree(info, ExtractionLimits.Default);
        Assert.Equal(2, tree.EncryptedCount);
        Assert.EndsWith(" · 2 com senha", tree.Summary, StringComparison.Ordinal);
    }
}
