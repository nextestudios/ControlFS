using System.Diagnostics;
using System.Text.Json;
using ControlFS.Core.Contracts;

namespace ControlFS.Infrastructure.Windows.FileSystem;

/// <summary>
/// Registro de temporários em disco: um arquivo pequeno por temporário ativo na pasta de dados do app, com o caminho,
/// o tipo, o token do manifesto (staging) e o processo dono. Ver <see cref="ITemporaryJournal"/>.
/// </summary>
public sealed class TemporaryJournal : ITemporaryJournal
{
    public const string StagingPrefix = ".controlfs-staging-";
    public const string ManifestName = ".controlfs-operation";
    private const string Extension = ".json";
    private static readonly string[] PartialPrefixes = [".controlfs-copy-", ".controlfs-new-"];
    private const string EmptyFolderPrefix = ".controlfs-new-";

    private readonly string _directory;
    private readonly int _processId;
    private readonly long _processStart;

    /// <param name="directory">Pasta do registro (dados do app).</param>
    /// <param name="processId">Processo dono dos registros criados por esta instância (testes simulam outro processo).</param>
    /// <param name="processStartTicks">Início do processo dono, em ticks UTC (distingue PIDs reaproveitados).</param>
    public TemporaryJournal(string directory, int? processId = null, long? processStartTicks = null)
    {
        _directory = directory;
        using var current = Process.GetCurrentProcess();
        _processId = processId ?? current.Id;
        _processStart = processStartTicks ?? current.StartTime.ToUniversalTime().Ticks;
    }

    /// <summary>Texto do manifesto de uma pasta de staging com o token registrado.</summary>
    public static string ManifestContent(string token) => $"controlfs-staging v2 token={token} {DateTimeOffset.UtcNow:O}";

    public IDisposable Register(string path, TemporaryKind kind, string? token = null)
    {
        var file = Path.Join(_directory, Guid.NewGuid().ToString("N") + Extension);
        try
        {
            Directory.CreateDirectory(_directory);
            var entry = new Entry(1, kind, Path.GetFullPath(path), token, _processId, _processStart);
            File.WriteAllText(file, JsonSerializer.Serialize(entry, EntryContext.Default.Entry));
            return new Registration(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem registro a operação continua; só perde a limpeza automática se o app cair no meio.
            return new Registration(null);
        }
    }

    public LeftoverCleanup CleanUpLeftovers()
    {
        var removed = new List<string>();
        var kept = 0;
        if (!Directory.Exists(_directory)) return new LeftoverCleanup(removed, kept);
        foreach (var file in Directory.EnumerateFiles(_directory, "*" + Extension).ToList())
        {
            Entry? entry;
            try
            {
                entry = JsonSerializer.Deserialize(File.ReadAllText(file), EntryContext.Default.Entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                continue; // ilegível agora (ex.: sendo gravado por outra instância): tenta na próxima inicialização
            }
            if (entry is null || entry.Version != 1 || string.IsNullOrEmpty(entry.Path))
            {
                TryDelete(file);
                continue;
            }
            if (OwnerIsRunning(entry))
            {
                kept++; // operação ativa de outra instância do ControlFS
                continue;
            }
            switch (Remove(entry))
            {
                case Outcome.Removed:
                    removed.Add(entry.Path);
                    TryDelete(file);
                    break;
                case Outcome.NotOurs:
                    TryDelete(file); // já não existe, ou não é comprovadamente nosso: nada é tocado no disco
                    break;
                default:
                    kept++; // nosso, mas não deu para remover agora (em uso)
                    break;
            }
        }
        return new LeftoverCleanup(removed, kept);
    }

    private enum Outcome
    {
        Removed,
        NotOurs,
        Busy,
    }

    private static Outcome Remove(Entry entry)
    {
        var path = entry.Path;
        var name = Path.GetFileName(path);
        try
        {
            switch (entry.Kind)
            {
                case TemporaryKind.StagingFolder:
                    if (!name.StartsWith(StagingPrefix, StringComparison.Ordinal) || !IsRealDirectory(path) || string.IsNullOrEmpty(entry.Token)) return Outcome.NotOurs;
                    var manifest = Path.Join(path, ManifestName);
                    if (!IsRealFile(manifest) || new FileInfo(manifest).Length > 4096) return Outcome.NotOurs;
                    if (!File.ReadAllText(manifest).Contains($" token={entry.Token} ", StringComparison.Ordinal)) return Outcome.NotOurs;
                    DeleteTree(path);
                    return Outcome.Removed;
                case TemporaryKind.PartialFile:
                    if (!PartialPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)) || !name.EndsWith(".part", StringComparison.Ordinal) || !IsRealFile(path))
                        return Outcome.NotOurs;
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    return Outcome.Removed;
                case TemporaryKind.EmptyFolder:
                    if (!name.StartsWith(EmptyFolderPrefix, StringComparison.Ordinal) || !IsRealDirectory(path) || Directory.EnumerateFileSystemEntries(path).Any())
                        return Outcome.NotOurs;
                    Directory.Delete(path);
                    return Outcome.Removed;
                default:
                    return Outcome.NotOurs;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Outcome.Busy;
        }
    }

    /// <summary>Apaga a árvore sem seguir links: um link/junction dentro dela é removido, nunca o que ele aponta.</summary>
    private static void DeleteTree(string path)
    {
        foreach (var child in new DirectoryInfo(path).EnumerateFileSystemInfos().ToList())
        {
            if ((child.Attributes & FileAttributes.ReparsePoint) != 0 || child.LinkTarget is not null)
            {
                if ((child.Attributes & FileAttributes.Directory) != 0) Directory.Delete(child.FullName);
                else File.Delete(child.FullName);
            }
            else if ((child.Attributes & FileAttributes.Directory) != 0)
            {
                DeleteTree(child.FullName);
            }
            else
            {
                File.SetAttributes(child.FullName, FileAttributes.Normal);
                File.Delete(child.FullName);
            }
        }
        Directory.Delete(path);
    }

    private static bool IsRealDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 && info.LinkTarget is null;
    }

    private static bool IsRealFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 && info.LinkTarget is null;
    }

    private static bool OwnerIsRunning(Entry entry)
    {
        try
        {
            using var process = Process.GetProcessById(entry.ProcessId);
            return process.StartTime.ToUniversalTime().Ticks == entry.ProcessStartTicks;
        }
        catch (ArgumentException)
        {
            return false; // nenhum processo com esse PID
        }
        catch (InvalidOperationException)
        {
            return false; // encerrou durante a consulta
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true; // existe, mas não dá para conferir: por segurança, não mexe
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class Registration(string? file) : IDisposable
    {
        public void Dispose()
        {
            if (file is not null) TryDelete(file);
        }
    }

    internal sealed record Entry(int Version, TemporaryKind Kind, string Path, string? Token, int ProcessId, long ProcessStartTicks);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(TemporaryJournal.Entry))]
internal sealed partial class EntryContext : System.Text.Json.Serialization.JsonSerializerContext;
