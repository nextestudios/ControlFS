using System.Globalization;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input.Mapping;
using ControlFS.Core.Policies;

namespace ControlFS.Infrastructure.Windows.Settings;

/// <summary>
/// Perfis de controle em <c>&lt;dados&gt;\controllers\*.json</c>, um arquivo por controle (nome derivado do GUID ou do
/// vendor/product, nunca do nome do dispositivo). Gravação atômica (temporário + flush + troca) e cópia <c>.bak</c> do
/// perfil anterior. Toda leitura passa pelo <see cref="ControllerProfileSerializer"/> (tamanho, esquema, faixas).
/// </summary>
public sealed class JsonControllerProfileStore(string dataDirectory) : IControllerProfileStore
{
    private const int MaxProfiles = 64;

    public string Directory { get; } = Path.Join(dataDirectory, "controllers");

    public ControllerProfilesLoadResult Load()
    {
        var profiles = new List<ControllerProfile>();
        var problems = new List<string>();
        if (!System.IO.Directory.Exists(Directory)) return new(profiles, problems);
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.Ordinal).Take(MaxProfiles))
        {
            try
            {
                profiles.Add(ReadFile(file));
            }
            catch (Exception ex) when (ex is ControllerProfileException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return new(profiles, problems);
    }

    public void Save(ControllerProfile profile)
    {
        var bytes = ControllerProfileSerializer.Serialize(profile); // valida antes de tocar no disco
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Join(Directory, FileNameFor(profile.Match));
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        WriteAtomic(path, bytes);
    }

    public ControllerProfile Import(string path) => ReadFile(path);

    public string Export(ControllerProfile profile, string directory)
    {
        var bytes = ControllerProfileSerializer.Serialize(profile);
        var name = UniqueNames.Next($"ControlFS-controle-{SafeStem(profile.Name)}.json", n => File.Exists(Path.Join(directory, n)));
        var path = Path.Join(directory, name);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        return path;
    }

    public IReadOnlyList<string> ListImportable(string directory) =>
        System.IO.Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase).Take(200).ToList();

    private static ControllerProfile ReadFile(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > ControllerProfileSerializer.MaxBytes)
            throw new ControllerProfileException($"Arquivo grande demais para um perfil (máximo {ControllerProfileSerializer.MaxBytes / 1024} KB).");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ControllerProfileSerializer.Read(stream);
    }

    private static string FileNameFor(ControllerMatch match) =>
        (match.DeviceGuid.Length > 0
            ? match.DeviceGuid.ToLowerInvariant()
            : string.Create(CultureInfo.InvariantCulture, $"{match.VendorId:x4}-{match.ProductId:x4}")) + ".json";

    /// <summary>Nome de arquivo a partir do nome do controle: só letras, dígitos, espaço, hífen e sublinhado.</summary>
    private static string SafeStem(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name)
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : c == ' ' ? '-' : '_');
        var stem = builder.ToString().Trim('-', '_');
        return stem.Length == 0 ? "joystick" : stem[..Math.Min(stem.Length, 48)];
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }
}
