using ControlFS.Core.Models;

namespace ControlFS.Application.State;

public enum PaneMode
{
    Browse,
    /// <summary>Escolha de pasta dentro do app (mostra somente pastas).</summary>
    PickFolder,
}

/// <summary>Um painel: localização, histórico, lista e estado de carregamento independentes.</summary>
public sealed class PaneState(PaneMode mode)
{
    internal readonly Stack<(Location Location, string? FocusId)> Back = new();
    internal readonly Stack<(Location Location, string? FocusId)> Forward = new();
    internal CancellationTokenSource? LoadCts;

    public PaneMode Mode { get; } = mode;
    public Location? Location { get; internal set; }
    public FileListState List { get; } = new();
    public bool IsLoading { get; internal set; }
    public int InaccessibleCount { get; internal set; }
    public ArchiveTree? Archive { get; internal set; }
    public PhysicalLocation? LastValidPhysical { get; internal set; }

    /// <summary>Lista ou barra de caminho (LB/RB alternam).</summary>
    public PaneRegion Region { get; internal set; } = PaneRegion.List;

    /// <summary>Segmento focado na barra de caminho (índice nos segmentos visíveis).</summary>
    public int BreadcrumbFocus { get; internal set; }

    /// <summary>Última busca deste painel; os resultados aparecem quando <see cref="Location"/> é a <see cref="SearchLocation"/> dela.</summary>
    public SearchState? Search { get; internal set; }

    /// <summary>Status do Git da pasta mostrada (#75); null fora de repositórios, com o ajuste desligado ou ainda lendo.</summary>
    public Core.Contracts.GitFolderStatus? Git { get; internal set; }

    /// <summary>O painel mostra os resultados de uma busca (local virtual).</summary>
    public SearchState? ActiveSearch => Location is SearchLocation location && Search is { } search && search.Location == location ? search : null;

    /// <summary>Senha do compactado aberto (cabeçalhos protegidos). Só na memória, só enquanto o compactado está aberto.</summary>
    internal string? ArchivePassword { get; set; }

    /// <summary>Incrementada a cada navegação; respostas atrasadas de outra geração são descartadas.</summary>
    public int Generation { get; internal set; }

    /// <summary>Pasta de onde a aba foi restaurada na inicialização (#51); guardada mesmo se ela estiver indisponível.</summary>
    public string? RestoredPath { get; internal set; }

    /// <summary>A pasta restaurada não existe ou não respondeu: a aba mostra o início até abrir outra pasta.</summary>
    public bool IsUnavailable { get; internal set; }

    public bool IsArchive => Location is ArchiveLocation;
    public bool CanGoBack => Back.Count > 0;
    public bool CanGoForward => Forward.Count > 0;
}
