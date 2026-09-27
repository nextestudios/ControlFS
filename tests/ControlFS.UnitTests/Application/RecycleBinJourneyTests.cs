using ControlFS.Application;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class RecycleBinJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>Lixeira em memória: registra o que foi pedido (a Lixeira real é coberta nos testes de integração do Windows).</summary>
    private sealed class FakeBin : IRecycleBin
    {
        public List<RecycledItem> Items { get; } = [];
        public List<string> Restored { get; } = [];

        public IReadOnlyList<RecycledItem> List(CancellationToken cancellationToken) => [.. Items];

        public string Restore(string id)
        {
            var item = Items.Single(i => i.Id == id);
            if (item.Problem is not null) throw new FileOperationException(OperationErrorKind.PathRejected, item.Problem);
            Items.Remove(item);
            Restored.Add(item.OriginalPath);
            return item.OriginalPath;
        }

        public void DeletePermanently(string id) => Items.RemoveAll(i => i.Id == id);
    }

    [Fact]
    public void Home_opens_the_recycle_bin_where_items_are_restored_and_permanent_delete_asks_first_starting_on_cancel() => UiContext.Run(async () =>
    {
        var bin = new FakeBin();
        var deleted = DateTimeOffset.Now.AddHours(-1);
        bin.Items.Add(new RecycledItem(@"C:\$Recycle.Bin\S-1\$R1.txt", "notas.txt", @"C:\Docs\notas.txt", false, 10, deleted));
        bin.Items.Add(new RecycledItem(@"C:\$Recycle.Bin\S-1\$R2", "Fotos", @"C:\Docs\Fotos", true, null, deleted));
        bin.Items.Add(new RecycledItem(@"C:\$Recycle.Bin\S-1\$R3.txt", "estranho.txt", @"..\fora.txt", false, 1, deleted, "O local original registrado é inválido; o item só pode ser excluído de vez."));
        var app = new AppController(new TestFileSystem(_tmp.Path), new ArchiveService(), recycleBin: bin);
        app.Start();
        var d = new Driver(app);

        // Início → Lixeira: local original e data de exclusão em cada item
        while (app.Places[app.PlacesFocus].Id != RecycleBinLocation.PlaceId) d.Press(InputAction.NavigateDown);
        d.Press(InputAction.Confirm);
        await d.Idle();
        Assert.IsType<RecycleBinLocation>(app.Browser.Location);
        var notes = app.Browser.List.Items.Single(i => i.Name == "notas.txt");
        Assert.Equal(@"C:\Docs", notes.FoundIn);
        Assert.Equal(deleted, notes.Modified);
        Assert.Null(notes.FullPath); // nunca tratado como arquivo do disco

        // Sul/A → Restaurar
        await d.FocusItem("notas.txt");
        d.Press(InputAction.Confirm);
        await d.ChooseMenu("Restaurar");
        await d.Idle();
        Assert.Equal([@"C:\Docs\notas.txt"], bin.Restored);
        Assert.DoesNotContain(app.Browser.List.Items, i => i.Name == "notas.txt");

        // Item com local original inválido: Restaurar indisponível, só excluir de vez
        await d.FocusItem("estranho.txt");
        d.Press(InputAction.Confirm);
        var menu = await d.WaitMenu();
        Assert.False(menu.Items.Single(i => i.Label == "Restaurar").IsEnabled);
        d.Press(InputAction.Back);

        // Excluir permanentemente: sempre pergunta, começando em Cancelar; Voltar não apaga nada
        await d.FocusItem("Fotos");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Excluir permanentemente");
        var dialog = await d.WaitDialog("Excluir 1 item permanentemente?");
        Assert.Equal("Cancelar", dialog.Options[dialog.FocusIndex].Label);
        d.Press(InputAction.Back);
        Assert.Contains(bin.Items, i => i.Name == "Fotos");

        d.Press(InputAction.Confirm);
        await d.ChooseMenu("Excluir permanentemente");
        d.ChooseOption(await d.WaitDialog("Excluir 1 item"), "Excluir permanentemente");
        await d.Idle();
        Assert.DoesNotContain(bin.Items, i => i.Name == "Fotos");
        Assert.Equal(["estranho.txt"], app.Browser.List.Items.Select(i => i.Name));
    });
}
