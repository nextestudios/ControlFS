using System.Diagnostics;
using System.Runtime.Versioning;
using ControlFS.Infrastructure.Windows.FileSystem;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>Tamanho de pasta sob demanda (#55) numa árvore gerada: soma como o Explorador e nunca segue junções.</summary>
[SupportedOSPlatform("windows")]
public sealed class FolderSizeIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-win-tests", Guid.NewGuid().ToString("N"));

    public FolderSizeIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Size_sums_every_file_including_hidden_ones_and_never_follows_a_junction()
    {
        var outside = Directory.CreateDirectory(Path.Join(_root, "outside")).FullName;
        File.WriteAllBytes(Path.Join(outside, "big.bin"), new byte[50_000]);
        var tree = Directory.CreateDirectory(Path.Join(_root, "tree")).FullName;
        var deep = Directory.CreateDirectory(Path.Join(tree, "a", "b", "c")).FullName;
        File.WriteAllBytes(Path.Join(tree, "root.bin"), new byte[1_000]);
        File.WriteAllBytes(Path.Join(deep, "deep.bin"), new byte[2_345]);
        var hidden = Path.Join(tree, "a", "hidden.bin");
        File.WriteAllBytes(hidden, new byte[10]);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", Path.Join(tree, "link"), outside }, UseShellExecute = false, CreateNoWindow = true })!;
        await mklink.WaitForExitAsync();
        Assert.Equal(0, mklink.ExitCode);

        var size = new LocalFileSystemProvider().MeasureFolder(tree, null, CancellationToken.None);

        Assert.Equal(3_355, size.Bytes); // o alvo da junção (50 000 bytes) fica de fora, como no Explorador
        Assert.Equal(3, size.Files);
        Assert.Equal(3, size.Folders);
        Assert.Equal(1, size.LinksNotFollowed);
        Assert.Empty(size.Inaccessible);
    }
}
