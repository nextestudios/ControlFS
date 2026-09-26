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

    /// <summary>Senha do compactado aberto (cabeçalhos protegidos). Só na memória, só enquanto o compactado está aberto.</summary>
    internal string? ArchivePassword { get; set; }

    /// <summary>Incrementada a cada navegação; respostas atrasadas de outra geração são descartadas.</summary>
    public int Generation { get; internal set; }

    public bool IsArchive => Location is ArchiveLocation;
    public bool CanGoBack => Back.Count > 0;
    public bool CanGoForward => Forward.Count > 0;
}
