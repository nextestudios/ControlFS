using ControlFS.Infrastructure.Windows.FileSystem;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

public sealed class RenameIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-rename-" + Guid.NewGuid().ToString("N"));

    public RenameIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Case_only_renames_work_for_files_and_folders()
    {
        var service = new FileOperationService();
        File.WriteAllText(Path.Join(_root, "foto.JPG"), "x");
        Directory.CreateDirectory(Path.Join(_root, "pasta"));

        service.Rename(Path.Join(_root, "foto.JPG"), "foto.jpg");
        service.Rename(Path.Join(_root, "pasta"), "Pasta");

        var names = Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["Pasta", "foto.jpg"], names);
    }
}
