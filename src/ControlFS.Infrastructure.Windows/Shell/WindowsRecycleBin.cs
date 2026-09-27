using System.Buffers.Binary;
using System.Text;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Windows.FileSystem;

namespace ControlFS.Infrastructure.Windows.Shell;

/// <summary>
/// Lixeira do Windows lida direto de <c>X:\$Recycle.Bin\&lt;SID&gt;</c>: cada item tem um par <c>$I…</c> (metadados:
/// tamanho, data e caminho original) e <c>$R…</c> (o conteúdo). Restaurar é renomear o <c>$R</c> de volta (mesma
/// unidade) e apagar o <c>$I</c>; excluir de vez apaga os dois, sem seguir links. O caminho original vem do disco e é
/// tratado como não confiável: precisa ser absoluto, normalizado, na mesma unidade da Lixeira e com nomes válidos.
/// Nunca sobrescreve: se já existe algo no local original, nada é movido.
/// </summary>
public sealed class WindowsRecycleBin : IRecycleBin
{
    private readonly Func<IReadOnlyList<string>> _folders;

    /// <summary>Lixeiras do usuário atual em todas as unidades prontas.</summary>
    public WindowsRecycleBin() => _folders = CurrentUserFolders;

    /// <summary>Pastas de Lixeira explícitas (testes usam pastas temporárias com a mesma estrutura).</summary>
    public WindowsRecycleBin(IReadOnlyList<string> folders) => _folders = () => folders;

    public IReadOnlyList<RecycledItem> List(CancellationToken cancellationToken)
    {
        var items = new List<RecycledItem>();
        foreach (var folder in _folders())
        {
            IEnumerable<string> infos;
            try { infos = Directory.EnumerateFiles(folder, "$I*").ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var info in infos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var content = ContentPath(info);
                bool isDirectory;
                try
                {
                    isDirectory = Directory.Exists(content);
                    if (!isDirectory && !File.Exists(content)) continue; // metadado órfão: o Windows também não mostra
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if (Read(info) is not { } meta) continue;
                var problem = ValidateOriginal(meta.OriginalPath, folder);
                var name = Path.GetFileName(meta.OriginalPath) is { Length: > 0 } n ? n : Path.GetFileName(content);
                items.Add(new RecycledItem(content, name, meta.OriginalPath, isDirectory, isDirectory ? null : meta.Size, meta.DeletedAt, problem));
            }
        }
        return items;
    }

    public string Restore(string id)
    {
        var (folder, info) = Resolve(id);
        var meta = Read(info) ?? throw new FileOperationException(OperationErrorKind.Corrupt, "Os dados deste item na Lixeira estão danificados.");
        if (ValidateOriginal(meta.OriginalPath, folder) is { } problem) throw new FileOperationException(OperationErrorKind.PathRejected, problem);
        var target = meta.OriginalPath;
        if (File.Exists(target) || Directory.Exists(target) || new FileInfo(target).LinkTarget is not null)
            throw new FileOperationException(OperationErrorKind.AlreadyExists, "Já existe um item com esse nome no local original; nada foi sobrescrito.");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); // a pasta original pode ter sido excluída depois
            // Mesma unidade: é um renomear. Move nunca sobrescreve; se algo surgir no destino agora, falha.
            if (Directory.Exists(id)) Directory.Move(id, target);
            else File.Move(id, target, overwrite: false);
        }
        catch (IOException) when (File.Exists(target) || Directory.Exists(target))
        {
            throw new FileOperationException(OperationErrorKind.AlreadyExists, "Já existe um item com esse nome no local original; nada foi sobrescrito.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var (kind, message) = FileOperationService.Map(ex);
            throw new FileOperationException(kind, message, ex);
        }
        TryDelete(info);
        return target;
    }

    public void DeletePermanently(string id)
    {
        var (_, info) = Resolve(id);
        try
        {
            if (File.Exists(id) || Directory.Exists(id)) FileOperationService.DeletePermanently(id);
            File.Delete(info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var (kind, message) = FileOperationService.Map(ex);
            throw new FileOperationException(kind, message, ex);
        }
    }

    /// <summary>O id precisa ser um <c>$R…</c> diretamente dentro de uma das Lixeiras conhecidas, com o <c>$I</c> ao lado.</summary>
    private (string Folder, string Info) Resolve(string id)
    {
        var full = Path.GetFullPath(id);
        var parent = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        var folder = _folders().FirstOrDefault(f => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)), parent, StringComparison.OrdinalIgnoreCase));
        if (folder is null || !string.Equals(full, id, StringComparison.OrdinalIgnoreCase) || !name.StartsWith("$R", StringComparison.OrdinalIgnoreCase) || name.Length < 3)
            throw new FileOperationException(OperationErrorKind.PathRejected, "Este item não está na Lixeira.");
        var info = Path.Join(parent, "$I" + name[2..]);
        if (!File.Exists(info)) throw new FileOperationException(OperationErrorKind.DestinationUnavailable, "O item não está mais na Lixeira.");
        return (folder, info);
    }

    private static string ContentPath(string info) => Path.Join(Path.GetDirectoryName(info), "$R" + Path.GetFileName(info)[2..]);

    /// <summary>
    /// Motivo para recusar o caminho original, ou <c>null</c> se ele é aceitável: absoluto, já normalizado (sem "..",
    /// "." ou separadores repetidos), na mesma unidade da Lixeira e com nomes que o Windows aceita.
    /// </summary>
    internal static string? ValidateOriginal(string original, string binFolder)
    {
        const string Invalid = "O local original registrado é inválido; o item só pode ser excluído de vez.";
        if (string.IsNullOrWhiteSpace(original) || !Path.IsPathFullyQualified(original) || original.StartsWith(@"\\", StringComparison.Ordinal)) return Invalid;
        string normalized;
        try { normalized = Path.GetFullPath(original); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return Invalid; }
        if (!string.Equals(normalized, original, StringComparison.Ordinal) || Path.EndsInDirectorySeparator(original)) return Invalid;
        var root = Path.GetPathRoot(normalized);
        if (!string.Equals(root, Path.GetPathRoot(Path.GetFullPath(binFolder)), StringComparison.OrdinalIgnoreCase)) return Invalid;
        var rest = normalized[root!.Length..];
        if (rest.Length == 0) return Invalid;
        foreach (var part in rest.Split(Path.DirectorySeparatorChar))
            if (!WindowsNameRules.ValidateComponent(part).IsValid) return Invalid;
        return null;
    }

    private sealed record Meta(string OriginalPath, long Size, DateTimeOffset? DeletedAt);

    /// <summary>
    /// <c>$I</c>: versão (8 bytes), tamanho (8), FILETIME da exclusão (8) e o caminho em UTF-16 — de tamanho fixo (260
    /// caracteres) na versão 1 (Windows Vista a 8.1) e prefixado pelo comprimento (4 bytes) na versão 2 (Windows 10+).
    /// </summary>
    private static Meta? Read(string info)
    {
        byte[] data;
        try
        {
            if (new FileInfo(info).Length is < 24 or > 65_600) return null;
            data = File.ReadAllBytes(info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        return Parse(data);
    }

    private static Meta? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24) return null;
        var version = BinaryPrimitives.ReadInt64LittleEndian(data);
        var size = BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
        var fileTime = BinaryPrimitives.ReadInt64LittleEndian(data[16..]);
        ReadOnlySpan<byte> name;
        switch (version)
        {
            case 1 when data.Length >= 24 + 520:
                name = data.Slice(24, 520);
                break;
            case 2 when data.Length >= 28:
                var chars = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
                if (chars <= 0 || chars > 32_768 || data.Length < 28 + (chars * 2)) return null;
                name = data.Slice(28, chars * 2);
                break;
            default:
                return null;
        }
        var path = Encoding.Unicode.GetString(name);
        var end = path.IndexOf('\0', StringComparison.Ordinal);
        if (end >= 0) path = path[..end];
        DateTimeOffset? deleted = null;
        try { if (fileTime > 0) deleted = DateTimeOffset.FromFileTime(fileTime); }
        catch (ArgumentOutOfRangeException) { }
        return new Meta(path, Math.Max(0, size), deleted);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static List<string> CurrentUserFolders()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) return [];
        var folders = new List<string>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        foreach (var drive in drives)
        {
            try
            {
                // Rede nunca tem Lixeira; ópticas não guardam nada. Só consulta unidades fixas e removíveis prontas.
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady) continue;
                var folder = Path.Join(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
                if (Directory.Exists(folder)) folders.Add(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return folders;
    }
}
