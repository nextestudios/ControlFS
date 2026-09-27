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
        null when tab.RestoredPath is { } restored => FolderLabel(restored) + (tab.IsUnavailable ? " (indisponível)" : string.Empty),
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
        tab.IsLoading = false;
        tab.Search?.Cts?.Cancel();
        tab.ArchivePassword = null; // senha só enquanto o compactado está aberto numa aba
        var title = TabTitle(tab);
        _tabs.RemoveAt(index);
        RememberClosedTab(tab, index);
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
            new("Duplicar aba", () => DuplicateTab(index), NewTabUnavailable, Detail: "Mesma pasta e histórico numa aba nova, sem as marcações.", Icon: ActionIcon.Copy),
            new("Fechar aba", () => CloseTab(index), _tabs.Count <= 1 ? "É a única aba aberta." : null, Icon: ActionIcon.CloseTab),
            new("Reabrir aba fechada", ReopenClosedTab, ReopenClosedTabUnavailable,
                Detail: _closedTabs.Count > 0 ? $"\"{TabTitle(_closedTabs[^1].Tab)}\", com o histórico dela." : null, Icon: ActionIcon.Undo),
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

    // ---------- Abas entre sessões (#51) ----------

    private bool _tabsRestored;

    /// <summary>Pasta do disco que representa a aba ao salvar: a atual, a última antes de um compactado/busca ou a restaurada.</summary>
    private static string? TabFolder(PaneState tab) =>
        tab.Location is PhysicalLocation physical ? physical.FullPath : tab.LastValidPhysical?.FullPath ?? tab.RestoredPath;

    /// <summary>
    /// Na inicialização: com 2+ abas salvas, recria as abas e abre a que estava ativa. Cada pasta é conferida fora da
    /// thread de UI (uma unidade de rede desligada não trava a tela); a que sumiu vira uma aba indisponível que mostra
    /// o início com um aviso. Nada aqui derruba o app: caminhos inválidos no arquivo só geram abas indisponíveis.
    /// </summary>
    private bool RestoreOpenTabs()
    {
        _tabsRestored = true;
        if (!Settings.RestoreTabs) return false;
        var saved = Settings.OpenTabs.Where(p => !string.IsNullOrWhiteSpace(p)).Take(MaxTabs).ToList();
        if (saved.Count < 2) return false;
        _tabs.Clear();
        foreach (var folder in saved) _tabs.Add(new PaneState(PaneMode.Browse) { RestoredPath = folder, IsLoading = true });
        ActiveTab = Math.Clamp(Settings.ActiveOpenTab, 0, _tabs.Count - 1);
        foreach (var tab in _tabs) Track(RestoreTabAsync(tab, tab.RestoredPath!));
        return true;
    }

    private async Task RestoreTabAsync(PaneState tab, string folder)
    {
        var generation = tab.Generation;
        bool exists;
        try { exists = await Task.Run(() => SafeDirectoryExists(folder, waitForNetwork: true)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { exists = false; }
        if (generation != tab.Generation || !_tabs.Contains(tab)) return; // fechada ou já levada a outra pasta
        if (exists)
        {
            await NavigateAsync(tab, new PhysicalLocation(folder), pushHistory: false);
            if (tab.Location is not null || generation + 1 != tab.Generation || !_tabs.Contains(tab)) return;
        }
        tab.IsLoading = false;
        tab.IsUnavailable = true;
        StatusMessage = $"Aba \"{FolderLabel(folder)}\" indisponível: a pasta não existe ou não está acessível. Ela mostra o início.";
        if (ReferenceEquals(tab, Browser) && Screen == Screen.Browser) GoHome();
        else RaiseChanged();
    }

    /// <summary>Grava as abas quando mudam (chamado a cada atualização da tela; só escreve se algo mudou).</summary>
    private void SaveOpenTabs()
    {
        if (!_tabsRestored || !Settings.RestoreTabs) return;
        var folders = new List<string>();
        var active = 0;
        if (_tabs.Count >= 2)
            for (var i = 0; i < _tabs.Count; i++)
            {
                if (TabFolder(_tabs[i]) is not { } folder) continue;
                if (i == ActiveTab) active = folders.Count;
                folders.Add(folder);
            }
        if (folders.Count < 2)
        {
            folders.Clear();
            active = 0;
        }
        if (active == Settings.ActiveOpenTab && folders.SequenceEqual(Settings.OpenTabs, StringComparer.Ordinal)) return;
        UpdateSettings(s => s with { OpenTabs = folders, ActiveOpenTab = active }, notify: false);
    }

    internal void ToggleRestoreTabs()
    {
        var restore = !Settings.RestoreTabs;
        UpdateSettings(s => restore ? s with { RestoreTabs = true } : s with { RestoreTabs = false, OpenTabs = [], ActiveOpenTab = 0 });
        SaveOpenTabs();
        StatusMessage = restore ? "As abas abertas serão restauradas ao abrir o ControlFS." : "As abas não serão mais restauradas.";
    }

    // ---------- Abas fechadas recentemente (#52) ----------

    internal const int MaxClosedTabs = 10;

    /// <summary>Abas fechadas, a mais recente no fim: o próprio <see cref="PaneState"/> (local, histórico e foco).</summary>
    private readonly List<(PaneState Tab, int Index)> _closedTabs = [];

    public int ClosedTabCount => _closedTabs.Count;

    private void RememberClosedTab(PaneState tab, int index)
    {
        if (tab.Location is null && tab.RestoredPath is null) return; // nada para reabrir
        tab.List.ClearSelection(); // reabrir traz o lugar e o histórico, não marcações antigas
        _closedTabs.Add((tab, index));
        if (_closedTabs.Count > MaxClosedTabs) _closedTabs.RemoveAt(0);
    }

    private string? ReopenClosedTabUnavailable => _closedTabs.Count == 0 ? "Nenhuma aba foi fechada nesta sessão." : NewTabUnavailable;

    /// <summary>Reabre a última aba fechada na posição que ela tinha, já ativa, com o local e o histórico dela.</summary>
    internal void ReopenClosedTab()
    {
        if (ReopenClosedTabUnavailable is { } reason)
        {
            StatusMessage = reason;
            return;
        }
        var (tab, index) = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);
        Browser.Region = PaneRegion.List;
        index = Math.Clamp(index, 0, _tabs.Count);
        _tabs.Insert(index, tab);
        ActiveTab = index;
        tab.Region = PaneRegion.List;
        // A pasta pode ter mudado enquanto a aba estava fechada: relê (compactado e busca voltam como estavam).
        if (tab.Location is PhysicalLocation) Refresh(tab);
        Screen = tab.Location is null ? Screen.Home : Screen.Browser;
        StatusMessage = $"Aba \"{TabTitle(tab)}\" reaberta ({ActiveTab + 1} de {_tabs.Count}).";
    }

    // ---------- Duplicar aba (#53) ----------

    /// <summary>
    /// Abre, logo depois de <paramref name="index"/>, uma aba no mesmo local, com cópia do histórico (Voltar/Avançar
    /// independentes) e o foco no mesmo item, mas sem as marcações. Resultados de busca não são copiados: a cópia abre
    /// na pasta de onde a busca partiu.
    /// </summary>
    internal void DuplicateTab(int index)
    {
        if (NewTabUnavailable is { } reason)
        {
            StatusMessage = reason;
            return;
        }
        if (index < 0 || index >= _tabs.Count) return;
        var source = _tabs[index];
        Location? target = source.Location is SearchLocation or RecycleBinLocation or null ? source.LastValidPhysical : source.Location;
        if (target is null)
        {
            StatusMessage = "Esta aba ainda não tem uma pasta para duplicar.";
            return;
        }
        var copy = new PaneState(PaneMode.Browse)
        {
            LastValidPhysical = source.LastValidPhysical,
            Archive = source.Archive, // somente leitura: pode ser compartilhada
            ArchivePassword = source.ArchivePassword,
        };
        foreach (var entry in source.Back.Reverse()) copy.Back.Push(entry);
        foreach (var entry in source.Forward.Reverse()) copy.Forward.Push(entry);
        copy.List.SetSort(source.List.Sort);
        Browser.Region = PaneRegion.List;
        _tabs.Insert(index + 1, copy);
        ActiveTab = index + 1;
        Screen = Screen.Browser;
        Track(NavigateAsync(copy, target, pushHistory: false, focusId: target == source.Location ? source.List.FocusedId : null));
        StatusMessage = $"Aba duplicada ({ActiveTab + 1} de {_tabs.Count}).";
    }
}
