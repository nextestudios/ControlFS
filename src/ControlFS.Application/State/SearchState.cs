using ControlFS.Core.Models;

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

    public bool IsRunning => Status == SearchStatus.Running;
    public bool IsPartial => Status is SearchStatus.Running or SearchStatus.Cancelled or SearchStatus.LimitReached;

    /// <summary>Linha de estado para o rodapé: parcial, concluída ou cancelada, e as pastas puladas.</summary>
    public string Summary
    {
        get
        {
            var count = Results.Count == 1 ? "1 resultado" : $"{Results.Count} resultados";
            var text = Status switch
            {
                SearchStatus.Running => $"Buscando… {count} até agora (parcial)",
                SearchStatus.Cancelled => $"Busca cancelada: {count} (parcial)",
                SearchStatus.LimitReached => $"Busca interrompida no limite de {count}: refine o termo",
                SearchStatus.Failed => "Não foi possível ler a pasta da busca",
                _ => $"Busca concluída: {count}",
            };
            if (Skipped.Count > 0) text += $" · {Skipped.Count} pasta(s) sem permissão ou inacessível(is) foram puladas";
            return text + ".";
        }
    }
}
