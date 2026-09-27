using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Windows.Shell;

namespace ControlFS.Infrastructure.Windows.FileSystem;

/// <summary>Sistema de arquivos local, operando sobre arquivos reais dentro das permissões do usuário.</summary>
public sealed class LocalFileSystemProvider : IFileSystemProvider
{
    public IReadOnlyList<FileEntry> GetPlaces()
    {
        var places = new List<FileEntry>();
        foreach (var (name, path) in KnownFolders.GetAll())
            places.Add(new FileEntry("place:" + path, name, EntryKind.KnownFolder, FullPath: path, Detail: path));

        foreach (var drive in SafeDrives())
        {
            var label = drive.Label;
            var name = string.IsNullOrWhiteSpace(label) ? drive.Name : $"{label} ({drive.Name.TrimEnd('\\', '/')})";
            var detail = $"{DescribeType(drive.Type)} · {FormatSize(drive.Free)} livres de {FormatSize(drive.Total)}";
            places.Add(new FileEntry("drive:" + drive.Name, name, EntryKind.Drive, FullPath: drive.Name, Detail: detail, Drive: KindOf(drive.Type)));
        }
        return places;
    }

    public Task<DirectoryListing> ListAsync(string path, bool includeHidden, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) throw new FileOperationException(OperationErrorKind.DestinationUnavailable, "A pasta não existe ou não está acessível.");
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = false,
                RecurseSubdirectories = false,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false,
            };
            var entries = new List<FileEntry>();
            var inaccessible = 0;
            try
            {
                foreach (var info in dir.EnumerateFileSystemInfos("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attrs;
                    try { attrs = info.Attributes; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { inaccessible++; continue; }
                    var hidden = IsHidden(info, attrs);
                    var system = (attrs & FileAttributes.System) != 0;
                    if (!includeHidden && (hidden || system)) continue;
                    entries.Add(ToEntry(info, attrs, info.Name, hidden, system));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new FileOperationException(OperationErrorKind.AccessDenied, "Permissão negada para listar esta pasta.", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new FileOperationException(OperationErrorKind.DestinationUnavailable, "A pasta deixou de existir.", ex);
            }
            return new DirectoryListing(dir.FullName, entries, inaccessible);
        }, cancellationToken);

    public IEnumerable<SearchResult> Search(SearchRequest request, CancellationToken cancellationToken)
    {
        // Resultados mais rasos primeiro; a descida (nunca em junções ou links) é decidida pelo TreeWalker.
        var root = Path.GetFullPath(request.RootPath);
        var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) is { Length: > 0 } name ? name : root;
        foreach (var step in TreeWalker.Walk(root, request.IncludeSubfolders, cancellationToken,
            (info, attrs) => request.IncludeHidden || !(IsHidden(info, attrs) || (attrs & FileAttributes.System) != 0)))
        {
            if (step.Info is not { } info)
            {
                yield return SearchResult.Skipped(step.Folder);
                continue;
            }
            if (!SearchQuery.Matches(info.Name, request.Query)) continue;
            var relative = Path.GetRelativePath(root, step.Folder);
            var foundIn = relative == "." ? rootName : Path.Join(rootName, relative);
            var hidden = IsHidden(info, step.Attributes);
            var system = (step.Attributes & FileAttributes.System) != 0;
            yield return SearchResult.Found(ToEntry(info, step.Attributes, info.FullName, hidden, system) with { FoundIn = foundIn });
        }
    }

    public FolderSize MeasureFolder(string path, IProgress<FolderSize>? progress, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(path);
        if (!Directory.Exists(root)) throw new FileOperationException(OperationErrorKind.DestinationUnavailable, "A pasta não existe ou não está acessível.");
        long bytes = 0, files = 0, folders = 0;
        var links = 0;
        var inaccessible = new List<string>();
        var lastReport = Environment.TickCount64;
        foreach (var step in TreeWalker.Walk(root, recurse: true, cancellationToken))
        {
            if (step.Info is not { } info)
            {
                inaccessible.Add(step.Folder);
                continue;
            }
            if (step.IsDirectory && !step.IsTraversed) links++;
            else if (step.IsDirectory) folders++;
            else
            {
                files++;
                bytes += SafeLength(info as FileInfo) ?? 0;
            }
            if (progress is not null && Environment.TickCount64 - lastReport >= 100)
            {
                lastReport = Environment.TickCount64;
                progress.Report(new FolderSize(bytes, files, folders, [.. inaccessible], links));
            }
        }
        return new FolderSize(bytes, files, folders, inaccessible, links);
    }

    private static bool IsHidden(FileSystemInfo info, FileAttributes attrs) =>
        (attrs & FileAttributes.Hidden) != 0 || (!OperatingSystem.IsWindows() && info.Name.StartsWith('.'));

    private static FileEntry ToEntry(FileSystemInfo info, FileAttributes attrs, string id, bool hidden, bool system)
    {
        var isDir = (attrs & FileAttributes.Directory) != 0;
        return new FileEntry(
            Id: id,
            Name: info.Name,
            Kind: isDir ? EntryKind.Directory : EntryKind.File,
            Size: isDir ? null : SafeLength(info as FileInfo),
            Modified: SafeTime(info),
            FullPath: info.FullName,
            IsHidden: hidden,
            IsSystem: system,
            IsReadOnly: (attrs & FileAttributes.ReadOnly) != 0,
            IsReparsePoint: (attrs & FileAttributes.ReparsePoint) != 0);
    }

    public string? GetParent(string path)
    {
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(path));
        return parent?.FullName;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public FileEntry CreateDirectory(string parentPath, string name)
    {
        var validation = WindowsNameRules.ValidateComponent(name);
        if (!validation.IsValid) throw new FileOperationException(OperationErrorKind.InvalidName, validation.Message);
        if (!Directory.Exists(parentPath)) throw new FileOperationException(OperationErrorKind.DestinationUnavailable, "A pasta de destino não existe mais.");
        var target = Path.Join(parentPath, name);
        if (File.Exists(target) || Directory.Exists(target))
            throw new FileOperationException(OperationErrorKind.AlreadyExists, $"Já existe um item chamado \"{name}\" nesta pasta.");
        try
        {
            // Cria com nome temporário e renomeia: o rename falha se o alvo surgir nesse intervalo,
            // garantindo que nunca "reaproveitamos" uma pasta existente.
            var temp = Path.Join(parentPath, ".controlfs-new-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                Directory.Move(temp, target);
            }
            catch (IOException)
            {
                Directory.Delete(temp);
                throw new FileOperationException(OperationErrorKind.AlreadyExists, $"Já existe um item chamado \"{name}\" nesta pasta.");
            }
            var info = new DirectoryInfo(target);
            return new FileEntry(info.Name, info.Name, EntryKind.Directory, Modified: info.LastWriteTime, FullPath: info.FullName);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new FileOperationException(OperationErrorKind.AccessDenied, "Permissão negada para criar pasta aqui.", ex);
        }
        catch (PathTooLongException ex)
        {
            throw new FileOperationException(OperationErrorKind.InvalidName, "Caminho longo demais para este destino.", ex);
        }
    }

    private static IEnumerable<(string Name, string Label, DriveType Type, long Free, long Total)> SafeDrives()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Fora do Windows (desenvolvimento), expõe apenas a raiz e a pasta pessoal.
            yield return ("/", "Raiz", DriveType.Fixed, 0, 0);
            yield break;
        }
        foreach (var d in DriveInfo.GetDrives())
        {
            (string, string, DriveType, long, long)? item = null;
            try
            {
                if (d.IsReady) item = (d.Name, d.VolumeLabel, d.DriveType, d.AvailableFreeSpace, d.TotalSize);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (item is { } value) yield return value;
        }
    }

    internal static DriveKind KindOf(DriveType type) => type switch
    {
        DriveType.Removable => DriveKind.Removable,
        DriveType.Network => DriveKind.Network,
        DriveType.CDRom => DriveKind.Optical,
        _ => DriveKind.Fixed,
    };

    private static string DescribeType(DriveType type) => KindOf(type) switch
    {
        DriveKind.Removable => "Removível (USB)",
        DriveKind.Network => "Rede",
        DriveKind.Optical => "Óptica",
        _ => "Local",
    };

    internal static string FormatSize(long bytes) => bytes switch
    {
        <= 0 => "—",
        >= 1L << 40 => $"{bytes / (double)(1L << 40):0.#} TB",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };

    private static long? SafeLength(FileInfo? info)
    {
        try { return info?.Length; }
        catch (IOException) { return null; }
    }

    private static DateTimeOffset? SafeTime(FileSystemInfo info)
    {
        try { return info.LastWriteTime; }
        catch (IOException) { return null; }
    }
}
