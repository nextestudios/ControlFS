using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;
using ControlFS.Infrastructure.Windows.DiskImages;
using Xunit;

namespace ControlFS.WindowsIntegrationTests;

/// <summary>
/// Montar e desmontar (#73, #74) com a API nativa do Windows, numa ISO 9660 mínima gerada aqui (um arquivo na raiz):
/// a unidade nova aparece com o conteúdo, a unidade diz qual imagem está por trás e some ao desmontar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DiskImageIntegrationTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), "controlfs-win-tests", Guid.NewGuid().ToString("N"));

    public DiskImageIntegrationTests()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requer Windows.");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_generated_iso_mounts_as_a_browsable_drive_and_unmounting_removes_it()
    {
        var iso = Path.Join(_root, "tiny.iso");
        TinyIso.Write(iso, "HELLO.TXT", "ControlFS ISO");
        var service = new VirtualDiskService();
        var root = service.Mount(iso, CancellationToken.None);
        try
        {
            Assert.Matches(@"^[A-Z]:\\$", root);
            Assert.Equal("ControlFS ISO", await File.ReadAllTextAsync(Path.Join(root, "HELLO.TXT")));
            var behind = service.ImageBehind(root);
            Assert.NotNull(behind);
            Assert.Equal("tiny.iso", Path.GetFileName(behind), ignoreCase: true);
            Assert.Equal(new FileInfo(iso).Length, new FileInfo(behind).Length);
            Assert.Equal(root, service.Mount(iso, CancellationToken.None)); // já montada: a mesma unidade

            service.Unmount(root);
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (Directory.Exists(root) && DateTime.UtcNow < deadline) await Task.Delay(200);
            Assert.False(Directory.Exists(root), "a unidade some depois de desmontar");
            root = string.Empty;
        }
        finally
        {
            if (root.Length > 0) try { service.Unmount(root); } catch (Exception) { }
        }
        Assert.Null(service.ImageBehind(Path.GetPathRoot(Environment.SystemDirectory)!)); // disco comum não é imagem
    }

    /// <summary>ISO 9660 nível 1 com um arquivo na raiz: 16 setores de sistema, PVD, terminador, tabelas de caminho, raiz e dados.</summary>
    private static class TinyIso
    {
        private const int Sector = 2048;

        public static void Write(string path, string name, string content)
        {
            var data = Encoding.UTF8.GetBytes(content);
            const int pvd = 16, terminator = 17, pathL = 18, pathM = 19, rootDir = 20, fileData = 21, total = 22;
            var image = new byte[total * Sector];

            var p = image.AsSpan(pvd * Sector, Sector);
            p[0] = 1;
            Ascii(p[1..6], "CD001");
            p[6] = 1;
            Ascii(p[8..40], string.Empty);
            Ascii(p[40..72], "CONTROLFS");
            Both32(p[80..88], total);
            Both16(p[120..124], 1);
            Both16(p[124..128], 1);
            Both16(p[128..132], Sector);
            Both32(p[132..140], 10);
            BinaryPrimitives.WriteInt32LittleEndian(p[140..144], pathL);
            BinaryPrimitives.WriteInt32BigEndian(p[148..152], pathM);
            DirectoryRecord(p[156..190], rootDir, Sector, directory: true, [0]);
            Ascii(p[190..813], string.Empty); // conjunto, editor, preparador, aplicativo e arquivos de copyright/resumo/bibliografia
            for (var at = 813; at < 881; at += 17) Ascii(p[at..(at + 16)], "0000000000000000"); // datas não informadas
            p[881] = 1;

            var t = image.AsSpan(terminator * Sector, Sector);
            t[0] = 255;
            Ascii(t[1..6], "CD001");
            t[6] = 1;

            PathTable(image.AsSpan(pathL * Sector, 10), rootDir, bigEndian: false);
            PathTable(image.AsSpan(pathM * Sector, 10), rootDir, bigEndian: true);

            var r = image.AsSpan(rootDir * Sector, Sector);
            DirectoryRecord(r[..34], rootDir, Sector, directory: true, [0]);
            DirectoryRecord(r[34..68], rootDir, Sector, directory: true, [1]);
            var fileName = Encoding.ASCII.GetBytes(name + ";1");
            var length = 33 + fileName.Length + (fileName.Length % 2 == 0 ? 1 : 0);
            DirectoryRecord(r[68..(68 + length)], fileData, data.Length, directory: false, fileName);

            data.CopyTo(image.AsSpan(fileData * Sector));
            File.WriteAllBytes(path, image);
        }

        private static void DirectoryRecord(Span<byte> record, int extent, int size, bool directory, byte[] name)
        {
            record[0] = (byte)record.Length;
            Both32(record[2..10], extent);
            Both32(record[10..18], size);
            record[18] = 126; // 2026
            record[19] = 1;
            record[20] = 1;
            record[25] = directory ? (byte)2 : (byte)0;
            Both16(record[28..32], 1);
            record[32] = (byte)name.Length;
            name.CopyTo(record[33..]);
        }

        private static void PathTable(Span<byte> table, int extent, bool bigEndian)
        {
            table[0] = 1;
            if (bigEndian)
            {
                BinaryPrimitives.WriteInt32BigEndian(table[2..6], extent);
                BinaryPrimitives.WriteInt16BigEndian(table[6..8], 1);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(table[2..6], extent);
                BinaryPrimitives.WriteInt16LittleEndian(table[6..8], 1);
            }
        }

        private static void Both32(Span<byte> target, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(target[..4], value);
            BinaryPrimitives.WriteInt32BigEndian(target[4..8], value);
        }

        private static void Both16(Span<byte> target, int value)
        {
            BinaryPrimitives.WriteInt16LittleEndian(target[..2], (short)value);
            BinaryPrimitives.WriteInt16BigEndian(target[2..4], (short)value);
        }

        private static void Ascii(Span<byte> target, string text)
        {
            target.Fill((byte)' ');
            Encoding.ASCII.GetBytes(text).CopyTo(target);
        }
    }
}
