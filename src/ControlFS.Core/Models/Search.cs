using System.Globalization;

namespace ControlFS.Core.Models;

/// <summary>
/// Resultados de uma busca por nome: um local virtual (não é uma pasta do disco). Os itens têm caminho completo;
/// abrir um resultado leva à pasta real dele.
/// </summary>
public sealed record SearchLocation(string RootPath, string Query, bool IncludeSubfolders) : Location
{
    public override string DisplayPath => $"Busca por \"{Query}\" em {RootPath}" + (IncludeSubfolders ? " e subpastas" : string.Empty);
}

/// <summary>Pedido de busca por nome. Nunca usa índice: enumera a pasta (e, se pedido, as subpastas) na hora.</summary>
public sealed record SearchRequest(string RootPath, string Query, bool IncludeSubfolders, bool IncludeHidden);

/// <summary>Um evento da busca: um item encontrado ou uma pasta que não pôde ser lida (nunca pulada em silêncio).</summary>
public sealed record SearchResult(FileEntry? Match, string? InaccessibleFolder)
{
    public static SearchResult Found(FileEntry entry) => new(entry, null);

    public static SearchResult Skipped(string folder) => new(null, folder);
}

/// <summary>Regra de correspondência: parte do nome, sem diferenciar maiúsculas nem acentos ("relatorio" acha "Relatório").</summary>
public static class SearchQuery
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    public static bool Matches(string name, string query) =>
        query.Length > 0 && Compare.IndexOf(name, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}
