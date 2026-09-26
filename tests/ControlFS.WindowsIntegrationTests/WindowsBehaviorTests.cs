using System.Diagnostics;
using System.IO.Compression;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.Infrastructure.Windows.Shell;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

public sealed class WindowsBehaviorTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-win-tests", Guid.NewGuid().ToString("N"));

    public WindowsBehaviorTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class NoConflicts : IExtractionInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Conflito inesperado");
    }

    private string Zip(string name, params (string Entry, string Content)[] items)
    {
        var path = Path.Join(_root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in items)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open());
            w.Write(content);
        }
        return path;
    }

    [Fact]
    public void Downloads_comes_from_known_folder_api()
    {
        var downloads = KnownFolders.GetAll().FirstOrDefault(f => f.Name == "Downloads");
        Assert.False(string.IsNullOrEmpty(downloads.Path));
        Assert.True(Directory.Exists(downloads.Path));
    }

    [Fact]
    public void Drives_are_listed_as_places()
    {
        var places = new LocalFileSystemProvider().GetPlaces();
        Assert.Contains(places, p => p.Kind == EntryKind.Drive);
    }

    [Fact]
    public async Task Junction_inside_destination_is_not_followed()
    {
        var outside = Directory.CreateDirectory(Path.Join(_root, "outside")).FullName;
        var dest = Directory.CreateDirectory(Path.Join(_root, "dest")).FullName;
        // Junctions não exigem privilégio de administrador.
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", Path.Join(dest, "j"), outside }, UseShellExecute = false, CreateNoWindow = true })!;
        await mklink.WaitForExitAsync();
        Assert.Equal(0, mklink.ExitCode);
        var zip = Zip("j.zip", ("j/evil.txt", "x"));

        var result = await new ArchiveService().ExtractAsync(new ExtractionRequest
        {
            ArchivePath = zip,
            DestinationDirectory = dest,
            Mode = DestinationMode.IntoExistingFolder,
        }, new NoConflicts(), null, CancellationToken.None);

        Assert.Equal(OperationErrorKind.DestinationTraversesLink, Assert.Single(result.Items).Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public async Task Mark_of_the_web_is_propagated_to_extracted_files()
    {
        var zip = Zip("web.zip", ("a.txt", "a"));
        File.WriteAllText(zip + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        var result = await new ArchiveService().ExtractAsync(new ExtractionRequest { ArchivePath = zip, DestinationDirectory = _root }, new NoConflicts(), null, CancellationToken.None);

        var extracted = Path.Join(result.Destination!, "a.txt");
        Assert.Contains("ZoneId=3", File.ReadAllText(extracted + ":Zone.Identifier"), StringComparison.Ordinal);
    }

    [Fact]
    public void Reserved_names_are_rejected_before_touching_the_disk()
    {
        var ex = Assert.Throws<FileOperationException>(() => new LocalFileSystemProvider().CreateDirectory(_root, "NUL"));
        Assert.Equal(OperationErrorKind.InvalidName, ex.Kind);
    }

    [Fact]
    public async Task Long_paths_beyond_260_characters_extract()
    {
        var deep = string.Join('/', Enumerable.Repeat("pasta-com-nome-longo-para-teste", 10)) + "/arquivo.txt";
        var zip = Zip("long.zip", (deep, "ok"));
        var result = await new ArchiveService().ExtractAsync(new ExtractionRequest { ArchivePath = zip, DestinationDirectory = _root }, new NoConflicts(), null, CancellationToken.None);
        Assert.Equal(OperationState.Completed, result.FinalState);
        Assert.True(Path.Join(result.Destination!, deep).Length > 260);
    }
}
