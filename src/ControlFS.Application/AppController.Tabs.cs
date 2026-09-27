using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Abas do navegador: cada uma é um <see cref="PaneState"/> com localização, histórico, marcação e foco próprios.
/// RB leva à faixa de abas; nela, LB/RB (ou esquerda/direita) trocam de aba e Norte cria/fecha.
/// </summary>
public sealed partial class AppController
{
    internal const int MaxTabs = 8;

    public IReadOnlyList<PaneState> Tabs => _tabs;
    public int ActiveTab { get; private set; }

    /// <summary>Nome curto da aba: pasta, compactado ou busca mostrados nela.</summary>
    public static string TabTitle(PaneState tab) => tab.Location switch
    {
        PhysicalLocation physical => FolderLabel(physical.FullPath),
        ArchiveLocation archive => Path.GetFileName(archive.ArchivePath),
        SearchLocation search => $"Busca: {search.Query}",
        RecycleBinLocation => "Lixeira",
        ThisPcLocation => "Meu computador",
        _ => "Nova aba",
    };

    private static string FolderLabel(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.Length > 0 ? name : path;
    }

    private string? NewTabUnavailable => _tabs.Count >= MaxTabs ? $"Limite de {MaxTabs} abas: feche uma primeiro." : null;

    /// <summary>Abre uma aba nova logo depois da atual, já ativa, em <paramref name="folder"/>.</summary>
    internal void OpenInNewTab(string folder)
    {
        if (NewTabUnavailable is { } reason)
        {
            StatusMessage = reason;
            return;
        }
        Browser.Region = PaneRegion.List;
        var tab = new PaneState(PaneMode.Browse);
        _tabs.Insert(ActiveTab + 1, tab);
        ActiveTab++;
        Screen = Screen.Browser;
        Track(NavigateAsync(tab, new PhysicalLocation(folder), pushHistory: false));
        StatusMessage = $"Aba {ActiveTab + 1} de {_tabs.Count}: {FolderLabel(folder)}.";
    }

    /// <summary>Nova aba na última pasta do disco da aba atual (dentro de um compactado ou busca, a pasta de origem).</summary>
    private void NewTabHere()
    {
        var folder = Browser.LastValidPhysical?.FullPath ?? _fs.GetPlaces().FirstOrDefault(p => p.FullPath is not null)?.FullPath;
        if (folder is null) return;
        OpenInNewTab(folder);
    }

    internal void CloseTab(int index)
    {
        if (_tabs.Count <= 1 || index < 0 || index >= _tabs.Count) return;
        var tab = _tabs[index];
        tab.LoadCts?.Cancel();
        tab.Generation++;
        tab.Search?.Cts?.Cancel();
        var title = TabTitle(tab);
        _tabs.RemoveAt(index);
        if (ActiveTab > index || ActiveTab >= _tabs.Count) ActiveTab = Math.Max(0, ActiveTab - 1);
        StatusMessage = $"Aba \"{title}\" fechada.";
        if (Browser.Location is null) Screen = Screen.Home;
    }

    private void SwitchTab(int index, bool keepStripFocus)
    {
        index = Math.Clamp(index, 0, _tabs.Count - 1);
        if (index == ActiveTab) return;
        Browser.Region = PaneRegion.List;
        ActiveTab = index;
        Browser.Region = keepStripFocus ? PaneRegion.Tabs : PaneRegion.List;
        // Uma aba ainda sem pasta (abertura cancelada) mostra o início, como o navegador sem localização.
        Screen = Browser.Location is null && !Browser.IsLoading ? Screen.Home : Screen.Browser;
    }

    private void HandleTabs(PaneState pane, InputAction action)
    {
        switch (action)
        {
            case InputAction.NavigateLeft:
            case InputAction.PreviousRegion:
                SwitchTab(ActiveTab - 1, keepStripFocus: true);
                break;
            case InputAction.NavigateRight:
            case InputAction.NextRegion:
                SwitchTab(ActiveTab + 1, keepStripFocus: true);
                break;
            case InputAction.PageUp: SwitchTab(0, keepStripFocus: true); break;
            case InputAction.PageDown: SwitchTab(_tabs.Count - 1, keepStripFocus: true); break;
            case InputAction.Confirm:
            case InputAction.NavigateDown:
            case InputAction.Back:
                pane.Region = PaneRegion.List;
                break;
            case InputAction.OpenContextMenu:
                ShowTabMenu();
                break;
            case InputAction.OpenAppMenu:
                pane.Region = PaneRegion.List;
                ShowAppMenu();
                break;
        }
    }

    private void ShowTabMenu()
    {
        var index = ActiveTab;
        PushModal(new MenuModal($"Aba {index + 1} de {_tabs.Count}",
        [
            new MenuItem("Nova aba", NewTabHere, NewTabUnavailable, Detail: "Abre a pasta atual numa aba nova."),
            new MenuItem("Fechar aba", () => CloseTab(index), _tabs.Count <= 1 ? "É a única aba aberta." : null),
        ]));
    }

    /// <summary>Mouse/toque numa aba: ativa a aba e volta o foco para a lista.</summary>
    public void PointerActivateTab(int index)
    {
        if (TopModal is not null || Screen != Screen.Browser || index < 0 || index >= _tabs.Count) return;
        SwitchTab(index, keepStripFocus: false);
        Browser.Region = PaneRegion.List;
        RaiseChanged();
    }
}
