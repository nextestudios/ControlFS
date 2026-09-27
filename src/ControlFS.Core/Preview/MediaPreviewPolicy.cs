using System.Globalization;

namespace ControlFS.Core.Preview;

/// <summary>
/// Quais arquivos a reprodução interna aceita (#60, #61). A extensão só escolhe o caminho; o conteúdo é conferido antes de
/// chegar ao reprodutor: um executável renomeado (cabeçalho MZ) nunca é entregue aos decodificadores.
/// </summary>
public static class MediaPreviewPolicy
{
    /// <summary>Áudio que o Windows 10/11 toca sem extensões extras (OGG/Opus dependem das extensões da Microsoft Store).</summary>
    public static readonly IReadOnlyDictionary<string, string> AudioTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".wma"] = "audio/x-ms-wma",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".flac"] = "audio/flac",
        [".ogg"] = "audio/ogg",
        [".opus"] = "audio/ogg",
    };

    /// <summary>Vídeo que o Windows costuma tocar; HEVC, VP9/AV1 e MKV podem exigir extensões de vídeo instaladas.</summary>
    public static readonly IReadOnlyDictionary<string, string> VideoTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".mov"] = "video/quicktime",
        [".wmv"] = "video/x-ms-wmv",
        [".avi"] = "video/x-msvideo",
        [".mkv"] = "video/x-matroska",
        [".webm"] = "video/webm",
        [".3gp"] = "video/3gpp",
    };

    public static bool IsAudioExtension(string extension) => AudioTypes.ContainsKey(extension);

    public static bool IsVideoExtension(string extension) => VideoTypes.ContainsKey(extension);

    /// <summary>Tipo MIME pela extensão (o reprodutor ainda confere o conteúdo).</summary>
    public static string ContentType(string path)
    {
        var extension = Path.GetExtension(path);
        return AudioTypes.TryGetValue(extension, out var audio) ? audio
            : VideoTypes.TryGetValue(extension, out var video) ? video
            : "application/octet-stream";
    }

    /// <exception cref="PreviewException">Arquivo vazio ou executável disfarçado.</exception>
    public static void Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> head = stackalloc byte[2];
        var read = stream.Read(head);
        if (read == 0) throw new PreviewException("O arquivo está vazio.");
        if (read == 2 && head[0] == (byte)'M' && head[1] == (byte)'Z')
            throw new PreviewException("O conteúdo é um programa, não áudio ou vídeo: não será reproduzido.");
    }

    /// <summary>Tempo em "m:ss" ou "h:mm:ss" (negativos viram zero).</summary>
    public static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{time.Minutes}:{time.Seconds:00}");
    }
}
