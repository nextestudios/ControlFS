using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Archives.Creation;
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

    /// <summary><paramref name="rar"/> falso: o WinRAR "instalado" para o teste; null: sem WinRAR (não depende da máquina).</summary>
    private (Driver Driver, FakeShell Shell) Boot(RarTool? rar = null)
    {
        var shell = new FakeShell();
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(rarLocator: () => rar), shell: shell);
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
        await d.PickOption(dialog, "Formato", "TAR.GZ");
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
    public void Compress_dialog_keeps_its_size_class_and_reserves_every_variant_text_across_formats() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("x.txt"), "x");
        var (d, _) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("x.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Compactar…");
        var dialog = await d.WaitDialog("Compactar");
        var size = dialog.Size;
        Assert.Equal(ModalSize.Standard, size);
        var formats = new List<string>();
        foreach (var format in new[] { "TAR.GZ", "7z", "ZIP" })
        {
            await d.PickOption(dialog, "Formato", format);
            formats.Add(dialog.Lines.Single(l => l.Label == "Formato").Value);
            Assert.Equal(size, dialog.Size); // a largura vem da classe, não do formato em foco (#227)
        }
        Assert.Equal(3, formats.Distinct().Count());
        // A altura reserva o texto mais longo de cada linha variável: todos os formatos e compressões, e o nome numerado.
        Assert.All(formats, f => Assert.Contains(f, dialog.LineReserve!["Formato"]));
        Assert.Equal(3, dialog.LineReserve!["Compressão"].Count);
        Assert.Contains(dialog.LineReserve["Arquivo"], t => t.Contains("numerado", StringComparison.Ordinal));
    });

    [Fact]
    public void Format_picker_lists_every_format_marks_the_current_and_returns_to_the_dialog_as_it_was() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("x.txt"), "x");
        var (d, _) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("x.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Compactar…");
        var dialog = await d.WaitDialog("Compactar");
        d.ChooseOption(dialog, "Formato: ZIP");
        var focusBefore = dialog.FocusIndex;

        // Abre com todos os formatos, a atual marcada (ícone e texto) e uma descrição em cada um.
        var picker = await d.WaitMenu();
        Assert.True(picker.IsPicker);
        Assert.Equal("Formato", picker.Title);
        Assert.Equal(["ZIP", "TAR.GZ", "7z", "RAR"], picker.Items.Select(i => i.Label));
        Assert.All(picker.Items, i => Assert.False(string.IsNullOrEmpty(i.Detail)));
        Assert.Equal(0, picker.FocusIndex);
        Assert.Equal((ActionIcon.RadioOn, "atual"), (picker.Items[0].Icon, picker.Items[0].Value));
        Assert.All(picker.Items.Skip(1), i => Assert.Equal((ActionIcon.RadioOff, (string?)null), (i.Icon, i.Value)));
        Assert.Contains("opção 1 de 4, selecionada", d.App.DescribeFocus().Item, StringComparison.Ordinal);
        d.Press(InputAction.NavigateDown);
        Assert.Contains("opção 2 de 4", d.App.DescribeFocus().Item, StringComparison.Ordinal);
        Assert.DoesNotContain("selecionada", d.App.DescribeFocus().Item, StringComparison.Ordinal);

        // Voltar deixa tudo como estava.
        d.Press(InputAction.Back);
        Assert.Same(dialog, d.App.TopModal);
        Assert.Equal((focusBefore, "Formato: ZIP"), (dialog.FocusIndex, dialog.Options[focusBefore].Label));

        // Escolher aplica e volta ao diálogo, com o foco na mesma opção; 7z é alcançável e o arquivo ganha a extensão.
        await d.PickOption(dialog, "Formato", "7z");
        Assert.Same(dialog, d.App.TopModal);
        Assert.Equal(("Formato: 7z", focusBefore), (dialog.Options[focusBefore].Label, dialog.FocusIndex));
        Assert.Contains(dialog.Lines, l => l.Label == "Arquivo" && l.Value.EndsWith(".7z", StringComparison.Ordinal));

        // A compressão também tem seletor, aberto na alternativa atual.
        d.ChooseOption(dialog, "Compressão");
        var strength = await d.WaitMenu();
        Assert.Equal(["rápida", "normal", "máxima (mais lenta)"], strength.Items.Select(i => i.Label));
        Assert.Equal(1, strength.FocusIndex);
        d.Press(InputAction.Back);
        Assert.Contains(dialog.Lines, l => l is { Label: "Compressão", Value: "normal" });
    });

    [Fact]
    public void Rar_is_listed_disabled_with_the_reason_without_winrar_and_selectable_with_it() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("x.txt"), "x");

        // Sem WinRAR: a alternativa aparece, desativada, dizendo o que fazer; escolher não muda o formato.
        var (d, _) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("x.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Compactar…");
        var dialog = await d.WaitDialog("Compactar");
        d.ChooseOption(dialog, "Formato: ZIP");
        var picker = await d.WaitMenu();
        var rar = picker.Items.Single(i => i.Label == "RAR");
        Assert.False(rar.IsEnabled);
        Assert.Contains("WinRAR", rar.DisabledReason, StringComparison.Ordinal);
        Assert.All(picker.Items.Where(i => i.Label != "RAR"), i => Assert.True(i.IsEnabled));
        d.Press(InputAction.Back);
        Assert.Equal("Formato: ZIP", dialog.Options.First(o => o.Label.StartsWith("Formato", StringComparison.Ordinal)).Label);

        // Com o WinRAR do usuário: dá para escolher e o arquivo ganha a extensão .rar.
        var (d2, _) = Boot(new RarTool("C:\\WinRAR\\Rar.exe"));
        d2.Press(InputAction.Confirm);
        await d2.FocusItem("x.txt");
        d2.Press(InputAction.OpenContextMenu);
        await d2.ChooseMenu("Compactar…");
        var dialog2 = await d2.WaitDialog("Compactar");
        await d2.PickOption(dialog2, "Formato", "RAR");
        Assert.Contains(dialog2.Lines, l => l.Label == "Arquivo" && l.Value.EndsWith(".rar", StringComparison.Ordinal));
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
