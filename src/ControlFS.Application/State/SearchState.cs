using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application.State;

public enum SearchStatus
{
    /// <summary>Procurando: os resultados mostrados são parciais.</summary>
    Running,
    Completed,
    /// <summary>Cancelada pelo usuário (ou ao sair da busca): os resultados mostrados são parciais.</summary>
    Cancelled,
    /// <summary>Parou no limite de resultados: refine o termo.</summary>
    LimitReached,
    /// <summary>A pasta da busca não pôde ser lida.</summary>
    Failed,
}

/// <summary>
/// A última busca de um painel: termo, escopo, estado e resultados (guardados para voltar a eles depois de abrir um).
/// Mutada só na thread de UI.
/// </summary>
public sealed class SearchState(SearchLocation location)
{
    internal readonly List<FileEntry> Results = [];
    internal readonly List<string> Skipped = [];
    internal CancellationTokenSource? Cts;

    public SearchLocation Location { get; } = location;
    public SearchStatus Status { get; internal set; } = SearchStatus.Running;
    public int ResultCount => Results.Count;

    /// <summary>Pastas que não puderam ser lidas (sem permissão ou indisponíveis): relatadas, nunca puladas em silêncio.</summary>
    public IReadOnlyList<string> SkippedFolders => Skipped;

    /// <summary>Filtros aplicados aos resultados (a lista mostra só os que passam; os demais continuam guardados).</summary>
    public SearchFilter Filter { get; internal set; } = SearchFilter.None;

    /// <summary>Resultados que passam pelos filtros atuais.</summary>
    public IEnumerable<FileEntry> VisibleResults()
    {
        if (!Filter.IsActive) return Results;
        var now = DateTimeOffset.Now;
        return Results.Where(r => Filter.Matches(r, now));
    }

    public bool IsRunning => Status == SearchStatus.Running;
    public bool IsPartial => Status is SearchStatus.Running or SearchStatus.Cancelled or SearchStatus.LimitReached;

    /// <summary>Linha de estado para o rodapé: parcial, concluída ou cancelada, e as pastas puladas.</summary>
    public string Summary
    {
        get
        {
            var count = Results.Count == 1 ? "1 resultado" : $"{Results.Count} resultados";
            if (Filter.IsActive) count = $"{VisibleResults().Count()} de {count} (filtros: {Filter.Describe()})";
            var text = Status switch
            {
                SearchStatus.Running => $"Buscando… {count} até agora (parcial)",
                SearchStatus.Cancelled => $"Busca cancelada: {count} (parcial)",
                SearchStatus.LimitReached => $"Busca interrompida no limite de {count}: refine o termo",
                SearchStatus.Failed => "Não foi possível ler a pasta da busca",
                _ => $"Busca concluída: {count}",
            };
            if (Skipped.Count > 0) text += $" · {Plural.Of(Skipped.Count, "pasta sem permissão ou inacessível foi pulada", "pastas sem permissão ou inacessíveis foram puladas")}";
            return text + ".";
        }
    }
}
