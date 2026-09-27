using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Abas do navegador: cada uma é um <see cref="PaneState"/> com localização, histórico, marcação e foco próprios.
/// A faixa (no cabeçalho) só aparece com 2+ abas e fica acima da barra superior: L1/R1 levam à barra e Cima, nela,
/// à faixa; na faixa, L1/R1 (ou esquerda/direita) trocam de aba e Norte cria/fecha. Menu → Abas faz o mesmo sem a faixa.
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
        if (_tabs.Count < 2 && Browser.Region == PaneRegion.Tabs) Browser.Region = PaneRegion.List; // a faixa some com uma aba
        StatusMessage = $"Aba \"{title}\" fechada.";
        if (Browser.Location is null) Screen = Screen.Home;
    }

    /// <summary>
    /// L2/R2 (LT/RT) no navegador com 2+ abas: aba anterior/próxima, dando a volta nas pontas (#185). Com uma aba só
    /// os gatilhos continuam paginando a lista e as seções do início. Modais, teclado e visualizações tratam os gatilhos
    /// antes e nunca chegam aqui; L1/R1 seguem na barra superior.
    /// </summary>
    private bool HandleTabTrigger(InputAction action)
    {
        if (_tabs.Count < 2 || action is not (InputAction.PageUp or InputAction.PageDown)) return false;
        var step = action == InputAction.PageDown ? 1 : -1;
        SwitchTab((ActiveTab + step + _tabs.Count) % _tabs.Count, keepStripFocus: false);
        StatusMessage = $"Aba {ActiveTab + 1} de {_tabs.Count}: {TabTitle(Browser)}.";
        return true;
    }

    private void AddTabTriggerHints(List<Hint> hints)
    {
        if (_tabs.Count < 2) return;
        hints.Add(new(InputAction.PageUp, "Aba anterior"));
        hints.Add(new(InputAction.PageDown, "Próxima aba"));
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

    /// <summary>Nova/fechar aba e, com 2+ abas, trocar para qualquer uma (Norte na faixa ou Menu → Abas).</summary>
    private void ShowTabMenu()
    {
        var index = ActiveTab;
        var fromStrip = Browser.Region == PaneRegion.Tabs;
        var items = new List<MenuItem>
        {
            new("Nova aba", NewTabHere, NewTabUnavailable, Detail: "Abre a pasta atual numa aba nova.", Icon: ActionIcon.NewTab),
            new("Fechar aba", () => CloseTab(index), _tabs.Count <= 1 ? "É a única aba aberta." : null, Icon: ActionIcon.CloseTab),
        };
        if (_tabs.Count > 1)
            for (var i = 0; i < _tabs.Count; i++)
            {
                var target = i;
                items.Add(new MenuItem($"Aba {i + 1}: {TabTitle(_tabs[i])}", () => SwitchTab(target, keepStripFocus: fromStrip),
                    i == index ? "É a aba atual." : null, Icon: ActionIcon.Folder, Section: "Ir para a aba"));
            }
        PushModal(new MenuModal($"Aba {index + 1} de {_tabs.Count}", items) { Icon = ActionIcon.NewTab });
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
