using ControlFS.Application;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.UnitTests.Support;

namespace ControlFS.UnitTests.Application;

public class DriveJourneyTests : IDisposable
{
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void A_drive_plugged_in_while_home_is_open_appears_and_the_focus_stays_on_the_same_place() => UiContext.Run(() =>
    {
        var fs = new TestFileSystem(_tmp.Path);
        fs.Drives.Add(new FileEntry("drive:D:\\", "Dados (D:)", EntryKind.Drive, FullPath: _tmp.MakeDir("D"), Drive: DriveKind.Fixed));
        var app = new AppController(fs, new ArchiveService());
        app.Start();
        var d = new Driver(app);
        d.Press(InputAction.NavigateDown);
        Assert.Equal("Dados (D:)", app.Places[app.PlacesFocus].Name);

        // Pendrive conectado antes da unidade focada na ordem de letras: o foco não pula para ele.
        fs.Drives.Insert(0, new FileEntry("drive:B:\\", "PENDRIVE (B:)", EntryKind.Drive, FullPath: _tmp.MakeDir("B"), Drive: DriveKind.Removable));
        app.RefreshDrives();
        Assert.Contains(app.Places, p => p is { Name: "PENDRIVE (B:)", Drive: DriveKind.Removable });
        Assert.Equal("Dados (D:)", app.Places[app.PlacesFocus].Name);

        // Unidade focada removida: o foco fica num local válido.
        fs.Drives.RemoveAt(1);
        app.RefreshDrives();
        Assert.InRange(app.PlacesFocus, 0, app.Places.Count - 1);
        Assert.DoesNotContain(app.Places, p => p.Name == "Dados (D:)");
        return Task.CompletedTask;
    });
}
