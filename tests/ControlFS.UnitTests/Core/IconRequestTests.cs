using ControlFS.Core.Contracts;
using ControlFS.Core.Icons;
using ControlFS.Core.Models;

namespace ControlFS.UnitTests.Core;

public class IconRequestTests
{
    [Fact]
    public void Common_types_and_archive_entries_are_keyed_by_extension_and_never_touch_the_disk()
    {
        var special = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Users\ana\Downloads" };

        var txt = IconRequest.For(new FileEntry("a", "Nota.TXT", EntryKind.File, FullPath: @"C:\x\Nota.TXT"))!;
        Assert.Equal((IconSourceKind.Extension, "ext:.txt"), (txt.Kind, txt.Key));
        Assert.Equal(txt.Key, IconRequest.For(new FileEntry("b", "outra.txt", EntryKind.File, FullPath: @"D:\y\outra.txt"))!.Key);

        // Dentro de um compactado, até um .exe usa só a extensão (o arquivo não existe no disco).
        var inArchive = IconRequest.For(new FileEntry("arc:setup.exe", "setup.exe", EntryKind.ArchiveFile))!;
        Assert.Equal(IconSourceKind.Extension, inArchive.Kind);
        Assert.Equal(IconSourceKind.Folder, IconRequest.For(new FileEntry("arc:dir/", "dir", EntryKind.ArchiveDirectory))!.Kind);

        // Programas, unidades e pastas especiais têm ícone próprio: pedido pelo caminho.
        Assert.Equal(IconSourceKind.Path, IconRequest.For(new FileEntry("s", "setup.exe", EntryKind.File, FullPath: @"C:\x\setup.exe"))!.Kind);
        Assert.Equal(IconSourceKind.Path, IconRequest.For(new FileEntry("drive:C:\\", "C:", EntryKind.Drive, FullPath: @"C:\"))!.Kind);
        Assert.Equal(IconSourceKind.Path, IconRequest.For(new FileEntry("d", "Downloads", EntryKind.Directory, FullPath: @"c:\users\ana\downloads"), special)!.Kind);
        Assert.Equal("folder", IconRequest.For(new FileEntry("p", "Projetos", EntryKind.Directory, FullPath: @"C:\Users\ana\Projetos"), special)!.Key);

        Assert.Null(IconRequest.For(new FileEntry("x", "con", EntryKind.ArchiveFile, BlockedReason: "nome reservado")));
    }

    [Fact]
    public void Icon_cache_is_bounded_and_evicts_the_least_recently_used()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGet("a", out _)); // "a" passa a ser o mais recente
        cache.Set("c", 3);
        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out var a));
        Assert.Equal(1, a);
    }
}
