using System.Formats.Tar;
using System.IO.Compression;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives.Security;

namespace ControlFS.Infrastructure.Archives.Creation;

/// <summary>
/// Cria ZIP (BCL, Deflate, nomes UTF-8), TAR.GZ (PAX + GZip) ou 7z (LZMA sólido, <see cref="SevenZipWriter"/>). Não segue links/junctions da origem (são listados como
/// ignorados), grava num temporário na pasta de destino e só o renomeia para o nome final ao concluir, sem sobrescrever.
/// Cancelamento ou falha fatal remove o temporário; nada parcial fica visível.
/// </summary>
public static class ArchiveCreator
{
    public const string TempPrefix = ".controlfs-new-";
    private const int BufferSize = 81920;

    public static Task<OperationResult> CreateAsync(CompressionRequest request, IProgress<OperationProgress>? progress, CancellationToken ct,
        ITemporaryJournal? journal = null, RarTool? rarTool = null, TimeSpan? rarStallTimeout = null) =>
        Task.Run(() => Create(request, progress, journal ?? NoTemporaryJournal.Instance, rarTool, rarStallTimeout ?? RarWriter.DefaultStallTimeout, ct), CancellationToken.None);

    private static OperationResult Create(CompressionRequest request, IProgress<OperationProgress>? progress, ITemporaryJournal journal, RarTool? rarTool,
        TimeSpan rarStallTimeout, CancellationToken ct)
    {
        var destination = Path.GetFullPath(request.DestinationPath);
        var folder = Path.GetDirectoryName(destination);
        var fileName = Path.GetFileName(destination);
        if (folder is null || !Directory.Exists(folder))
            return Fail(OperationErrorKind.DestinationUnavailable, "A pasta de destino não existe.");
        var nameCheck = WindowsNameRules.ValidateComponent(fileName);
        if (!nameCheck.IsValid) return Fail(OperationErrorKind.InvalidName, nameCheck.Message);
        if (File.Exists(destination) || Directory.Exists(destination))
            return Fail(OperationErrorKind.AlreadyExists, $"Já existe \"{fileName}\" nesta pasta.");
        if (request.SourcePaths.Count == 0) return Fail(OperationErrorKind.Unknown, "Nada para compactar.");
        if (request.Format == CompressionFormat.Rar)
        {
            if (rarTool is null) return Fail(OperationErrorKind.UnsupportedFormat, RarLocator.MissingReason);
            var bases = request.SourcePaths.Select(s => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(s)))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (bases.Count != 1 || bases[0] is null) return Fail(OperationErrorKind.PathRejected, "Para criar RAR, todos os itens precisam estar na mesma pasta.");
        }

        // Plano: arquivos e pastas relativos à pasta de origem comum; links nunca são seguidos.
        var results = new List<ItemResult>();
        var plan = new List<(string FullPath, string EntryName, bool IsDirectory, long Size)>();
        try
        {
            foreach (var source in request.SourcePaths)
            {
                var full = Path.GetFullPath(source);
                var baseDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(full))!;
                if (IsInside(destination, full))
                    return Fail(OperationErrorKind.PathRejected, "O compactado não pode ser criado dentro de uma pasta que está sendo compactada.");
                Collect(full, baseDir, plan, results, ct);
            }
        }
        catch (OperationCanceledException)
        {
            return new OperationResult(OperationState.Cancelled, results, OperationErrorKind.Cancelled, "Compactação cancelada; nenhum arquivo foi criado.");
        }
        var totalBytes = plan.Sum(p => p.Size);
        var totalFiles = plan.Count(p => !p.IsDirectory);
        var temp = Path.Join(folder, TempPrefix + Guid.NewGuid().ToString("N") + ".part");
        // Registrado antes de existir: se o app cair, a próxima inicialização remove este parcial.
        var registration = journal.Register(temp, TemporaryKind.PartialFile);
        long done = 0;
        var filesDone = 0;
        if (request.Format == CompressionFormat.Rar)
        {
            // O Rar.exe grava o próprio arquivo: o temporário precisa NÃO existir (ele criaria/atualizaria o RAR).
            var baseDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.SourcePaths[0])))!;
            OperationResult rar;
            try { rar = RarWriter.Create(rarTool!, plan, results, baseDir, temp, destination, request.Strength, rarStallTimeout, ct); }
            finally { if (!File.Exists(temp)) registration.Dispose(); }
            if (rar.FinalState is OperationState.Completed or OperationState.CompletedWithWarnings)
            {
                progress?.Report(new OperationProgress(null, totalFiles, totalFiles, totalBytes, totalBytes));
                if (results.Any(r => r.Outcome is ItemOutcome.Failed or ItemOutcome.Skipped) && rar.FinalState == OperationState.Completed)
                    return rar with { FinalState = OperationState.CompletedWithWarnings, Message = "Compactado criado; alguns itens ficaram de fora (veja a lista)." };
            }
            return rar;
        }
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
            {
                switch (request.Format)
                {
                    case CompressionFormat.Zip: WriteZip(output, request.Strength, plan, results, Report, ct); break;
                    case CompressionFormat.SevenZip: SevenZipWriter.Write(output, request.Strength, plan, results, Report, ct); break;
                    default: WriteTarGz(output, request.Strength, plan, results, Report, ct); break;
                }
                output.Flush(flushToDisk: true);
            }
            File.Move(temp, destination, overwrite: false);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temp);
            return new OperationResult(OperationState.Cancelled, results, OperationErrorKind.Cancelled, "Compactação cancelada; nenhum arquivo foi criado.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            var (kind, message) = ErrorMapper.Map(ex);
            if (File.Exists(destination) && kind == OperationErrorKind.Unknown) (kind, message) = (OperationErrorKind.AlreadyExists, $"\"{fileName}\" foi criado por outro programa enquanto compactávamos.");
            return new OperationResult(OperationState.Failed, results, kind, message);
        }
        finally
        {
            if (!File.Exists(temp)) registration.Dispose(); // se ficou para trás, o registro também fica
        }

        progress?.Report(new OperationProgress(null, filesDone, totalFiles, done, totalBytes));
        var warnings = results.Any(r => r.Outcome is ItemOutcome.Failed or ItemOutcome.Skipped);
        return new OperationResult(warnings ? OperationState.CompletedWithWarnings : OperationState.Completed, results,
            Message: warnings ? "Compactado criado; alguns itens ficaram de fora (veja a lista)." : null, Destination: destination);

        void Report(string name, long bytes, bool fileFinished)
        {
            done += bytes;
            if (fileFinished) filesDone++;
            progress?.Report(new OperationProgress(name, filesDone, totalFiles, done, totalBytes));
        }
    }

    private static void Collect(string full, string baseDir, List<(string, string, bool, long)> plan, List<ItemResult> results, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = new FileInfo(full);
        FileAttributes attrs;
        try { attrs = File.GetAttributes(full); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            results.Add(new ItemResult(Relative(baseDir, full), ItemOutcome.Failed, OperationErrorKind.AccessDenied, "Sem acesso a este item."));
            return;
        }
        var name = Relative(baseDir, full);
        if ((attrs & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
        {
            results.Add(new ItemResult(name, ItemOutcome.Skipped, OperationErrorKind.LinkOrSpecialBlocked, "Link ou junction: não é seguido nem incluído."));
            return;
        }
        if ((attrs & FileAttributes.Directory) == 0)
        {
            plan.Add((full, name, false, info.Length));
            return;
        }
        plan.Add((full, name + "/", true, 0));
        IEnumerable<string> children;
        try { children = Directory.EnumerateFileSystemEntries(full).Order(StringComparer.OrdinalIgnoreCase).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            results.Add(new ItemResult(name, ItemOutcome.Failed, OperationErrorKind.AccessDenied, "Sem acesso ao conteúdo desta pasta."));
            return;
        }
        foreach (var child in children) Collect(child, baseDir, plan, results, ct);
    }

    private static void WriteZip(Stream output, CompressionStrength strength, List<(string FullPath, string EntryName, bool IsDirectory, long Size)> plan,
        List<ItemResult> results, Action<string, long, bool> report, CancellationToken ct)
    {
        var level = strength switch
        {
            CompressionStrength.Fast => CompressionLevel.Fastest,
            CompressionStrength.Maximum => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: System.Text.Encoding.UTF8);
        var buffer = new byte[BufferSize];
        foreach (var item in plan)
        {
            ct.ThrowIfCancellationRequested();
            if (item.IsDirectory)
            {
                zip.CreateEntry(item.EntryName);
                continue;
            }
            FileStream source;
            try { source = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new ItemResult(item.EntryName, ItemOutcome.Failed, ex is UnauthorizedAccessException ? OperationErrorKind.AccessDenied : OperationErrorKind.Unknown,
                    "Não foi possível ler (arquivo em uso ou sem permissão)."));
                continue;
            }
            using (source)
            {
                var entry = zip.CreateEntry(item.EntryName, level);
                entry.LastWriteTime = SafeTime(File.GetLastWriteTime(item.FullPath));
                using var target = entry.Open();
                Copy(source, target, buffer, item.EntryName, report, ct);
            }
            results.Add(new ItemResult(item.EntryName, ItemOutcome.Succeeded));
            report(item.EntryName, 0, true);
        }
    }

    private static void WriteTarGz(Stream output, CompressionStrength strength, List<(string FullPath, string EntryName, bool IsDirectory, long Size)> plan,
        List<ItemResult> results, Action<string, long, bool> report, CancellationToken ct)
    {
        var level = strength switch
        {
            CompressionStrength.Fast => CompressionLevel.Fastest,
            CompressionStrength.Maximum => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };
        using var gzip = new GZipStream(output, level, leaveOpen: true);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var item in plan)
        {
            ct.ThrowIfCancellationRequested();
            if (item.IsDirectory)
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, item.EntryName) { ModificationTime = SafeTime(Directory.GetLastWriteTime(item.FullPath)) });
                continue;
            }
            FileStream source;
            try { source = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new ItemResult(item.EntryName, ItemOutcome.Failed, ex is UnauthorizedAccessException ? OperationErrorKind.AccessDenied : OperationErrorKind.Unknown,
                    "Não foi possível ler (arquivo em uso ou sem permissão)."));
                continue;
            }
            using (source)
            {
                // O TAR precisa do tamanho no cabeçalho: o fluxo é envolvido para contar progresso e respeitar cancelamento.
                using var counted = new ProgressStream(source, item.EntryName, report, ct);
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, item.EntryName)
                {
                    DataStream = counted,
                    ModificationTime = SafeTime(File.GetLastWriteTime(item.FullPath)),
                });
            }
            results.Add(new ItemResult(item.EntryName, ItemOutcome.Succeeded));
            report(item.EntryName, 0, true);
        }
    }

    private static void Copy(Stream source, Stream target, byte[] buffer, string name, Action<string, long, bool> report, CancellationToken ct)
    {
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            target.Write(buffer, 0, read);
            report(name, read, false);
        }
    }

    private static DateTime SafeTime(DateTime time) => time.Year is < 1980 or > 2107 ? new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local) : time;

    private static string Relative(string baseDir, string full) => Path.GetRelativePath(baseDir, full).Replace('\\', '/');

    private static bool IsInside(string candidate, string folder)
    {
        var f = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(f, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static OperationResult Fail(OperationErrorKind kind, string message) => new(OperationState.Failed, [], kind, message);

    /// <summary>Fluxo somente leitura que informa progresso e respeita cancelamento.</summary>
    private sealed class ProgressStream(Stream inner, string name, Action<string, long, bool> report, CancellationToken ct) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ct.ThrowIfCancellationRequested();
            var read = inner.Read(buffer, offset, count);
            if (read > 0) report(name, read, false);
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
