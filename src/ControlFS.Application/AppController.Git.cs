using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Status do Git (#75), opcional (Configurações → Exibição, desligado por padrão) e somente leitura: ao abrir uma pasta
/// dentro de um repositório, o status é lido fora da thread de UI e chega depois da lista (navegar nunca espera por ele).
/// As linhas ganham "Git: modificado/novo…" e o cabeçalho mostra o ramo. Nenhuma operação do Git é oferecida.
/// </summary>
public sealed partial class AppController
{
    /// <summary>Leituras mais lentas que isto (repositórios enormes) são descartadas: a pasta fica sem marcas.</summary>
    internal static readonly TimeSpan GitStatusTimeout = TimeSpan.FromSeconds(10);

    public IGitStatusReader? Git { get; init; }

    /// <summary>Muda quando um status chega ou some: a tela redesenha as linhas já criadas.</summary>
    public int GitStatusVersion { get; private set; }

    private void RequestGitStatus(PaneState pane)
    {
        var had = pane.Git is not null;
        pane.Git = null;
        if (had) GitStatusVersion++;
        if (Git is not { } reader || !Settings.ShowGitStatus || pane.Mode != PaneMode.Browse || pane.Location is not PhysicalLocation here) return;
        var generation = pane.Generation;
        Track(ReadGitStatusAsync(reader, pane, here.FullPath, generation));
    }

    private async Task ReadGitStatusAsync(IGitStatusReader reader, PaneState pane, string folder, int generation)
    {
        var read = Task.Run(() => reader.Read(folder));
        if (await Task.WhenAny(read, Task.Delay(GitStatusTimeout)) != read) return;
        var status = await read;
        if (status is null || generation != pane.Generation || !Settings.ShowGitStatus) return;
        pane.Git = status;
        GitStatusVersion++;
        RaiseChanged();
    }

    /// <summary>Texto do estado do Git de um item da pasta ativa ("Git: modificado"); null sem mudança.</summary>
    public string? GitState(FileEntry entry) =>
        ActivePane.Git is { } git && entry.FullPath is not null && git.Children.TryGetValue(entry.Name, out var change) ? "Git: " + GitLabel(change, entry.IsContainer) : null;

    internal static string GitLabel(GitChange change, bool folder) => change switch
    {
        GitChange.Conflicted => "conflito",
        GitChange.Modified => folder ? "com mudanças" : "modificado",
        GitChange.Renamed => "renomeado",
        GitChange.Added => "adicionado",
        GitChange.Deleted => "excluído",
        _ => "novo (não rastreado)",
    };

    /// <summary>Linha do cabeçalho numa pasta de repositório: "GIT · ramo main · 3 itens com mudanças".</summary>
    public string? GitSummary => Screen == Screen.Browser && ActivePane.Git is { } git
        ? $"GIT · ramo {git.Branch} · " + (git.ChangedCount == 0 ? "sem mudanças aqui" : Core.Text.Plural.Of(git.ChangedCount, "item com mudanças", "itens com mudanças"))
        : null;

    internal void ToggleGitStatus()
    {
        UpdateSettings(s => s with { ShowGitStatus = !s.ShowGitStatus });
        foreach (var tab in _tabs) RequestGitStatus(tab);
        StatusMessage = Settings.ShowGitStatus ? "Status do Git ligado: pastas de repositórios mostram o ramo e o que mudou." : "Status do Git desligado.";
    }
}
