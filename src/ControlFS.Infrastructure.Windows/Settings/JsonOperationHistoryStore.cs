using System.Text.Json;
using System.Text.Json.Serialization;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Infrastructure.Windows.Settings;

/// <summary>
/// Histórico em <c>&lt;dados&gt;\history.json</c> (JSON versionado). Gravação atômica (temporário + flush + troca).
/// Arquivo corrompido é preservado com sufixo e o histórico recomeça vazio, com aviso; arquivo de versão mais nova é
/// lido como vazio e não é sobrescrito nesta sessão.
/// </summary>
public sealed class JsonOperationHistoryStore(string dataDirectory) : IOperationHistoryStore
{
    public const int CurrentSchemaVersion = 1;
    private const long MaxBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private bool _readOnly;

    public string FilePath => Path.Join(dataDirectory, "history.json");

    private sealed record Document(int SchemaVersion, IReadOnlyList<OperationHistoryEntry>? Entries);

    public OperationHistoryLoadResult Load()
    {
        if (!File.Exists(FilePath)) return new([], null);
        try
        {
            if (new FileInfo(FilePath).Length > MaxBytes) throw new InvalidDataException("Histórico grande demais.");
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(FilePath), Options) ?? throw new InvalidDataException("Vazio.");
            if (document.SchemaVersion > CurrentSchemaVersion)
            {
                _readOnly = true;
                return new([], "Histórico de operações criado por versão mais nova; ele não será alterado nesta sessão.");
            }
            return new(document.Entries ?? [], null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            var backup = FilePath + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(FilePath, backup); } catch (IOException) { }
            return new([], $"Histórico de operações corrompido foi preservado em {Path.GetFileName(backup)} e recomeçou vazio.");
        }
    }

    public void Save(IReadOnlyList<OperationHistoryEntry> entries)
    {
        if (_readOnly) return;
        Directory.CreateDirectory(dataDirectory);
        var temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new Document(CurrentSchemaVersion, entries), Options);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, FilePath, overwrite: true);
    }
}
