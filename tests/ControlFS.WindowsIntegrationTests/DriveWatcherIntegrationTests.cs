using System.Diagnostics;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>
/// Uma letra nova (unidade virtual do <c>subst</c>, o mais próximo de conectar um pendrive que o CI permite) é percebida
/// com o app aberto e aparece nos locais com o tipo. Pendrive e leitor óptico reais ficam na verificação manual.
/// </summary>
public sealed class DriveWatcherIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-drive-tests", Guid.NewGuid().ToString("N"));
    private string? _letter;

    public DriveWatcherIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (_letter is not null) Subst($"{_letter} /D");
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task A_new_drive_letter_is_noticed_without_restarting_and_listed_with_its_type()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        var free = "QRSTUVWXYZ".FirstOrDefault(c => !used.Contains(c));
        if (free == default) Assert.Skip("Nenhuma letra livre.");
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new DriveWatcher(TimeSpan.FromMilliseconds(200));
        watcher.Changed += () => changed.TrySetResult();

        _letter = free + ":";
        Assert.Equal(0, Subst($"{_letter} \"{_root}\""));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        var drive = Assert.Single(new LocalFileSystemProvider().GetPlaces(), p => p.Id.Equals($"drive:{_letter}\\", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(EntryKind.Drive, drive.Kind);
        Assert.Equal(DriveKind.Fixed, drive.Drive);
    }

    private static int Subst(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("subst.exe", arguments) { UseShellExecute = false, CreateNoWindow = true })!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
