using System.Text;
using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Edição leve a partir da visualização de texto (#62), só com ações semânticas.</summary>
public class TextEditJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static readonly byte[] Bom = Encoding.UTF8.GetPreamble();

    private async Task<(AppController App, Driver Driver, TextPreviewModal Preview)> OpenAsync(string name)
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        await d.FocusItem(name);
        d.Press(InputAction.Confirm);
        await d.Idle();
        return (app, d, Assert.IsType<TextPreviewModal>(app.TopModal));
    }

    [Fact]
    public void North_edits_lines_on_the_keyboard_and_start_saves_atomically_keeping_encoding_endings_and_a_backup() => UiContext.Run(async () =>
    {
        var path = _tmp.Sub("config.ini");
        var original = Bom.Concat(Encoding.UTF8.GetBytes("nome=ControlFS\r\nporta=80\r\nfim")).ToArray();
        File.WriteAllBytes(path, original);
        var (app, d, preview) = await OpenAsync("config.ini");
        Assert.Contains(app.Hints, h => h.Action == InputAction.OpenContextMenu && h.Label == "Editar");

        d.Press(InputAction.OpenContextMenu);
        await d.Idle();
        var editor = Assert.IsType<TextEditor>(preview.Editor);
        d.Press(InputAction.NavigateDown);
        d.Press(InputAction.Confirm); // Sul: a linha em foco no teclado virtual
        var kb = await d.WaitKeyboard();
        Assert.Equal("porta=80", kb.Keyboard.Text);
        d.PressKey(kb, KeyKind.Clear);
        d.TypeOnKeyboard(kb, "porta nova");
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => app.TopModal is TextPreviewModal, "teclado fechado");
        Assert.Equal("porta nova", editor.Lines[1].Text);
        Assert.True(editor.IsModified);

        // Linha nova abaixo da última (que não tinha quebra): a quebra passa para "fim" e a nova fica sem.
        d.Press(InputAction.NextRegion);
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Inserir linha abaixo");
        kb = await d.WaitKeyboard();
        d.TypeOnKeyboard(kb, "extra");
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => app.TopModal is TextPreviewModal, "teclado fechado");
        Assert.Equal(3, editor.Cursor);

        // Apagar e desfazer.
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Apagar a linha 4");
        Assert.Equal(3, editor.Lines.Count);
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Desfazer");
        Assert.Equal("extra", editor.Lines[3].Text);
        Assert.Equal(original, File.ReadAllBytes(path)); // nada no disco até salvar

        // Start: confirma antes de substituir; Salvar grava de uma vez e guarda o original ao lado.
        d.Press(InputAction.OpenAppMenu);
        var confirm = await d.WaitDialog("Salvar as alterações?");
        Assert.Contains(confirm.Lines, l => l.Label == "Cópia do original" && l.Value == "config.ini.controlfs.bak");
        d.ChooseOption(confirm, "Salvar");
        await d.Idle();
        Assert.Equal(Bom.Concat(Encoding.UTF8.GetBytes("nome=ControlFS\r\nporta nova\r\nfim\r\nextra")).ToArray(), File.ReadAllBytes(path));
        Assert.Equal(original, File.ReadAllBytes(path + ".controlfs.bak"));
        Assert.Null(preview.Editor);
        Assert.Equal("porta nova", preview.Document!.Lines[1]); // a visualização mostra o arquivo salvo
        Assert.Empty(Directory.GetFiles(_tmp.Path, ".controlfs-edit-*")); // nenhum temporário para trás
    });

    [Fact]
    public void Back_with_changes_asks_before_discarding_and_a_file_changed_on_disk_is_never_overwritten_silently() => UiContext.Run(async () =>
    {
        var path = _tmp.Sub("notas.txt");
        File.WriteAllText(path, "um\ndois\n");
        var (app, d, preview) = await OpenAsync("notas.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.Idle();
        d.Press(InputAction.Confirm);
        var kb = await d.WaitKeyboard();
        d.TypeOnKeyboard(kb, "s");
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => app.TopModal is TextPreviewModal, "teclado fechado");

        // Voltar com alterações: pergunta, começando em "Continuar editando".
        d.Press(InputAction.Back);
        var discard = await d.WaitDialog("Descartar as alterações?");
        Assert.Equal("Continuar editando", discard.Options[discard.FocusIndex].Label);
        d.Press(InputAction.Back);
        Assert.NotNull(preview.Editor);

        // Outro programa mexe no arquivo: salvar avisa e, cancelado, não toca em nada.
        File.WriteAllText(path, "versão de outro programa\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        d.Press(InputAction.OpenAppMenu);
        d.ChooseOption(await d.WaitDialog("Salvar as alterações?"), "Salvar");
        var changed = await d.WaitDialog("O arquivo mudou no disco");
        Assert.Equal("Continuar editando", changed.Options[changed.FocusIndex].Label);
        d.Press(InputAction.Confirm);
        Assert.Equal("versão de outro programa\n", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".controlfs.bak"));

        // Descartar sai da edição sem gravar.
        d.Press(InputAction.Back);
        d.ChooseOption(await d.WaitDialog("Descartar as alterações?"), "Descartar");
        Assert.Null(preview.Editor);
        Assert.Equal("versão de outro programa\n", File.ReadAllText(path));
    });

    [Fact]
    public void Read_only_files_are_refused_with_the_reason() => UiContext.Run(async () =>
    {
        var path = _tmp.Sub("travado.txt");
        File.WriteAllText(path, "não mexa");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var (app, d, preview) = await OpenAsync("travado.txt");
            d.Press(InputAction.OpenContextMenu);
            await d.Idle();
            Assert.Null(preview.Editor);
            Assert.Contains("somente leitura", app.StatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    });
}
