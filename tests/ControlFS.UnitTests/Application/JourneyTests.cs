using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;
using static ControlFS.UnitTests.Support.ZipFixtures;

namespace ControlFS.UnitTests.Application;

/// <summary>
/// Jornadas de ponta a ponta sobre arquivos reais (diretório temporário), dirigidas apenas por
/// ações semânticas. Não substituem testes de UI WinUI nem de hardware real.
/// </summary>
public class JourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private (Driver Driver, TestFileSystem Fs) Boot()
    {
        var fs = new TestFileSystem(_tmp.Path);
        var app = new AppController(fs, new ArchiveService());
        app.Start();
        return (new Driver(app), fs);
    }

    [Fact]
    public void Vertical_journey_create_accented_folder_then_extract_zip_there_using_only_semantic_actions() => UiContext.Run(async () =>
    {
        Create(_tmp.Sub("pacote.zip"), Text("leia-me.txt", "olá"), Text("img/foto.png", "PNG"));
        var (d, _) = Boot();
        var app = d.App;

        // Início -> pasta de teste
        Assert.Equal(Screen.Home, app.Screen);
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal(Screen.Browser, app.Screen);
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);

        // Menu -> Nova pasta -> teclado virtual (limpar, digitar com acentos, OK)
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Nova pasta");
        var kb = await d.WaitKeyboard();
        Assert.Equal("Nova pasta", kb.Keyboard.Text);
        d.PressKey(kb, KeyKind.Clear);
        d.TypeOnKeyboard(kb, "Relatórios ação");
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => app.TopModal is null, "teclado fechado");
        await d.Idle();
        Assert.True(Directory.Exists(_tmp.Sub("Relatórios ação")));
        Assert.Equal("Relatórios ação", app.Browser.List.Focused?.Name); // foco na pasta criada

        // Focar o ZIP -> Ações -> Extrair para… -> seletor interno -> entrar na pasta -> escolher
        await d.FocusItem("pacote.zip");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Extrair para…");
        await d.Idle();
        Assert.Equal(Screen.FolderPicker, app.Screen);
        Assert.All(app.Picker.List.Items, i => Assert.True(i.IsContainer)); // seletor mostra só pastas
        await d.FocusItem("Relatórios ação");
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Escolher esta pasta");

        // Resumo final: origem, destino, entradas, conflitos -> Extrair
        var summary = await d.WaitDialog("Extrair");
        Assert.Contains(summary.Lines, l => l.Label == "Destino" && l.Value.Contains("Relatórios ação", StringComparison.Ordinal));
        Assert.Contains(summary.Lines, l => l.Label == "Conflitos");
        d.ChooseOption(summary, "Extrair");

        var result = await d.WaitDialog("Extração concluída");
        Assert.Contains(result.Lines, l => l is ("Extraídos", "2"));
        d.ChooseOption(result, "Abrir pasta extraída");
        await d.Idle();

        var dest = Path.Join(_tmp.Path, "Relatórios ação", "pacote");
        Assert.Equal(dest, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal(["img", "leia-me.txt"], app.Browser.List.Items.Select(i => i.Name));
        Assert.Equal("olá", File.ReadAllText(Path.Join(dest, "leia-me.txt")));
        Assert.Equal("PNG", File.ReadAllText(Path.Join(dest, "img", "foto.png")));
        Assert.Equal(OperationState.Completed, app.Operations.Items.Single().State);
    });

    [Fact]
    public void Extract_here_twice_resolves_conflict_keeping_both() => UiContext.Run(async () =>
    {
        Create(_tmp.Sub("dados.zip"), Text("a.txt", "A"));
        var (d, _) = Boot();
        var app = d.App;
        d.Press(InputAction.Confirm);
        await d.FocusItem("dados.zip");

        for (var round = 0; round < 2; round++)
        {
            d.Press(InputAction.OpenContextMenu);
            await d.ChooseMenu("Extrair aqui");
            var summary = await d.WaitDialog("Extrair");
            d.ChooseOption(summary, "Extrair");
            if (round == 1)
            {
                var conflict = await d.WaitDialog("Já existe");
                Assert.Equal(0, conflict.FocusIndex); // foco inicial: pular (preserva o existente)
                Assert.Equal(OperationState.WaitingForUser, app.Operations.Items[^1].State);
                d.ChooseOption(conflict, "Manter ambos");
            }
            var done = await d.WaitDialog("Extração concluída");
            d.ChooseOption(done, "Fechar");
            await d.Idle();
        }

        Assert.Equal("A", File.ReadAllText(_tmp.Sub("a.txt")));
        Assert.Equal("A", File.ReadAllText(_tmp.Sub("a (2).txt")));
    });

    [Fact]
    public void Retry_failed_items_of_a_cancelled_extraction_only_extracts_what_was_left() => UiContext.Run(async () =>
    {
        Create(_tmp.Sub("dados.zip"), Text("a.txt", "A"), Text("b.txt", "B"), Text("c.txt", "C"));
        File.WriteAllText(_tmp.Sub("b.txt"), "existente");
        var (d, _) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("dados.zip");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Extrair aqui");
        d.ChooseOption(await d.WaitDialog("Extrair"), "Extrair");
        d.ChooseOption(await d.WaitDialog("Já existe"), "Cancelar operação");
        var cancelled = await d.WaitDialog("Extração cancelada");
        File.WriteAllText(_tmp.Sub("a.txt"), "editado depois"); // já extraído: não pode ser tocado de novo

        d.ChooseOption(cancelled, "Tentar de novo só as falhas (2)");
        d.ChooseOption(await d.WaitDialog("Já existe"), "Manter ambos");
        await d.WaitDialog("Extração concluída");

        var retried = d.App.Operations.Items[^1].Result!;
        Assert.Equal(["b.txt", "c.txt"], retried.Items.Select(i => i.Name).Order(StringComparer.Ordinal));
        Assert.Equal("editado depois", File.ReadAllText(_tmp.Sub("a.txt")));
        Assert.Equal("existente", File.ReadAllText(_tmp.Sub("b.txt")));
        Assert.Equal("B", File.ReadAllText(_tmp.Sub("b (2).txt")));
        Assert.Equal("C", File.ReadAllText(_tmp.Sub("c.txt")));
    });

    [Fact]
    public void Conflict_dialog_back_skips_and_replace_requires_second_confirmation() => UiContext.Run(async () =>
    {
        Create(_tmp.Sub("d.zip"), Text("a.txt", "NOVO"), Text("b.txt", "NOVO"));
        File.WriteAllText(_tmp.Sub("a.txt"), "velho");
        File.WriteAllText(_tmp.Sub("b.txt"), "velho");
        var (d, _) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("d.zip");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Extrair aqui");
        d.ChooseOption(await d.WaitDialog("Extrair"), "Extrair");

        await d.WaitDialog("Já existe");
        d.Press(InputAction.Back); // Voltar = pular (seguro)

        var second = await d.WaitDialog("Já existe");
        d.ChooseOption(second, "Substituir");
        var confirm = await d.WaitDialog("Substituir arquivo existente?");
        Assert.Equal("Cancelar", confirm.Options[confirm.FocusIndex].Label); // começa na opção segura
        d.ChooseOption(confirm, "Substituir");

        var done = await d.WaitDialog("Extração concluída");
        Assert.Contains(done.Lines, l => l is ("Ignorados", "1"));
        Assert.Contains(done.Lines, l => l is ("Substituídos", "1"));
        Assert.Equal("velho", File.ReadAllText(_tmp.Sub("a.txt")));
        Assert.Equal("NOVO", File.ReadAllText(_tmp.Sub("b.txt")));
    });

    [Fact]
    public void Test_integrity_from_actions_menu_reports_the_corrupted_entry_without_extracting() => UiContext.Run(async () =>
    {
        var zip = Create(_tmp.Sub("baixado.zip"), Text("ok.txt", "intacto"),
            new Item("dados.txt", System.Text.Encoding.ASCII.GetBytes("CONTEUDO-ORIGINAL-1234567890"), Level: System.IO.Compression.CompressionLevel.NoCompression));
        var bytes = File.ReadAllBytes(zip);
        bytes[bytes.AsSpan().IndexOf("ORIGINAL"u8)] ^= 0x20;
        File.WriteAllBytes(zip, bytes);
        var (d, _) = Boot();
        d.Press(InputAction.Confirm);
        await d.FocusItem("baixado.zip");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Testar integridade");

        var result = await d.WaitDialog("Integridade: problemas encontrados");
        Assert.Contains(result.Lines, l => l.Item1 == "Conferidas pelo CRC" && l.Item2 == "1");
        Assert.Contains(result.Lines, l => l.Item1 == "• dados.txt");
        Assert.Contains("não é uma verificação de vírus", result.Message, StringComparison.Ordinal);
        Assert.Equal(["baixado.zip"], Directory.EnumerateFileSystemEntries(_tmp.Path).Select(Path.GetFileName));
    });

    [Fact]
    public void Password_flow_retries_after_wrong_password() => UiContext.Run(async () =>
    {
        File.Copy(FixturePath("zip/zipcrypto-senha-certa.zip"), _tmp.Sub("cofre.zip"));
        var (d, _) = Boot();
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
        Assert.Equal("conteúdo protegido\n", File.ReadAllText(_tmp.Sub("cofre", "segredo.txt")));
        Assert.False(Directory.Exists(_tmp.Sub("cofre (2)")), "a tentativa com senha errada não deixou pasta para trás");
    });

    [Fact]
    public void Archive_is_browsed_read_only_and_back_returns_to_disk() => UiContext.Run(async () =>
    {
        Create(_tmp.Sub("arq.zip"), Text("docs/a.txt", "a"), Text("docs/sub/b.txt", "b"), Text("../fora.txt", "x"), Text("raiz.txt", "r"));
        var (d, _) = Boot();
        var app = d.App;
        d.Press(InputAction.Confirm);
        await d.FocusItem("arq.zip");
        d.Press(InputAction.Confirm);
        await d.Idle();

        var location = Assert.IsType<ArchiveLocation>(app.Browser.Location);
        Assert.Equal("", location.InnerPath);
        Assert.Contains(app.Browser.List.Items, i => i.IsBlocked && i.Name == "../fora.txt");
        Assert.Equal(1, app.Browser.Archive!.BlockedCount);
        Assert.Equal(["arq.zip"], Directory.EnumerateFileSystemEntries(_tmp.Path).Select(Path.GetFileName)); // nada extraído para listar

        await d.FocusItem("docs");
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Equal("docs", ((ArchiveLocation)app.Browser.Location!).InnerPath);
        Assert.Equal(["sub", "a.txt"], app.Browser.List.Items.Select(i => i.Name));

        d.Press(InputAction.OpenAppMenu);
        await d.WaitMenu();
        Assert.DoesNotContain(((MenuModal)app.TopModal!).Items, i => i.Label == "Nova pasta" && i.IsEnabled); // somente leitura
        d.Press(InputAction.Back);

        d.Press(InputAction.Back);
        await d.Idle();
        Assert.Equal("", ((ArchiveLocation)app.Browser.Location!).InnerPath);
        d.Press(InputAction.Back);
        await d.Idle();
        Assert.IsType<PhysicalLocation>(app.Browser.Location);
        Assert.Equal("arq.zip", app.Browser.List.Focused?.Name); // foco restaurado no item de origem
    });

    [Fact]
    public void Back_semantics_selection_then_history_then_home_then_confirmed_exit() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("A");
        File.WriteAllText(_tmp.Sub("A", "x.txt"), "x");
        var (d, _) = Boot();
        var app = d.App;
        var exited = false;
        app.ExitRequested += () => exited = true;

        d.Press(InputAction.Confirm);
        await d.FocusItem("A");
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.ToggleSelection);
        Assert.Equal(1, app.Browser.List.SelectionCount);

        d.Press(InputAction.Back); // 1) limpa seleção
        Assert.Equal(0, app.Browser.List.SelectionCount);
        Assert.EndsWith("A", ((PhysicalLocation)app.Browser.Location!).FullPath, StringComparison.Ordinal);

        d.Press(InputAction.Back); // 2) histórico
        await d.Idle();
        Assert.Equal(_tmp.Path, ((PhysicalLocation)app.Browser.Location!).FullPath);
        Assert.Equal("A", app.Browser.List.Focused?.Name);

        d.Press(InputAction.Back); // 3) início
        Assert.Equal(Screen.Home, app.Screen);

        d.Press(InputAction.Back); // 4) confirmação de saída, foco em "Cancelar"
        var exit = await d.WaitDialog("Sair");
        Assert.Equal("Cancelar", exit.Options[exit.FocusIndex].Label);
        d.Press(InputAction.Confirm);
        Assert.False(exited);
        Assert.Null(app.TopModal);
    });

    [Fact]
    public void Late_listing_response_does_not_overwrite_newer_navigation() => UiContext.Run(async () =>
    {
        var slow = _tmp.MakeDir("lenta");
        _tmp.MakeDir("rapida");
        File.WriteAllText(Path.Join(slow, "de-lenta.txt"), "x");
        var (d, fs) = Boot();
        var app = d.App;
        d.Press(InputAction.Confirm);
        await d.Idle();
        fs.Delays[Path.GetFullPath(slow)] = TimeSpan.FromMilliseconds(400);

        await d.FocusItem("lenta");
        d.Press(InputAction.Confirm);         // inicia listagem lenta
        d.Press(InputAction.Back);            // cancela o carregamento
        await d.FocusItem("rapida");
        d.Press(InputAction.Confirm);
        await d.Idle();
        await Task.Delay(500);

        Assert.EndsWith("rapida", ((PhysicalLocation)app.Browser.Location!).FullPath, StringComparison.Ordinal);
        Assert.DoesNotContain(app.Browser.List.Items, i => i.Name == "de-lenta.txt");
    });

    [Fact]
    public void Invalid_folder_name_keeps_keyboard_open_with_reason() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("existente");
        var (d, _) = Boot();
        d.Press(InputAction.Confirm);
        await d.Idle();
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu("Nova pasta");
        var kb = await d.WaitKeyboard();
        d.PressKey(kb, KeyKind.Clear);
        d.TypeOnKeyboard(kb, "existente");
        d.PressKey(kb, KeyKind.Done);
        await d.Idle();
        Assert.Same(kb, d.App.TopModal);
        Assert.Contains("Já existe", kb.Keyboard.ErrorMessage, StringComparison.Ordinal);

        d.PressKey(kb, KeyKind.Clear);
        d.TypeOnKeyboard(kb, "CON");
        d.PressKey(kb, KeyKind.Done);
        Assert.Contains("reservado", kb.Keyboard.ErrorMessage, StringComparison.Ordinal);
        d.Press(InputAction.Back);
        Assert.Null(d.App.TopModal);
        Assert.False(Directory.Exists(_tmp.Sub("CON")));
    });

    [Fact]
    public void Footer_hints_only_show_actions_that_work_in_context() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("f.txt"), "x");
        var (d, _) = Boot();
        Assert.Contains(d.App.Hints, h => h is { Action: InputAction.Back, Label: "Sair" });
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.Contains(d.App.Hints, h => h.Action == InputAction.ToggleSelection);
        Assert.Contains(d.App.Hints, h => h is { Action: InputAction.Search, Label: "Buscar" });
        d.Press(InputAction.ToggleSelection);
        Assert.Contains(d.App.Hints, h => h is { Action: InputAction.Back, Label: "Cancelar seleção" });
    });
}
