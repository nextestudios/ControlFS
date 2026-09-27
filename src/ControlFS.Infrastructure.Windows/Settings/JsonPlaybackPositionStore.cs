using System.Text.Json;
using ControlFS.Core.Contracts;
using ControlFS.Core.Preview;

namespace ControlFS.Infrastructure.Windows.Settings;

/// <summary>
/// Posições de vídeo em <c>&lt;dados&gt;\playback.json</c> (#170): só resumos SHA-256 e segundos, nunca caminhos. Gravação
/// atômica (temporário + flush + troca); arquivo ilegível ou grande demais recomeça vazio (perder uma posição não é grave).
/// </summary>
public sealed class JsonPlaybackPositionStore(string dataDirectory) : IPlaybackPositionStore
{
    private const long MaxBytes = 1024 * 1024;

    public string FilePath => Path.Join(dataDirectory, "playback.json");

    private sealed record Entry(string Key, double Seconds, DateTimeOffset Updated);

    public IReadOnlyList<PlaybackPosition> Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > MaxBytes) return [];
            var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? [];
            return [.. entries
                .Where(e => e.Key is { Length: 64 } && double.IsFinite(e.Seconds) && e.Seconds > 0 && e.Seconds < TimeSpan.MaxValue.TotalSeconds)
                .OrderByDescending(e => e.Updated)
                .Take(PlaybackResume.MaxEntries)
                .Select(e => new PlaybackPosition(e.Key, TimeSpan.FromSeconds(e.Seconds), e.Updated))];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<PlaybackPosition> positions)
    {
        Directory.CreateDirectory(dataDirectory);
        if (positions.Count == 0)
        {
            File.Delete(FilePath);
            return;
        }
        var temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, positions.Take(PlaybackResume.MaxEntries).Select(p => new Entry(p.Key, p.Position.TotalSeconds, p.Updated)).ToList());
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, FilePath, overwrite: true);
    }
}
