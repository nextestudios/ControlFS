using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.Shell;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>Ícones reais do Shell do Windows. Nitidez em 4K e no portátil fica na verificação manual (docs/TESTING.md).</summary>
public sealed class ShellIconIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-icon-tests", Guid.NewGuid().ToString("N"));
    private readonly ShellIconProvider _icons = new();

    public ShellIconIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _icons.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    public async Task Text_files_folders_and_drives_get_a_visible_icon_of_the_requested_scale(int size)
    {
        var file = Path.Join(_root, "nota.txt");
        File.WriteAllText(file, "x");
        var drive = Path.GetPathRoot(Environment.SystemDirectory)!;
        var requests = new[]
        {
            IconRequest.For(new FileEntry("nota.txt", "nota.txt", EntryKind.File, FullPath: file))!,
            IconRequest.For(new FileEntry("pasta", "pasta", EntryKind.Directory, FullPath: _root))!,
            IconRequest.For(new FileEntry("drive:" + drive, drive, EntryKind.Drive, FullPath: drive))!,
        };
        foreach (var request in requests)
        {
            var icon = await _icons.GetIconAsync(request, size, TestContext.Current.CancellationToken);
            Assert.NotNull(icon);
            Assert.InRange(icon.Width, 16, 256);
            Assert.Equal(icon.Width * icon.Height * 4, icon.Pixels.Length);
            // Não é uma imagem vazia: há pixels visíveis.
            var span = icon.Pixels.Span;
            var visible = 0;
            for (var i = 3; i < span.Length; i += 4) if (span[i] != 0) visible++;
            Assert.True(visible > icon.Width, $"{request.Key}: ícone sem pixels visíveis");
        }
    }
}
