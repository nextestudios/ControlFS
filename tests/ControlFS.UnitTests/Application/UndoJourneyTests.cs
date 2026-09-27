using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Text;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>
/// Desfazer/refazer (#22). Desfazer pode remover arquivos: cada inverso é provado aqui, e também a recusa quando o disco
/// mudou desde a operação (nada é alterado nesse caso).
/// </summary>
public class UndoJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>Serviço real sem Lixeira: exclusões são permanentes (nada vai para a Lixeira do agente de CI).</summary>
    private sealed class NoRecycleBin : IFileOperationService
    {
        private readonly FileOperationService _real = new();
        public bool CanRecycle(string path) => false;
        public FileEntry Rename(string path, string newName) => _real.Rename(path, newName);
        public Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
            _real.RunAsync(request, conflicts, progress, cancellationToken);
    }

    /// <summary>Lixeira simulada numa pasta temporária: "excluir" move para lá e restaurar devolve (sem sobrescrever).</summary>
    private sealed class FolderRecycleBin(string folder) : IFileOperationService, IRecycleBin
    {
        private readonly FileOperationService _real = new();
        public List<RecycledItem> Items { get; } = [];
        public bool CanRecycle(string path) => true;
        public FileEntry Rename(string path, string newName) => _real.Rename(path, newName);

        public async Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
        {
            if (request.Kind != FileOperationKind.Delete || request.Permanent) return await _real.RunAsync(request, conflicts, progress, cancellationToken);
            await Task.Yield(); // como o motor real, termina depois de ser enfileirado
            var results = new List<ItemResult>();
            foreach (var source in request.Sources)
            {
                var stored = Path.Join(folder, Guid.NewGuid().ToString("N"));
                File.Move(source, stored);
                Items.Add(new RecycledItem(stored, Path.GetFileName(source), source, false, new FileInfo(stored).Length, DateTimeOffset.Now));
                results.Add(new ItemResult(Path.GetFileName(source), ItemOutcome.Succeeded, Message: "Movido para a Lixeira.") { SourcePath = source });
            }
            return new OperationResult(OperationState.Completed, results);
        }

        public IReadOnlyList<RecycledItem> List(CancellationToken cancellationToken) => [.. Items];

        public string Restore(string id)
        {
            var item = Items.Single(i => i.Id == id);
            if (File.Exists(item.OriginalPath)) throw new FileOperationException(OperationErrorKind.AlreadyExists, "Já existe.");
            File.Move(item.Id, item.OriginalPath);
            Items.Remove(item);
            return item.OriginalPath;
        }

        public void DeletePermanently(string id) => throw new NotSupportedException();
    }

    private Driver Boot(IFileOperationService ops, IRecycleBin? bin = null)
    {
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), fileOperations: ops, recycleBin: bin);
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.Confirm);
        return d;
    }

    private static async Task ChooseAppMenu(Driver d, string label)
    {
        d.Press(InputAction.OpenAppMenu);
        await d.ChooseMenu(label);
    }

    private static async Task Undo(Driver d)
    {
        await ChooseAppMenu(d, "Desfazer: ");
        var confirm = await d.WaitDialog("Desfazer \"");
        Assert.Equal("Cancelar", confirm.Options[confirm.FocusIndex].Label); // começa na opção segura
        d.ChooseOption(confirm, "Desfazer");
    }

    private static async Task<MenuItem> AppMenuItem(Driver d, string labelStart)
    {
        d.Press(InputAction.OpenAppMenu);
        var item = (await d.WaitMenu()).Items.First(i => i.Label.StartsWith(labelStart, StringComparison.Ordinal));
        d.Press(InputAction.Back);
        return item;
    }

    [Fact]
    public void Undo_of_a_rename_restores_the_previous_name_and_redo_renames_it_again() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("rascunho.txt"), "texto");
        var d = Boot(new FileOperationService());
        await d.Idle();
        await d.FocusItem("rascunho.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Renomear…");
        var kb = await d.WaitKeyboard();
        d.TypeOnKeyboard(kb, "final");
        d.PressKey(kb, KeyKind.Done);
        await UiContext.WaitUntil(() => File.Exists(_tmp.Sub("final.txt")), "renomeado");
        await d.Idle();

        await Undo(d);
        await UiContext.WaitUntil(() => File.Exists(_tmp.Sub("rascunho.txt")), "nome anterior de volta");
        await d.Idle();
        Assert.False(File.Exists(_tmp.Sub("final.txt")));
        Assert.Equal("texto", File.ReadAllText(_tmp.Sub("rascunho.txt")));

        await UiContext.WaitUntil(() => d.App.RedoTitle is not null, "refazer disponível"); // o arquivo volta antes de a operação terminar
        await ChooseAppMenu(d, "Refazer: ");
        await UiContext.WaitUntil(() => File.Exists(_tmp.Sub("final.txt")), "renomeado de novo");
        await d.Idle();
        Assert.False(File.Exists(_tmp.Sub("rascunho.txt")));
        Assert.True((await AppMenuItem(d, "Desfazer")).IsEnabled, "o refeito pode ser desfeito outra vez");
    });

    [Fact]
    public void Undo_of_a_move_brings_items_back_but_is_refused_with_a_reason_when_the_original_place_is_taken() => UiContext.Run(async () =>
    {
        _tmp.MakeDir("Pasta");
        File.WriteAllText(_tmp.Sub("Pasta", "dentro.txt"), "d");
        File.WriteAllText(_tmp.Sub("a.txt"), "original");
        _tmp.MakeDir("Destino");
        var d = Boot(new NoRecycleBin());
        await d.Idle();

        d.App.ConfirmTransfer(FileOperationKind.Move, [_tmp.Sub("Pasta"), _tmp.Sub("a.txt")], _tmp.Sub("Destino"), _tmp.Path);
        d.ChooseOption(await d.WaitDialog("Mover 2 itens?"), "Mover");
        var done = await d.WaitDialog("Mover: concluído");
        d.ChooseOption(done, "Desfazer"); // ação no próprio resultado
        d.ChooseOption(await d.WaitDialog("Desfazer \""), "Desfazer");
        await UiContext.WaitUntil(() => File.Exists(_tmp.Sub("a.txt")) && Directory.Exists(_tmp.Sub("Pasta")), "itens de volta");
        await d.Idle();
        Assert.Equal("d", File.ReadAllText(_tmp.Sub("Pasta", "dentro.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_tmp.Sub("Destino")));

        // Move de novo; depois alguém cria outro a.txt na origem: desfazer é recusado e nada muda.
        d.App.ConfirmTransfer(FileOperationKind.Move, [_tmp.Sub("a.txt")], _tmp.Sub("Destino"), _tmp.Path);
        d.ChooseOption(await d.WaitDialog("Mover 1 item?"), "Mover");
        d.ChooseOption(await d.WaitDialog("Mover: concluído"), "Fechar");
        File.WriteAllText(_tmp.Sub("a.txt"), "novo");
        await Undo(d);
        var refused = await d.WaitDialog("Não foi possível desfazer");
        Assert.Contains(refused.Lines, l => l.Value.Contains("Já existe", StringComparison.Ordinal));
        Assert.Equal("novo", File.ReadAllText(_tmp.Sub("a.txt")));
        Assert.Equal("original", File.ReadAllText(_tmp.Sub("Destino", "a.txt")));
        d.ChooseOption(refused, "Fechar");
        Assert.False((await AppMenuItem(d, "Desfazer")).IsEnabled);
    });

    [Fact]
    public void Undo_of_a_copy_removes_only_an_unchanged_copy_and_permanent_delete_offers_no_undo() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("nota.txt"), "nota");
        _tmp.MakeDir("Destino");
        var d = Boot(new NoRecycleBin());
        await d.Idle();

        // Cópia editada depois: não é removida.
        d.App.ConfirmTransfer(FileOperationKind.Copy, [_tmp.Sub("nota.txt")], _tmp.Sub("Destino"), _tmp.Path);
        d.ChooseOption(await d.WaitDialog("Copiar 1 item?"), "Copiar");
        d.ChooseOption(await d.WaitDialog("Copiar: concluído"), "Fechar");
        File.AppendAllText(_tmp.Sub("Destino", "nota.txt"), " editada");
        await Undo(d);
        var refused = await d.WaitDialog("Não foi possível desfazer");
        Assert.Contains(refused.Lines, l => l.Value.Contains("alterada", StringComparison.Ordinal));
        Assert.Equal("nota editada", File.ReadAllText(_tmp.Sub("Destino", "nota.txt")));
        d.ChooseOption(refused, "Fechar");

        // Cópia intacta (mantida como "nota (2).txt"): desfazer remove só ela.
        d.App.ConfirmTransfer(FileOperationKind.Copy, [_tmp.Sub("nota.txt")], _tmp.Sub("Destino"), _tmp.Path);
        d.ChooseOption(await d.WaitDialog("Copiar 1 item?"), "Copiar");
        d.ChooseOption(await d.WaitDialog("Já existe"), "Manter ambos");
        d.ChooseOption(await d.WaitDialog("Copiar: concluído"), "Fechar");
        Assert.True(File.Exists(_tmp.Sub("Destino", "nota (2).txt")));
        await Undo(d);
        await UiContext.WaitUntil(() => !File.Exists(_tmp.Sub("Destino", "nota (2).txt")), "cópia removida");
        await d.Idle();
        Assert.Equal("nota", File.ReadAllText(_tmp.Sub("nota.txt")));
        Assert.Equal("nota editada", File.ReadAllText(_tmp.Sub("Destino", "nota.txt")));

        // Exclusão permanente nunca entra na lista de desfazer.
        await d.Idle();
        await d.FocusItem("nota.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Excluir…");
        d.ChooseOption(await d.WaitDialog("Excluir 1 item permanentemente?"), "Excluir permanentemente");
        await UiContext.WaitUntil(() => !File.Exists(_tmp.Sub("nota.txt")), "excluído");
        await d.Idle();
        var undo = await AppMenuItem(d, "Desfazer");
        Assert.False(undo.IsEnabled);
        Assert.Equal("Desfazer", undo.Label);
    });

    [Fact]
    public void Undo_of_a_recycle_restores_from_the_bin_and_is_refused_when_the_name_is_taken_again() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("a.txt"), "a");
        var bin = new FolderRecycleBin(_tmp.MakeDir(".bin"));
        var d = Boot(bin, bin);
        await d.Idle();
        await d.FocusItem("a.txt");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Excluir…");
        d.ChooseOption(await d.WaitDialog("Mover 1 item para a Lixeira?"), "Mover para a Lixeira");
        await UiContext.WaitUntil(() => !File.Exists(_tmp.Sub("a.txt")), "na Lixeira");
        await d.Idle();

        await Undo(d);
        await UiContext.WaitUntil(() => File.Exists(_tmp.Sub("a.txt")), "restaurado");
        await d.Idle();
        Assert.Empty(bin.Items);

        await UiContext.WaitUntil(() => d.App.RedoTitle is not null, "refazer disponível"); // o arquivo volta antes de a operação terminar
        await ChooseAppMenu(d, "Refazer: ");
        await UiContext.WaitUntil(() => !File.Exists(_tmp.Sub("a.txt")), "na Lixeira de novo");
        await d.Idle();

        File.WriteAllText(_tmp.Sub("a.txt"), "outro");
        await Undo(d);
        var refused = await d.WaitDialog("Não foi possível desfazer");
        Assert.Contains(refused.Lines, l => l.Value.Contains("Já existe", StringComparison.Ordinal));
        Assert.Equal("outro", File.ReadAllText(_tmp.Sub("a.txt")));
        Assert.Single(bin.Items); // continua na Lixeira
    });
}
