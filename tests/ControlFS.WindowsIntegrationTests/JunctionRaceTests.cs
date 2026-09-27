using System.Diagnostics;
using System.IO.Compression;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>
/// Adversarial: outro processo com os mesmos privilégios troca uma pasta do destino por uma junction para fora dele,
/// sem parar, enquanto o ControlFS extrai ou copia para dentro dela. Nenhuma gravação pode chegar ao alvo da junction.
/// </summary>
public sealed class JunctionRaceTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-race-tests", Guid.NewGuid().ToString("N"));

    public JunctionRaceTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // Remove as junctions antes da limpeza recursiva (nunca apaga o que elas apontam).
        foreach (var dir in Directory.EnumerateDirectories(_root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }).ToList())
            if (Directory.Exists(dir) && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) Directory.Delete(dir);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static async Task Junction(string link, string target)
    {
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", link, target }, UseShellExecute = false, CreateNoWindow = true })!;
        await mklink.WaitForExitAsync();
        Assert.Equal(0, mklink.ExitCode);
    }

    /// <summary>Em outra thread: "sub" ↔ junction para fora, o mais rápido possível, até ser descartado.</summary>
    private sealed class Swapper : IDisposable
    {
        private readonly string _real;
        private readonly string _parked;
        private readonly string _junction;
        private readonly Thread _thread;
        private volatile bool _stop;

        public Swapper(string real, string junction)
        {
            _real = real;
            _parked = real + "-parked";
            _junction = junction;
            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
        }

        public int Attempts { get; private set; }
        public int Swaps { get; private set; }

        private void Loop()
        {
            while (!_stop)
            {
                Attempts++;
                try { Directory.Move(_real, _parked); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                try
                {
                    Directory.Move(_junction, _real); // agora "sub" é uma junction para fora do destino
                    Swaps++;
                    Thread.Yield();
                    Directory.Move(_real, _junction);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                finally
                {
                    Restore();
                }
            }
        }

        private void Restore()
        {
            for (var i = 0; i < 1000 && Directory.Exists(_parked); i++)
            {
                try
                {
                    if (Directory.Exists(_real) && (File.GetAttributes(_real) & FileAttributes.ReparsePoint) != 0) Directory.Move(_real, _junction);
                    Directory.Move(_parked, _real);
                }
                catch (IOException) { Thread.Sleep(1); }
                catch (UnauthorizedAccessException) { Thread.Sleep(1); }
            }
        }

        public void Dispose()
        {
            _stop = true;
            _thread.Join();
        }
    }

    private sealed class MergeFolders : IConflictInteraction, IExtractionInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            Task.FromResult(conflict.IsFolderMerge ? new ConflictDecision(ConflictChoice.Replace) : new ConflictDecision(ConflictChoice.KeepBoth));
    }

    private async Task<(string Dest, string Outside)> Arena()
    {
        var outside = Directory.CreateDirectory(Path.Join(_root, "outside")).FullName;
        var dest = Directory.CreateDirectory(Path.Join(_root, "dest")).FullName;
        Directory.CreateDirectory(Path.Join(dest, "sub"));
        await Junction(Path.Join(dest, "sub-junction"), outside);
        return (dest, outside);
    }

    private static void AssertNothingEscaped(string outside, int attempts)
    {
        Assert.True(attempts > 0, "o atacante precisa ter rodado");
        // Cada item ou foi colocado dentro do destino, ou recusado com motivo; nada pode ter chegado ao alvo da junction.
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Racing_junction_swap_cannot_redirect_extraction_writes()
    {
        var (dest, outside) = await Arena();
        var zip = Path.Join(_root, "muitos.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            for (var i = 0; i < 300; i++)
            {
                using var w = new StreamWriter(archive.CreateEntry($"sub/f{i:000}.txt").Open());
                w.Write(new string('x', 2048));
            }

        OperationResult result;
        int attempts;
        using (var swapper = new Swapper(Path.Join(dest, "sub"), Path.Join(dest, "sub-junction")))
        {
            result = await new ArchiveService().ExtractAsync(new ExtractionRequest
            {
                ArchivePath = zip,
                DestinationDirectory = dest,
                Mode = DestinationMode.IntoExistingFolder,
            }, new MergeFolders(), null, CancellationToken.None);
            attempts = swapper.Attempts;
        }

        AssertNothingEscaped(outside, attempts);
        Assert.Equal(300, result.Items.Count); // cada entrada tem um resultado próprio (colocada ou recusada)
    }

    [Theory]
    [InlineData(FileOperationKind.Copy)]
    [InlineData(FileOperationKind.Move)]
    public async Task Racing_junction_swap_cannot_redirect_copy_or_move_writes(FileOperationKind kind)
    {
        var (dest, outside) = await Arena();
        var source = Directory.CreateDirectory(Path.Join(_root, "origem", "sub")).FullName;
        for (var i = 0; i < 300; i++) File.WriteAllText(Path.Join(source, $"f{i:000}.txt"), new string('x', 2048));

        OperationResult result;
        int attempts;
        using (var swapper = new Swapper(Path.Join(dest, "sub"), Path.Join(dest, "sub-junction")))
        {
            result = await new FileOperationService().RunAsync(new FileOperationRequest { Kind = kind, Sources = [source], DestinationFolder = dest },
                new MergeFolders(), null, CancellationToken.None);
            attempts = swapper.Attempts;
        }

        AssertNothingEscaped(outside, attempts);
        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public void A_pinned_folder_cannot_be_renamed_or_replaced_while_held()
    {
        var dest = Directory.CreateDirectory(Path.Join(_root, "dest")).FullName;
        var sub = Directory.CreateDirectory(Path.Join(dest, "a", "b")).FullName;

        using (var pinned = PinnedDirectory.Open(dest, ["a", "b"], create: false))
        {
            Assert.Equal(sub, pinned.FullPath, ignoreCase: true);
            Assert.ThrowsAny<IOException>(() => Directory.Move(sub, sub + "-x"));
            Assert.ThrowsAny<IOException>(() => Directory.Move(Path.Join(dest, "a"), Path.Join(dest, "a-x")));
        }

        Directory.Move(sub, sub + "-x"); // solto: volta a ser possível
    }

    [Fact]
    public async Task A_junction_in_the_chain_is_refused_by_handle()
    {
        var outside = Directory.CreateDirectory(Path.Join(_root, "outside")).FullName;
        var dest = Directory.CreateDirectory(Path.Join(_root, "dest")).FullName;
        await Junction(Path.Join(dest, "j"), outside);

        var error = Assert.Throws<FileOperationException>(() => PinnedDirectory.Open(dest, ["j"], create: true));

        Assert.Equal(OperationErrorKind.DestinationTraversesLink, error.Kind);
        var ops = await new FileOperationService().RunAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [Directory.CreateDirectory(Path.Join(_root, "origem", "j")).FullName],
            DestinationFolder = dest,
        }, new MergeFolders(), null, CancellationToken.None);
        Assert.Contains(ops.Items, i => i.Outcome == ItemOutcome.Blocked && i.Error == OperationErrorKind.DestinationTraversesLink);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }
}
