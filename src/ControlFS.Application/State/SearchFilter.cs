using ControlFS.Core.Models;
using ControlFS.Core.Policies;

namespace ControlFS.Application.State;

[Flags]
public enum SearchTypeFilter
{
    None = 0,
    Folders = 1,
    Images = 2,
    Videos = 4,
    Audio = 8,
    Documents = 16,
    Archives = 32,
    Executables = 64,
}

public enum SearchSizeFilter
{
    Any,
    Under1MB,
    From1To100MB,
    From100MBTo1GB,
    Over1GB,
}

public enum SearchDateFilter
{
    Any,
    Today,
    Last7Days,
    Last30Days,
    LastYear,
}

/// <summary>
/// Filtros dos resultados de busca (#47): tipos combinados entre si (qualquer um marcado), e tamanho e data somados a
/// eles (todos precisam valer). Aplicados sobre os resultados já encontrados, sem refazer a busca.
/// </summary>
public sealed record SearchFilter(SearchTypeFilter Types = SearchTypeFilter.None, SearchSizeFilter Size = SearchSizeFilter.Any, SearchDateFilter Date = SearchDateFilter.Any)
{
    public static SearchFilter None { get; } = new();

    public bool IsActive => Types != SearchTypeFilter.None || Size != SearchSizeFilter.Any || Date != SearchDateFilter.Any;

    public static IReadOnlyList<SearchTypeFilter> AllTypes { get; } =
        [SearchTypeFilter.Folders, SearchTypeFilter.Images, SearchTypeFilter.Videos, SearchTypeFilter.Audio, SearchTypeFilter.Documents, SearchTypeFilter.Archives, SearchTypeFilter.Executables];

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif", ".avif", ".svg", ".ico", ".raw", ".dng", ".cr2", ".nef", ".psd" };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".flv", ".3gp", ".ts", ".m2ts" };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".opus", ".m4a", ".wma", ".aiff", ".mid", ".midi" };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".txt", ".md", ".rtf", ".doc", ".docx", ".odt", ".xls", ".xlsx", ".ods", ".csv", ".ppt", ".pptx", ".odp", ".epub", ".json", ".xml", ".html", ".htm", ".log" };

    public static SearchTypeFilter TypeOf(FileEntry entry)
    {
        if (entry.IsContainer) return SearchTypeFilter.Folders;
        var extension = entry.Extension;
        if (ImageExtensions.Contains(extension)) return SearchTypeFilter.Images;
        if (VideoExtensions.Contains(extension)) return SearchTypeFilter.Videos;
        if (AudioExtensions.Contains(extension)) return SearchTypeFilter.Audio;
        if (DocumentExtensions.Contains(extension)) return SearchTypeFilter.Documents;
        if (ArchiveFormats.HasExtractableExtension(entry.Name)) return SearchTypeFilter.Archives;
        if (ExecutableFiles.IsPotentiallyExecutable(entry.Name)) return SearchTypeFilter.Executables;
        return SearchTypeFilter.None;
    }

    public bool Matches(FileEntry entry, DateTimeOffset now)
    {
        if (Types != SearchTypeFilter.None && (Types & TypeOf(entry)) == 0) return false;
        if (Size != SearchSizeFilter.Any)
        {
            // Pastas não têm tamanho: com filtro de tamanho, ficam de fora.
            if (entry.IsContainer || entry.Size is not long bytes) return false;
            var inRange = Size switch
            {
                SearchSizeFilter.Under1MB => bytes < Mb,
                SearchSizeFilter.From1To100MB => bytes is >= Mb and < 100 * Mb,
                SearchSizeFilter.From100MBTo1GB => bytes is >= 100 * Mb and < 1024 * Mb,
                _ => bytes >= 1024 * Mb,
            };
            if (!inRange) return false;
        }
        if (Date != SearchDateFilter.Any)
        {
            if (entry.Modified is not { } modified) return false;
            var local = now.ToLocalTime();
            var since = Date switch
            {
                SearchDateFilter.Today => new DateTimeOffset(local.Date, local.Offset),
                SearchDateFilter.Last7Days => now.AddDays(-7),
                SearchDateFilter.Last30Days => now.AddDays(-30),
                _ => now.AddYears(-1),
            };
            if (modified < since) return false;
        }
        return true;
    }

    private const long Mb = 1024 * 1024;

    public static string TypeName(SearchTypeFilter type) => type switch
    {
        SearchTypeFilter.Folders => "Pastas",
        SearchTypeFilter.Images => "Imagens",
        SearchTypeFilter.Videos => "Vídeos",
        SearchTypeFilter.Audio => "Músicas e áudio",
        SearchTypeFilter.Documents => "Documentos",
        SearchTypeFilter.Archives => "Compactados",
        SearchTypeFilter.Executables => "Executáveis",
        _ => type.ToString(),
    };

    public static string SizeName(SearchSizeFilter size) => size switch
    {
        SearchSizeFilter.Under1MB => "menos de 1 MB",
        SearchSizeFilter.From1To100MB => "1 MB a 100 MB",
        SearchSizeFilter.From100MBTo1GB => "100 MB a 1 GB",
        SearchSizeFilter.Over1GB => "mais de 1 GB",
        _ => "qualquer",
    };

    public static string DateName(SearchDateFilter date) => date switch
    {
        SearchDateFilter.Today => "hoje",
        SearchDateFilter.Last7Days => "últimos 7 dias",
        SearchDateFilter.Last30Days => "últimos 30 dias",
        SearchDateFilter.LastYear => "último ano",
        _ => "qualquer",
    };

    /// <summary>Resumo curto para o rodapé ("Imagens, Vídeos · mais de 1 GB").</summary>
    public string Describe()
    {
        var parts = new List<string>();
        var types = AllTypes.Where(t => (Types & t) != 0).Select(TypeName).ToList();
        if (types.Count > 0) parts.Add(string.Join(", ", types));
        if (Size != SearchSizeFilter.Any) parts.Add(SizeName(Size));
        if (Date != SearchDateFilter.Any) parts.Add("modificado " + DateName(Date));
        return string.Join(" · ", parts);
    }
}
