using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

/// <summary>Montar e desmontar imagens (#73, #74) pelo menu, com um serviço simulado (o nativo é provado no Windows).</summary>
public class DiskImageJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>"Monta" a imagem numa pasta temporária que aparece como unidade E:.</summary>
    private sealed class FakeImages(TestFileSystem fs, string driveRoot) : IDiskImageService
    {
        public string? Mounted { get; private set; }

        public string Mount(string imagePath, CancellationToken cancellationToken)
        {
            if (new FileInfo(imagePath).Length == 0) throw new FileOperationException(OperationErrorKind.UnsupportedFormat, "O Windows não reconheceu este arquivo como uma imagem de disco.");
            Mounted = imagePath;
            fs.Drives.Add(new FileEntry("drive:E:\\", "Imagem (E:)", EntryKind.Drive, FullPath: driveRoot, Drive: DriveKind.Optical));
            return driveRoot;
        }

        public string? ImageBehind(string root) => Mounted is not null && root == driveRoot ? Mounted : null;

        public void Unmount(string root)
        {
            Mounted = null;
            fs.Drives.Clear();
        }
    }

    [Fact]
    public void Mounting_opens_the_new_drive_and_unmount_asks_first_then_removes_it() => UiContext.Run(async () =>
    {
        File.WriteAllText(_tmp.Sub("jogo.iso"), "iso");
        File.WriteAllText(_tmp.Sub("vazia.iso"), string.Empty);
        var driveRoot = _tmp.MakeDir("E");
        File.WriteAllText(Path.Join(driveRoot, "setup.txt"), "x");
        var fs = new TestFileSystem(_tmp.MakeDir("pasta"));
        var images = new FakeImages(fs, driveRoot);
        var app = new AppController(fs, new ArchiveService()) { DiskImages = images };
        app.Start();
        var d = new Driver(app);
        app.OpenPhysical(_tmp.Path);

        // Erro explicado, nada muda.
        await d.FocusItem("vazia.iso");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Montar imagem");
        var error = await d.WaitDialog("Não foi possível montar");
        Assert.Contains("não reconheceu", error.Message, StringComparison.Ordinal);
        d.Press(InputAction.Back);

        await d.FocusItem("jogo.iso");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Montar imagem");
        await UiContext.WaitUntil(() => app.Browser.Location is PhysicalLocation { FullPath: var p } && p == driveRoot && !app.Browser.IsLoading, "unidade nova aberta");
        Assert.Contains(app.Browser.List.Items, i => i.Name == "setup.txt");

        await app.NavigateAsync(app.Browser, ThisPcLocation.Instance, pushHistory: true);
        await d.FocusItem("Imagem (E:)");
        d.Press(InputAction.OpenContextMenu);
        await d.ChooseMenu("Desmontar imagem…");
        var confirm = await d.WaitDialog("Desmontar a imagem?");
        Assert.Equal("Cancelar", confirm.Options[confirm.FocusIndex].Label);
        d.ChooseOption(confirm, "Desmontar");
        await UiContext.WaitUntil(() => images.Mounted is null && app.StatusMessage?.Contains("desmontada", StringComparison.Ordinal) == true, "desmontada");
        await d.Idle();
        Assert.DoesNotContain(app.Browser.List.Items, i => i.Name == "Imagem (E:)");
    });
}
