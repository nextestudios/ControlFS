using System.IO.Compression;
using System.Text;

namespace ControlFS.UnitTests.Support;

/// <summary>Gera ZIPs controlados (incluindo maliciosos) com o BCL, em diretórios temporários.</summary>
public static class ZipFixtures
{
    public const int UnixSymlinkMode = 0xA1FF; // S_IFLNK | 0777

    public sealed record Item(string Name, byte[] Data, int? ExternalAttributes = null, CompressionLevel Level = CompressionLevel.Optimal);

    public static Item Text(string name, string content) => new(name, Encoding.UTF8.GetBytes(content));

    public static Item Dir(string name) => new(name.EndsWith('/') ? name : name + "/", []);

    public static Item Symlink(string name, string target) => new(name, Encoding.UTF8.GetBytes(target), UnixSymlinkMode << 16);

    public static string Create(string path, params Item[] items)
    {
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var item in items)
        {
            var entry = zip.CreateEntry(item.Name, item.Level);
            if (item.ExternalAttributes is int attrs) entry.ExternalAttributes = attrs;
            if (item.Name.EndsWith('/')) continue;
            using var s = entry.Open();
            s.Write(item.Data);
        }
        return path;
    }

    public static string FixturePath(string relative) => Path.Join(AppContext.BaseDirectory, "Fixtures", relative);
}
