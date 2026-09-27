using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class ShellAndCompressJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private sealed class FakeShell : IShellService
    {
        public List<(string Verb, string Path)> Calls { get; } = [];
        public void Open(string path) => Calls.Add(("open", path));
        public void OpenWith(string path) => Calls.Add(("openwith", path));
        public void RevealInExplorer(string path) => Calls.Add(("reveal", path));
    }

    private (Driver Driver, FakeShell Shell) Boot()
    {
        var shell = new FakeShell();
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), shell: shell);
        app.Start();
        return (new Driver(app), shell);
    }

    [Fact]
    public void Confirm_on_a_document_opens_it_with_windows_and_warns_about_leaving() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("relatorio.pdf"), "x");
        var (d, shell) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("relatorio.pdf");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal([("open", _tmp.Sub("relatorio.pdf"))], shell.Calls);
        Assert.Contains("outro programa", d.App.StatusMessage, StringComparison.Ordinal);
    });

    [Fact]
    public void Executables_require_explicit_confirmation_starting_on_cancel() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("setup.exe"), "MZ");
        var (d, shell) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("setup.exe");
        d.Press(InputAction.Confirm);
        var dialog = await d.WaitDialog("Executar este arquivo?");
        Assert.Equal("Cancelar", dialog.Options[dialog.FocusIndex].Label);
        d.Press(InputAction.Confirm); // Cancelar
        Assert.Empty(shell.Calls);

        d.Press(InputAction.Confirm);
        d.ChooseOption(await d.WaitDialog("Executar este arquivo?"), "Executar");
        Assert.Equal([("open", _tmp.Sub("setup.exe"))], shell.Calls);
    });

    [Fact]
    public void File_menu_offers_open_with_and_reveal() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("foto.jpg"), "x");
        var (d, shell) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("foto.jpg");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Abrir com…");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Mostrar no Explorador");
        Assert.Equal([("openwith", _tmp.Sub("foto.jpg")), ("reveal", _tmp.Sub("foto.jpg"))], shell.Calls);
    });

    [Fact]
    public void Compress_marked_items_as_tar_gz_then_browse_the_result() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("fotos");
        File.WriteAllText(_tmp.Sub("fotos", "a.txt"), "a");
        File.WriteAllText(_tmp.Sub("b.txt"), "b");
        var (d, _) = Boot();
        var app = d.App;
        d.Press(InputAction.Confirm);
        await d.FocusItem("fotos");
        d.Press(InputAction.ToggleSelection);
        await d.FocusItem("b.txt");
        d.Press(InputAction.ToggleSelection);
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Compactar 2 itens");

        var dialog = await d.WaitDialog("Compactar");
        d.ChooseOption(dialog, "Formato: ZIP");  // alterna para TAR.GZ
        Assert.Contains(dialog.Lines, l => l.Label == "Arquivo" && l.Value.EndsWith(".tar.gz", StringComparison.Ordinal));
        d.ChooseOption(dialog, "Compactar");

        var done = await d.WaitDialog("Compactado criado");
        var created = done.Lines.Single(l => l.Label == "Arquivo").Value;
        Assert.True(File.Exists(created));
        d.ChooseOption(done, "Mostrar o arquivo");
        await d.Idle();
        Assert.Equal(Path.GetFileName(created), app.Browser.List.Focused?.Name);

        d.Press(InputAction.Confirm); // abre o TAR.GZ no navegador somente leitura
        await d.Idle();
        Assert.IsType<ArchiveLocation>(app.Browser.Location);
        Assert.Equal(["fotos", "b.txt"], app.Browser.List.Items.Select(i => i.Name));
    });

    [Fact]
    public void Compress_name_is_typed_with_the_virtual_keyboard_and_validated() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("x.txt"), "x");
        var (d, _) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("x.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Compactar…");
        var dialog = await d.WaitDialog("Compactar");
        d.ChooseOption(dialog, "Nome:");
        var kb = await d.WaitKeyboard();
        d.PressKey(kb, Keys.Clear);
        d.TypeOnKeyboard(kb, "CON");
        d.PressKey(kb, Keys.Done);
        Assert.NotNull(kb.Keyboard.ErrorMessage); // nome reservado recusado
        d.PressKey(kb, Keys.Clear);
        d.TypeOnKeyboard(kb, "Backup ação");
        d.PressKey(kb, Keys.Done);
        await UiContext.WaitUntil(() => d.App.TopModal is DialogModal, "volta ao resumo");
        d.ChooseOption((DialogModal)d.App.TopModal!, "Compactar");
        await d.WaitDialog("Compactado criado");
        Assert.True(File.Exists(_tmp.Sub("Backup ação.zip")));
    });

    [Theory]
    [InlineData("setup.exe", true)]
    [InlineData("script.PS1", true)]
    [InlineData("atalho.lnk", true)]
    [InlineData("instalar.msi", true)]
    [InlineData("truque.exe. ", true)]
    [InlineData("foto.jpg", false)]
    [InlineData("texto.txt", false)]
    [InlineData("pacote.zip", false)]
    public void Executable_detection(string name, bool expected) => Assert.Equal(expected, ExecutableFiles.IsPotentiallyExecutable(name));

    private static class Keys
    {
        public const ControlFS.Core.Text.KeyKind Clear = ControlFS.Core.Text.KeyKind.Clear;
        public const ControlFS.Core.Text.KeyKind Done = ControlFS.Core.Text.KeyKind.Done;
    }
}
