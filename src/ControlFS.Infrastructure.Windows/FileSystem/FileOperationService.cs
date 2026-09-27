using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;

namespace ControlFS.Infrastructure.Windows.FileSystem;

/// <summary>
/// Copiar, mover e excluir com planejamento, progresso, conflitos e resultado por item.
/// Cópia: temporário na pasta de destino → nome final só ao concluir, sem sobrescrever sem decisão.
/// Mover no mesmo volume: renomeação atômica; entre volumes: copia e só então remove cada origem copiada.
/// Links/junctions dentro de pastas nunca são seguidos. Lixeira nunca vira exclusão permanente silenciosa.
/// </summary>
public sealed partial class FileOperationService : IFileOperationService
{
    public const string TempPrefix = ".controlfs-copy-";
    private const int BufferSize = 81920;
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public Task<OperationResult> RunAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => RunCoreAsync(request, conflicts, progress, cancellationToken), CancellationToken.None);

    private async Task<OperationResult> RunCoreAsync(FileOperationRequest request, IConflictInteraction conflicts, IProgress<OperationProgress>? progress,
        CancellationToken ct)
    {
        var run = new Run(conflicts, progress, ct);
        string? destination = null;
        try
        {
            if (request.Kind == FileOperationKind.Delete)
            {
                run.Total = request.Sources.Count;
                foreach (var source in request.Sources)
                {
                    ct.ThrowIfCancellationRequested();
                    run.Results.Add(DeleteItem(Path.GetFullPath(source), request.Permanent));
                    run.Report(Path.GetFileName(source), 0, fileDone: true);
                }
            }
            else
            {
                destination = Path.GetFullPath(request.DestinationFolder ?? throw new ArgumentException("Destino obrigatório."));
                if (!Directory.Exists(destination))
                    return Fail(OperationErrorKind.DestinationUnavailable, "A pasta de destino não existe ou não está acessível.");
                var move = request.Kind == FileOperationKind.Move;
                // Planejamento: nada começa se houver pedido incoerente.
                foreach (var raw in request.Sources)
                {
                    var source = Path.GetFullPath(raw);
                    if (!File.Exists(source) && !Directory.Exists(source))
                        return Fail(OperationErrorKind.DestinationUnavailable, $"\"{Path.GetFileName(source)}\" não existe mais.");
                    if (Directory.Exists(source) && IsSameOrInside(destination, source))
                        return Fail(OperationErrorKind.PathRejected, $"Não é possível {(move ? "mover" : "copiar")} a pasta \"{Path.GetFileName(source)}\" para dentro dela mesma.");
                }
                (run.Total, run.BytesTotal) = Measure(request.Sources);
                foreach (var raw in request.Sources)
                {
                    if (run.Cancelled) break;
                    ct.ThrowIfCancellationRequested();
                    var source = Path.GetFullPath(Path.TrimEndingDirectorySeparator(raw));
                    var name = Path.GetFileName(source);
                    if (move && string.Equals(Path.GetDirectoryName(source), Path.TrimEndingDirectorySeparator(destination), PathComparison))
                    {
                        run.Results.Add(new ItemResult(name, ItemOutcome.Skipped, Message: "Já está nesta pasta."));
                        continue;
                    }
                    if (IsLink(source))
                    {
                        if (move && SameVolume(source, destination)) await TransferFileAsync(source, destination, name, move, run).ConfigureAwait(false);
                        else run.Results.Add(new ItemResult(name, ItemOutcome.Skipped, OperationErrorKind.LinkOrSpecialBlocked, "Link ou junction: não é seguido."));
                        continue;
                    }
                    if (Directory.Exists(source)) await TransferDirectoryAsync(source, destination, name, move, run).ConfigureAwait(false);
                    else await TransferFileAsync(source, destination, name, move, run).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            run.Cancelled = true;
        }
        catch (FatalException ex)
        {
            return new OperationResult(OperationState.Failed, run.Results, ex.Kind, ex.Message, destination);
        }

        run.Report(null, 0, fileDone: false);
        if (run.Cancelled)
            return new OperationResult(OperationState.Cancelled, run.Results, OperationErrorKind.Cancelled,
                "Operação cancelada. Itens já concluídos foram mantidos; temporários foram removidos.", destination);
        var warnings = run.Results.Any(r => r.Outcome is ItemOutcome.Failed or ItemOutcome.Blocked);
        return new OperationResult(warnings ? OperationState.CompletedWithWarnings : OperationState.Completed, run.Results, Destination: destination);
    }

    // ---------------- Arquivos ----------------

    private static async Task TransferFileAsync(string source, string destDir, string name, bool move, Run run)
    {
        var final = Path.Join(destDir, name);
        try
        {
            if (move && SameVolume(source, destDir))
            {
                var length = SafeLength(source);
                if (!Exists(final) && TryMove(source, final, overwrite: false))
                {
                    run.Results.Add(new ItemResult(name, ItemOutcome.Succeeded, FinalPath: final));
                    run.Report(name, length, fileDone: true);
                    return;
                }
                var decision = await AskAsync(final, source, name, incomingIsDirectory: false, run).ConfigureAwait(false);
                run.Results.Add(ApplyMoveDecision(source, destDir, final, name, decision, run));
                run.Report(name, length, fileDone: true);
                return;
            }

            // Cópia (ou movimentação entre volumes): temporário → nome final.
            var temp = Path.Join(destDir, TempPrefix + Guid.NewGuid().ToString("N") + ".part");
            try
            {
                await CopyContentAsync(source, temp, name, run).ConfigureAwait(false);
                var placed = await PlaceAsync(temp, final, destDir, name, source, run).ConfigureAwait(false);
                if (move && placed.Outcome is ItemOutcome.Succeeded or ItemOutcome.Renamed or ItemOutcome.Replaced)
                {
                    try
                    {
                        ClearReadOnly(source);
                        File.Delete(source);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        placed = placed with
                        {
                            Outcome = ItemOutcome.Failed,
                            Error = ex is UnauthorizedAccessException ? OperationErrorKind.AccessDenied : OperationErrorKind.Unknown,
                            Message = "Copiado para o destino, mas o original não pôde ser removido (continua na origem).",
                        };
                    }
                }
                run.Results.Add(placed);
                run.Report(name, 0, fileDone: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FatalException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var (kind, message) = Map(ex);
            if (kind is OperationErrorKind.InsufficientSpace or OperationErrorKind.DestinationUnavailable) throw new FatalException(kind, message);
            run.Results.Add(new ItemResult(name, ItemOutcome.Failed, kind, message));
            run.Report(name, 0, fileDone: true);
        }
    }

    private static async Task CopyContentAsync(string source, string temp, string name, Run run)
    {
        var buffer = new byte[BufferSize];
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
        {
            int read;
            while ((read = await input.ReadAsync(buffer, run.Ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), run.Ct).ConfigureAwait(false);
                run.Report(name, read, fileDone: false);
            }
            await output.FlushAsync(run.Ct).ConfigureAwait(false);
        }
        File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(source));
        File.SetCreationTimeUtc(temp, File.GetCreationTimeUtc(source));
    }

    private static async Task<ItemResult> PlaceAsync(string temp, string final, string destDir, string name, string source, Run run)
    {
        if (!Exists(final) && TryMove(temp, final, overwrite: false)) return new ItemResult(name, ItemOutcome.Succeeded, FinalPath: final);
        var decision = await AskAsync(final, source, name, incomingIsDirectory: false, run).ConfigureAwait(false);
        switch (decision.Choice)
        {
            case ConflictChoice.Skip:
                return new ItemResult(name, ItemOutcome.Skipped, Message: "Existente preservado.", FinalPath: final);
            case ConflictChoice.KeepBoth:
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var alternative = Path.Join(destDir, UniqueNames.Next(name, n => Exists(Path.Join(destDir, n))));
                    if (TryMove(temp, alternative, overwrite: false))
                        return new ItemResult(name, ItemOutcome.Renamed, Message: $"Salvo como \"{Path.GetFileName(alternative)}\".", FinalPath: alternative);
                }
                return new ItemResult(name, ItemOutcome.Failed, OperationErrorKind.AlreadyExists, "Não foi possível obter um nome livre.");
            case ConflictChoice.Replace:
                if (Directory.Exists(final))
                    return new ItemResult(name, ItemOutcome.Failed, OperationErrorKind.AlreadyExists, "Existe uma pasta com esse nome; pastas não são substituídas por arquivos.");
                if (IsLink(final))
                    return new ItemResult(name, ItemOutcome.Blocked, OperationErrorKind.DestinationTraversesLink, "O item existente é um link; não será substituído.");
                ClearReadOnly(final);
                File.Move(temp, final, overwrite: true);
                return new ItemResult(name, ItemOutcome.Replaced, FinalPath: final);
            default:
                run.Cancelled = true;
                return new ItemResult(name, ItemOutcome.Skipped, OperationErrorKind.Cancelled, "Cancelado no conflito; existente preservado.", final);
        }
    }

    private static ItemResult ApplyMoveDecision(string source, string destDir, string final, string name, ConflictDecision decision, Run run)
    {
        switch (decision.Choice)
        {
            case ConflictChoice.Skip:
                return new ItemResult(name, ItemOutcome.Skipped, Message: "Existente preservado; o original não foi movido.", FinalPath: final);
            case ConflictChoice.KeepBoth:
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var alternative = Path.Join(destDir, UniqueNames.Next(name, n => Exists(Path.Join(destDir, n))));
                    if (TryMove(source, alternative, overwrite: false))
                        return new ItemResult(name, ItemOutcome.Renamed, Message: $"Movido como \"{Path.GetFileName(alternative)}\".", FinalPath: alternative);
                }
                return new ItemResult(name, ItemOutcome.Failed, OperationErrorKind.AlreadyExists, "Não foi possível obter um nome livre.");
            case ConflictChoice.Replace:
                if (Directory.Exists(final))
                    return new ItemResult(name, ItemOutcome.Failed, OperationErrorKind.AlreadyExists, "Existe uma pasta com esse nome; pastas não são substituídas por arquivos.");
                if (IsLink(final))
                    return new ItemResult(name, ItemOutcome.Blocked, OperationErrorKind.DestinationTraversesLink, "O item existente é um link; não será substituído.");
                ClearReadOnly(final);
                File.Move(source, final, overwrite: true);
                return new ItemResult(name, ItemOutcome.Replaced, FinalPath: final);
            default:
                run.Cancelled = true;
                return new ItemResult(name, ItemOutcome.Skipped, OperationErrorKind.Cancelled, "Cancelado no conflito; nada foi alterado.", final);
        }
    }

    // ---------------- Pastas ----------------

    private static async Task TransferDirectoryAsync(string source, string destDir, string name, bool move, Run run, string? relative = null)
    {
        var label = relative is null ? name + "/" : relative + "/";
        var target = Path.Join(destDir, name);
        try
        {
            if (move && SameVolume(source, destDir) && !Exists(target))
            {
                try
                {
                    Directory.Move(source, target);
                    var (files, bytes) = Measure([target]);
                    run.Results.Add(new ItemResult(label, ItemOutcome.Succeeded, FinalPath: target));
                    run.Report(name, bytes, fileDone: false);
                    run.FilesDone += files;
                    return;
                }
                catch (IOException) when (Exists(target))
                {
                    // Surgiu um item com o mesmo nome: segue pelo fluxo de conflito abaixo.
                }
            }

            if (Exists(target))
            {
                var decision = await AskAsync(target, source, name, incomingIsDirectory: true, run).ConfigureAwait(false);
                switch (decision.Choice)
                {
                    case ConflictChoice.Skip:
                        run.Results.Add(new ItemResult(label, ItemOutcome.Skipped, Message: "Pasta existente preservada."));
                        return;
                    case ConflictChoice.KeepBoth:
                        target = Path.Join(destDir, UniqueNames.Next(name, n => Exists(Path.Join(destDir, n)), isDirectory: true));
                        Directory.CreateDirectory(target);
                        break;
                    case ConflictChoice.Replace when !Directory.Exists(target):
                        run.Results.Add(new ItemResult(label, ItemOutcome.Failed, OperationErrorKind.AlreadyExists, "Existe um arquivo com esse nome; ele não é substituído por uma pasta."));
                        return;
                    case ConflictChoice.Replace:
                        if (IsLink(target))
                        {
                            run.Results.Add(new ItemResult(label, ItemOutcome.Blocked, OperationErrorKind.DestinationTraversesLink, "A pasta de destino é um link; não será usada."));
                            return;
                        }
                        break; // mesclar: conflitos de arquivos dentro dela continuam perguntando
                    default:
                        run.Cancelled = true;
                        return;
                }
            }
            else
            {
                Directory.CreateDirectory(target);
            }
            Directory.SetLastWriteTimeUtc(target, Directory.GetLastWriteTimeUtc(source));

            foreach (var child in new DirectoryInfo(source).EnumerateFileSystemInfos().ToList())
            {
                if (run.Cancelled) return;
                run.Ct.ThrowIfCancellationRequested();
                var childLabel = (relative is null ? name : relative) + "/" + child.Name;
                if (IsLink(child.FullName))
                {
                    run.Results.Add(new ItemResult(childLabel, ItemOutcome.Skipped, OperationErrorKind.LinkOrSpecialBlocked, "Link ou junction: não é seguido."));
                    continue;
                }
                if ((child.Attributes & FileAttributes.Directory) != 0)
                    await TransferDirectoryAsync(child.FullName, target, child.Name, move, run, childLabel).ConfigureAwait(false);
                else
                    await TransferFileAsync(child.FullName, target, child.Name, move, run).ConfigureAwait(false);
            }

            if (move && !run.Cancelled)
            {
                // Só remove a pasta de origem se tudo dentro dela saiu com sucesso.
                if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
                else run.Results.Add(new ItemResult(label, ItemOutcome.Failed, OperationErrorKind.Unknown, "A pasta de origem ficou com itens que não foram movidos."));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FatalException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var (kind, message) = Map(ex);
            if (kind is OperationErrorKind.InsufficientSpace or OperationErrorKind.DestinationUnavailable) throw new FatalException(kind, message);
            run.Results.Add(new ItemResult(label, ItemOutcome.Failed, kind, message));
        }
    }

    // ---------------- Conflitos ----------------

    private static async Task<ConflictDecision> AskAsync(string existing, string source, string name, bool incomingIsDirectory, Run run)
    {
        var existingFile = new FileInfo(existing);
        var existingDir = new DirectoryInfo(existing);
        var incoming = new FileInfo(source);
        var conflict = new ConflictInfo(
            existing,
            existingFile.Exists ? existingFile.Length : null,
            existingFile.Exists ? existingFile.LastWriteTime : existingDir.Exists ? existingDir.LastWriteTime : null,
            existingDir.Exists,
            name,
            incomingIsDirectory || !incoming.Exists ? null : incoming.Length,
            incomingIsDirectory ? Directory.GetLastWriteTime(source) : incoming.Exists ? incoming.LastWriteTime : null,
            incomingIsDirectory);
        if (run.ApplyToAll is { } all && (all.FolderMerge == conflict.IsFolderMerge)) return all.Decision;
        var decision = await run.Conflicts.ResolveConflictAsync(conflict, run.Ct).ConfigureAwait(false);
        if (decision.ApplyToRemaining && decision.Choice != ConflictChoice.Cancel) run.ApplyToAll = (decision, conflict.IsFolderMerge);
        return decision;
    }

    // ---------------- Excluir ----------------

    private ItemResult DeleteItem(string path, bool permanent)
    {
        var name = Path.GetFileName(path);
        if (!Exists(path)) return new ItemResult(name, ItemOutcome.Skipped, Message: "O item já não existe.");
        try
        {
            if (permanent)
            {
                DeletePermanently(path);
                return new ItemResult(name, ItemOutcome.Succeeded, Message: "Excluído permanentemente.");
            }
            if (!OperatingSystem.IsWindows()) return new ItemResult(name, ItemOutcome.Failed, OperationErrorKind.Unknown, "A Lixeira só existe no Windows.");
            if (!CanRecycle(path))
                return new ItemResult(name, ItemOutcome.Failed, OperationErrorKind.Unknown, "Este local não tem Lixeira; nada foi excluído.");
            var aborted = Recycle(path);
            if (aborted) return new ItemResult(name, ItemOutcome.Skipped, Message: "Cancelado no aviso do Windows; nada foi excluído.");
            return Exists(path)
                ? new ItemResult(name, ItemOutcome.Failed, OperationErrorKind.Unknown, "O Windows não moveu o item para a Lixeira.")
                : new ItemResult(name, ItemOutcome.Succeeded, Message: "Movido para a Lixeira.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var (kind, message) = Map(ex);
            return new ItemResult(name, ItemOutcome.Failed, kind, message);
        }
    }

    /// <summary>Exclusão permanente sem seguir links: um link/junction é removido, nunca o que ele aponta.</summary>
    private static void DeletePermanently(string path)
    {
        if (IsLink(path))
        {
            if (Directory.Exists(path)) Directory.Delete(path);
            else File.Delete(path);
            return;
        }
        if (Directory.Exists(path))
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path).ToList()) DeletePermanently(child);
            ClearReadOnly(path);
            Directory.Delete(path);
            return;
        }
        ClearReadOnly(path);
        File.Delete(path);
    }

    public bool CanRecycle(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var info = new ShQueryRbInfo { Size = Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBinW(root, ref info) >= 0;
    }

    /// <summary>
    /// Envia para a Lixeira. FOF_WANTNUKEWARNING faz o Windows avisar (e não apagar em silêncio) se o item não puder
    /// ir para a Lixeira (ex.: grande demais). Retorna verdadeiro se o usuário cancelou esse aviso.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool Recycle(string path)
    {
        const uint FoDelete = 3;
        const ushort Flags = 0x0004 /* SILENT */ | 0x0010 /* NOCONFIRMATION */ | 0x0040 /* ALLOWUNDO */ | 0x0400 /* NOERRORUI */ | 0x4000 /* WANTNUKEWARNING */;
        var from = Marshal.StringToHGlobalUni(path + "\0"); // lista terminada por nulo duplo
        try
        {
            var op = new ShFileOpStruct { Func = FoDelete, From = from, Flags = Flags };
            var rc = SHFileOperationW(ref op);
            if (op.AnyOperationsAborted != 0) return true;
            if (rc != 0) throw new IOException($"O Windows recusou a exclusão (código {rc}).");
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    // ---------------- Renomear ----------------

    public FileEntry Rename(string path, string newName)
    {
        var validation = WindowsNameRules.ValidateComponent(newName);
        if (!validation.IsValid) throw new FileOperationException(OperationErrorKind.InvalidName, validation.Message);
        var full = Path.GetFullPath(Path.TrimEndingDirectorySeparator(path));
        var parent = Path.GetDirectoryName(full) ?? throw new FileOperationException(OperationErrorKind.PathRejected, "Não é possível renomear a raiz de uma unidade.");
        if (!Exists(full)) throw new FileOperationException(OperationErrorKind.DestinationUnavailable, "O item não existe mais.");
        var target = Path.Join(parent, newName);
        if (string.Equals(target, full, StringComparison.Ordinal)) return ToEntry(target);
        var caseOnly = string.Equals(target, full, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && Exists(target)) throw new FileOperationException(OperationErrorKind.AlreadyExists, $"Já existe um item chamado \"{newName}\" nesta pasta.");
        try
        {
            var isDir = Directory.Exists(full);
            if (caseOnly)
            {
                // Mudança só de maiúsculas/minúsculas: passa por um nome temporário.
                var temp = Path.Join(parent, ".controlfs-rename-" + Guid.NewGuid().ToString("N"));
                MoveAny(full, temp, isDir);
                MoveAny(temp, target, isDir);
            }
            else
            {
                MoveAny(full, target, isDir);
            }
            return ToEntry(target);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new FileOperationException(OperationErrorKind.AccessDenied, "Permissão negada para renomear este item.", ex);
        }
        catch (IOException ex) when (Exists(target) && !caseOnly)
        {
            throw new FileOperationException(OperationErrorKind.AlreadyExists, $"Já existe um item chamado \"{newName}\" nesta pasta.", ex);
        }
        catch (IOException ex)
        {
            var (kind, message) = Map(ex);
            throw new FileOperationException(kind, message, ex);
        }
    }

    private static void MoveAny(string from, string to, bool isDir)
    {
        if (isDir) Directory.Move(from, to);
        else File.Move(from, to, overwrite: false);
    }

    private static FileEntry ToEntry(string path)
    {
        var dir = Directory.Exists(path);
        var info = dir ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return new FileEntry(info.Name, info.Name, dir ? EntryKind.Directory : EntryKind.File,
            Size: dir ? null : ((FileInfo)info).Length, Modified: info.LastWriteTime, FullPath: info.FullName);
    }

    // ---------------- Utilitários ----------------

    private static (int Files, long Bytes) Measure(IEnumerable<string> sources)
    {
        var files = 0;
        long bytes = 0;
        var stack = new Stack<string>(sources.Select(Path.GetFullPath));
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            try
            {
                if (IsLink(current)) continue;
                if (Directory.Exists(current))
                {
                    foreach (var child in Directory.EnumerateFileSystemEntries(current)) stack.Push(child);
                }
                else if (File.Exists(current))
                {
                    files++;
                    bytes += new FileInfo(current).Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Itens inacessíveis aparecem depois como falha por item.
            }
        }
        return (files, bytes);
    }

    private static bool IsSameOrInside(string candidate, string folder)
    {
        var c = Path.TrimEndingDirectorySeparator(candidate);
        var f = Path.TrimEndingDirectorySeparator(folder);
        return string.Equals(c, f, PathComparison) || c.StartsWith(f + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private static bool IsLink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null) return true;
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryMove(string from, string to, bool overwrite)
    {
        try
        {
            File.Move(from, to, overwrite);
            return true;
        }
        catch (IOException) when (Exists(to))
        {
            return false;
        }
    }

    private static (OperationErrorKind Kind, string Message) Map(Exception ex) => ex switch
    {
        FileOperationException f => (f.Kind, f.Message),
        UnauthorizedAccessException => (OperationErrorKind.AccessDenied, "Permissão negada."),
        DirectoryNotFoundException or DriveNotFoundException => (OperationErrorKind.DestinationUnavailable, "Destino indisponível (pasta ou unidade removida?)."),
        IOException io when io.HResult is ErrorDiskFull or ErrorHandleDiskFull or 28 => (OperationErrorKind.InsufficientSpace, "Espaço insuficiente no destino."),
        IOException io when io.HResult is ErrorSharingViolation or ErrorLockViolation => (OperationErrorKind.Unknown, "O arquivo está em uso por outro programa."),
        PathTooLongException => (OperationErrorKind.InvalidName, "Caminho longo demais para o destino."),
        _ => (OperationErrorKind.Unknown, $"Erro de E/S ({ex.GetType().Name})."),
    };

    private static OperationResult Fail(OperationErrorKind kind, string message) => new(OperationState.Failed, [], kind, message);

    private sealed class FatalException(OperationErrorKind kind, string message) : Exception(message)
    {
        public OperationErrorKind Kind { get; } = kind;
    }

    private sealed class Run(IConflictInteraction conflicts, IProgress<OperationProgress>? progress, CancellationToken ct)
    {
        public IConflictInteraction Conflicts { get; } = conflicts;
        public CancellationToken Ct { get; } = ct;
        public List<ItemResult> Results { get; } = [];
        public (ConflictDecision Decision, bool FolderMerge)? ApplyToAll { get; set; }
        public bool Cancelled { get; set; }
        public int Total { get; set; }
        public long BytesTotal { get; set; }
        public int FilesDone { get; set; }
        private long _bytes;

        public void Report(string? current, long bytes, bool fileDone)
        {
            _bytes += bytes;
            if (fileDone) FilesDone++;
            progress?.Report(new OperationProgress(current, FilesDone, Total, _bytes, BytesTotal > 0 ? BytesTotal : null));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int Size;
        public long TotalBytes;
        public long NumItems;
    }

    [LibraryImport("shell32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial int SHFileOperationW(ref ShFileOpStruct op);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial int SHQueryRecycleBinW(string rootPath, ref ShQueryRbInfo info);
}
