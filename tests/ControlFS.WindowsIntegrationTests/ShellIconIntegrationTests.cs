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
    public async Task Text_files_folders_drives_and_the_recycle_bin_get_a_visible_icon_of_the_requested_scale(int size)
    {
        var file = Path.Join(_root, "nota.txt");
        File.WriteAllText(file, "x");
        var drive = Path.GetPathRoot(Environment.SystemDirectory)!;
        var requests = new[]
        {
            IconRequest.For(new FileEntry("nota.txt", "nota.txt", EntryKind.File, FullPath: file))!,
            IconRequest.For(new FileEntry("pasta", "pasta", EntryKind.Directory, FullPath: _root))!,
            IconRequest.For(new FileEntry("drive:" + drive, drive, EntryKind.Drive, FullPath: drive))!,
            IconRequest.For(new FileEntry(RecycleBinLocation.PlaceId, "Lixeira", EntryKind.KnownFolder))!, // pasta virtual, pelo PIDL
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

    [Fact]
    public async Task A_rar_file_gets_an_icon_even_when_windows_has_no_program_for_it()
    {
        var request = IconRequest.For(new FileEntry("a.rar", "a.rar", EntryKind.File, FullPath: Path.Join(_root, "a.rar")))!;
        var icon = await _icons.GetIconAsync(request, 48, TestContext.Current.CancellationToken);
        Assert.NotNull(icon); // o do Windows, ou o de reserva do ControlFS: nunca a página em branco nem nada
        Assert.InRange(icon.Width, 16, 256);
    }
}
