using System.Buffers.Binary;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;
using ControlFS.Infrastructure.Windows.Shell;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

public sealed class RecycleBinIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-bin-" + Guid.NewGuid().ToString("N"));

    public RecycleBinIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class NoConflicts : IConflictInteraction
    {
        public Task<ConflictDecision> ResolveConflictAsync(ConflictInfo conflict, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private static async Task SendToBin(string path)
    {
        var result = await new FileOperationService().RunAsync(new FileOperationRequest { Kind = FileOperationKind.Delete, Sources = [path] }, new NoConflicts(), null, CancellationToken.None);
        Assert.Equal(ItemOutcome.Succeeded, Assert.Single(result.Items).Outcome);
    }

    [Fact]
    public async Task A_temp_file_sent_to_the_real_bin_is_listed_restored_to_its_folder_and_can_be_deleted_for_good()
    {
        var folder = Directory.CreateDirectory(Path.Join(_root, "pasta original")).FullName;
        var file = Path.Join(folder, "lixo de teste.txt");
        File.WriteAllText(file, "conteúdo");
        if (!new FileOperationService().CanRecycle(file)) Assert.Skip("Este runner não tem Lixeira na unidade do TEMP.");
        var bin = new WindowsRecycleBin();

        // A pasta temporária tem um nome único: o item é achado pelo final do caminho (o TEMP pode vir em nome curto 8.3).
        var tail = Path.Join(Path.GetFileName(_root), "pasta original", "lixo de teste.txt");
        bool Mine(RecycledItem i) => i.OriginalPath.EndsWith(tail, StringComparison.OrdinalIgnoreCase);
        await SendToBin(file);
        var item = Assert.Single(bin.List(CancellationToken.None), Mine);
        Assert.Equal("lixo de teste.txt", item.Name);
        Assert.Null(item.Problem);
        Assert.InRange(item.DeletedAt!.Value, DateTimeOffset.Now.AddMinutes(-5), DateTimeOffset.Now.AddMinutes(1));

        // A pasta original também sumiu: restaurar a recria.
        Directory.Delete(folder);
        Assert.Equal(item.OriginalPath, bin.Restore(item.Id));
        Assert.Equal("conteúdo", File.ReadAllText(file));
        Assert.DoesNotContain(bin.List(CancellationToken.None), i => i.Id == item.Id);

        await SendToBin(file);
        var again = Assert.Single(bin.List(CancellationToken.None), Mine);
        bin.DeletePermanently(again.Id);
        Assert.DoesNotContain(bin.List(CancellationToken.None), Mine);
        Assert.False(File.Exists(again.Id));
        Assert.False(File.Exists(file));
    }

    /// <summary>Par $I/$R numa Lixeira falsa (pasta temporária), no formato do Windows 10+.</summary>
    private static string Plant(string binFolder, string suffix, string originalPath, string content)
    {
        var name = Encoding.Unicode.GetBytes(originalPath + "\0");
        var info = new byte[28 + name.Length];
        BinaryPrimitives.WriteInt64LittleEndian(info, 2);
        BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(8), content.Length);
        BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(16), DateTime.Now.ToFileTime());
        BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(24), name.Length / 2);
        name.CopyTo(info.AsSpan(28));
        File.WriteAllBytes(Path.Join(binFolder, "$I" + suffix), info);
        var r = Path.Join(binFolder, "$R" + suffix);
        File.WriteAllText(r, content);
        return r;
    }

    [Fact]
    public void Restore_only_goes_to_a_valid_original_path_on_the_same_drive_and_never_overwrites()
    {
        var binFolder = Directory.CreateDirectory(Path.Join(_root, "$Recycle.Bin", "S-1-5-21-teste")).FullName;
        var bin = new WindowsRecycleBin([binFolder]);
        var target = Path.Join(_root, "restaurado", "ok.txt");
        var otherDrive = (Path.GetPathRoot(_root)!.StartsWith('Z') ? "Y" : "Z") + @":\fora\x.txt";
        var ok = Plant(binFolder, "AAAAAA.txt", target, "ok");
        var relative = Plant(binFolder, "BBBBBB.txt", @"..\..\fora.txt", "r");
        var dotted = Plant(binFolder, "CCCCCC.txt", Path.Join(_root, "a", "..", "..", "fora.txt"), "d");
        var foreign = Plant(binFolder, "DDDDDD.txt", otherDrive, "f");
        var exists = Plant(binFolder, "EEEEEE.txt", Path.Join(_root, "ja-existe.txt"), "novo");
        File.WriteAllText(Path.Join(_root, "ja-existe.txt"), "original");

        var listed = bin.List(CancellationToken.None).ToDictionary(i => i.Id);
        Assert.Equal(5, listed.Count);
        Assert.Null(listed[ok].Problem);
        foreach (var bad in new[] { relative, dotted, foreign })
        {
            Assert.NotNull(listed[bad].Problem);
            var ex = Assert.Throws<FileOperationException>(() => bin.Restore(bad));
            Assert.Equal(OperationErrorKind.PathRejected, ex.Kind);
            Assert.True(File.Exists(bad)); // nada foi movido
        }
        Assert.False(File.Exists(Path.Join(Path.GetDirectoryName(_root), "fora.txt")));

        var conflict = Assert.Throws<FileOperationException>(() => bin.Restore(exists));
        Assert.Equal(OperationErrorKind.AlreadyExists, conflict.Kind);
        Assert.Equal("original", File.ReadAllText(Path.Join(_root, "ja-existe.txt")));
        Assert.True(File.Exists(exists));

        // Um id fora da Lixeira nunca é aceito (nem para excluir).
        var outside = Path.Join(_root, "$Rsolto.txt");
        File.WriteAllText(outside, "x");
        Assert.Equal(OperationErrorKind.PathRejected, Assert.Throws<FileOperationException>(() => bin.DeletePermanently(outside)).Kind);
        Assert.True(File.Exists(outside));

        Assert.Equal(target, bin.Restore(ok));
        Assert.Equal("ok", File.ReadAllText(target));
        Assert.False(File.Exists(Path.Join(binFolder, "$IAAAAAA.txt")));
    }
}
