using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Dois painéis lado a lado (#56). O painel esquerdo é a aba ativa (as abas continuam dele); o direito é um
/// <see cref="PaneState"/> próprio, com local, histórico, marcação e foco independentes. <see cref="Browser"/> é sempre o
/// painel com o foco, então abrir, voltar, marcar, buscar e os menus valem para ele sem código novo. L3 (Tab no teclado)
/// troca o foco de painel sem mexer em marcações nem iniciar nada. Ações → Copiar/Mover/Extrair para o outro painel
/// mostram origem e destino antes de executar. Em telas estreitas (portáteis) só um painel aparece, e a escolha fica guardada.
/// </summary>
public sealed partial class AppController
{
    private readonly PaneState _second = new(PaneMode.Browse);

    /// <summary>Painel direito (só aparece com <see cref="DualPaneActive"/>).</summary>
    public PaneState SecondPane => _second;

    /// <summary>Painel esquerdo: a aba ativa.</summary>
    public PaneState LeftPane => _tabs[ActiveTab];

    /// <summary>O foco está no painel direito (vale só com os dois painéis à mostra).</summary>
    public bool SecondPaneFocused { get; private set; }

    /// <summary>A tela cabe dois painéis (publicado pela janela: portáteis e janelas estreitas ficam com um).</summary>
    public bool DualPaneFits { get; private set; } = true;

    /// <summary>Dois painéis ligados e cabendo na tela.</summary>
    public bool DualPaneActive => Settings.DualPane && DualPaneFits;

    /// <summary>O painel sem o foco, quando há dois; null com um painel só.</summary>
    public PaneState? OtherPane => DualPaneActive ? (SecondPaneFocused ? LeftPane : _second) : null;

    /// <summary>Todos os painéis do navegador que podem mostrar uma pasta (abas e, com dois painéis, o direito).</summary>
    private IEnumerable<PaneState> BrowsePanes => DualPaneActive ? [.. _tabs, _second] : _tabs;

    private bool FocusOnSecond => SecondPaneFocused && DualPaneActive;

    public void SetDualPaneFits(bool fits)
    {
        if (DualPaneFits == fits) return;
        DualPaneFits = fits;
        if (Settings.DualPane) AfterPaneLayoutChange();
        RaiseChanged();
    }

    /// <summary>Menu → Dois painéis: liga ou desliga (a escolha fica salva).</summary>
    internal void ToggleDualPane()
    {
        var on = !Settings.DualPane;
        UpdateSettings(s => s with { DualPane = on });
        if (on && _second.Location is null && !_second.IsLoading) OpenSecondPaneBeside();
        AfterPaneLayoutChange();
        StatusMessage = !on ? "Um painel." : DualPaneFits
            ? "Dois painéis: L3 (Tab no teclado) troca de painel; Ações → Copiar/Mover para o outro painel."
            : "Dois painéis ligados, mas a tela é estreita demais: aparecem numa tela maior.";
    }

    /// <summary>O painel direito começa na mesma pasta do esquerdo (ou no primeiro local do disco).</summary>
    private void OpenSecondPaneBeside()
    {
        var folder = LeftPane.LastValidPhysical?.FullPath ?? LeftPane.RestoredPath ?? _fs.GetPlaces().FirstOrDefault(p => p.FullPath is not null)?.FullPath;
        if (folder is not null) Track(NavigateAsync(_second, new PhysicalLocation(folder), pushHistory: false));
    }

    /// <summary>Painéis ligados/desligados ou a tela mudou: o foco fica num painel visível e a tela segue o painel focado.</summary>
    private void AfterPaneLayoutChange()
    {
        if (!DualPaneActive && SecondPaneFocused)
        {
            _second.Region = PaneRegion.List;
            SecondPaneFocused = false;
        }
        if (Screen is Screen.Browser or Screen.Home && TopModal is null)
            Screen = Browser.Location is null && !Browser.IsLoading ? Screen.Home : Screen.Browser;
    }

    /// <summary>L3/Tab: o foco passa para o outro painel. Marcações, foco da lista e operações não mudam.</summary>
    private void SwitchPane()
    {
        if (Screen == Screen.FolderPicker) return; // o seletor de pasta é de um painel só
        if (!DualPaneActive)
        {
            if (Settings.DualPane) StatusMessage = "A tela é estreita demais para dois painéis.";
            return;
        }
        if (Screen == Screen.Home) _homeRegion = PaneRegion.List;
        Browser.Region = PaneRegion.List;
        SecondPaneFocused = !SecondPaneFocused;
        Browser.Region = PaneRegion.List;
        Screen = Browser.Location is null && !Browser.IsLoading ? Screen.Home : Screen.Browser;
        StatusMessage = $"{PaneName(Browser)} ativo" + (Browser.Location is { } here ? ": " + here.DisplayPath : ".");
    }

    /// <summary>Tabs e abas pertencem ao painel esquerdo: usar a faixa ou o menu de abas leva o foco para lá.</summary>
    private void FocusLeftPane()
    {
        if (!SecondPaneFocused) return;
        _second.Region = PaneRegion.List;
        SecondPaneFocused = false;
    }

    public string PaneName(PaneState pane) => ReferenceEquals(pane, _second) ? "Painel direito" : "Painel esquerdo";

    /// <summary>Mouse/toque no painel sem foco: ele passa a ser o ativo (a mesma troca do L3).</summary>
    public void PointerFocusPane(bool second)
    {
        if (TopModal is not null || !DualPaneActive || second == SecondPaneFocused) return;
        SwitchPane();
        RaiseChanged();
    }

    // ---------- Ações para o outro painel ----------

    /// <summary>Pasta do disco mostrada no outro painel, ou o motivo de não haver destino.</summary>
    private (string? Folder, string? Unavailable) OtherPaneFolder(PaneState pane)
    {
        if (OtherPane is not { } other) return (null, "Ligue os dois painéis no Menu.");
        if (other.IsLoading) return (null, "O outro painel ainda está carregando.");
        if (other.Location is not PhysicalLocation there) return (null, "O outro painel não está numa pasta do disco.");
        if (pane.Location is PhysicalLocation here && string.Equals(Path.TrimEndingDirectorySeparator(here.FullPath), Path.TrimEndingDirectorySeparator(there.FullPath), StringComparison.OrdinalIgnoreCase))
            return (null, "Os dois painéis estão na mesma pasta.");
        return (there.FullPath, null);
    }

    private string Arrow => SecondPaneFocused ? "←" : "→";

    /// <summary>Copiar/Mover para o outro painel (ações rápidas), com origem e destino no resumo antes de executar.</summary>
    private List<MenuItem> OtherPaneTransferItems(PaneState pane, IReadOnlyList<FileEntry> entries, string? section = null)
    {
        if (!DualPaneActive || pane.Location is not PhysicalLocation here) return [];
        var (folder, unavailable) = OtherPaneFolder(pane);
        var sources = entries.Where(e => e.FullPath is not null).Select(e => e.FullPath!).ToList();
        unavailable ??= FileOpsUnavailable ?? (sources.Count == 0 ? "Nada para copiar aqui." : null);
        var detail = folder is null ? null : "Para " + folder;
        return
        [
            new MenuItem("Copiar para o outro painel", () => ConfirmTransfer(FileOperationKind.Copy, sources, folder!, here.FullPath), unavailable,
                Detail: detail, Icon: ActionIcon.CopyTo, Section: section, Placement: MenuPlacement.Quick, ShortLabel: $"Copiar {Arrow}"),
            new MenuItem("Mover para o outro painel", () => ConfirmTransfer(FileOperationKind.Move, sources, folder!, here.FullPath), unavailable,
                Detail: detail, Icon: ActionIcon.MoveTo, Section: section, Placement: MenuPlacement.Quick, ShortLabel: $"Mover {Arrow}"),
        ];
    }

    /// <summary>Extrair (tudo ou as entradas marcadas) para a pasta do outro painel, numa pasta com o nome do compactado.</summary>
    private List<MenuItem> OtherPaneExtractItems(PaneState pane, string archivePath, List<string>? selected, string basePath, string? section = null)
    {
        if (!DualPaneActive) return [];
        var (folder, unavailable) = OtherPaneFolder(pane);
        var what = selected is { Count: > 0 } ? $"Extrair seleção ({selected.Count}) para o outro painel" : "Extrair para o outro painel";
        return
        [
            new MenuItem(what, () => BeginExtraction(archivePath, folder!, dedicated: true, selected, basePath), unavailable,
                Detail: folder is null ? null : $"Para {Path.Join(folder, ArchiveFormats.StemOf(archivePath))}", Icon: ActionIcon.Extract, Section: section,
                Placement: MenuPlacement.Quick, ShortLabel: $"Extrair {Arrow}"),
        ];
    }
}
