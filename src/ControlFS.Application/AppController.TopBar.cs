using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Barra superior compartilhada (início e navegador): à esquerda o caminho (raiz "Locais"/"Meu computador" e os
/// segmentos), à direita o acesso rápido; uma região só, com um foco. L1 entra na pasta de cima e R1 no primeiro atalho
/// (no início, os dois no primeiro atalho); na barra, L1/R1 e esquerda/direita vão ao alvo anterior/seguinte. A pasta
/// atual é só o rótulo do local: nunca recebe o foco nem recarrega. Sul abre; baixo e Leste voltam ao conteúdo; Cima
/// leva às abas quando há mais de uma (#176).
/// </summary>
public sealed partial class AppController
{
    private PaneRegion _homeRegion = PaneRegion.List;
    private int _homeCrumbFocus;
    private IReadOnlyList<FileEntry>? _quickAccessSource;
    private IReadOnlyList<QuickAccessItem> _quickAccess = [];

    private static readonly Breadcrumb[] HomeTrail =
    [
        new("Locais", BreadcrumbKind.Root, null, null),
        new("Início", BreadcrumbKind.Folder, null, null, IsCurrent: true),
    ];

    /// <summary>Região com o foco na tela atual: conteúdo, caminho, acesso rápido ou abas.</summary>
    public PaneRegion FocusRegion => Screen == Screen.Home ? _homeRegion : ActivePane.Region;

    /// <summary>Segmento focado quando <see cref="FocusRegion"/> é o caminho (índice em <see cref="Breadcrumbs"/>).</summary>
    public int BreadcrumbFocus => Screen == Screen.Home ? _homeCrumbFocus : ActivePane.BreadcrumbFocus;

    /// <summary>Atalho focado quando <see cref="FocusRegion"/> é o acesso rápido.</summary>
    public int QuickAccessFocus { get; private set; }

    /// <summary>
    /// Favoritos, Recentes, as pastas do Windows que existem (as mesmas do início), Meu computador e a Lixeira.
    /// Vazio no seletor de pasta (lá, a raiz do caminho leva aos outros locais).
    /// </summary>
    public IReadOnlyList<QuickAccessItem> QuickAccess
    {
        get
        {
            if (Screen == Screen.FolderPicker) return [];
            if (!ReferenceEquals(_quickAccessSource, Places))
            {
                _quickAccessSource = Places;
                _quickAccess = BuildQuickAccess();
            }
            return _quickAccess;
        }
    }

    private List<QuickAccessItem> BuildQuickAccess()
    {
        var items = new List<QuickAccessItem> { new("Favoritos", QuickAccessKind.Favorites), new("Recentes", QuickAccessKind.Recents) };
        foreach (var place in Places)
        {
            if (place is not { Kind: EntryKind.KnownFolder, FullPath: { } path } || IsFavoriteEntry(place) || IsRecentPlace(place) || place.Id == RecycleBinLocation.PlaceId) continue;
            items.Add(new QuickAccessItem(place.Name, QuickAccessKind.Folder, path) { Place = place });
        }
        items.Add(new QuickAccessItem("Meu computador", QuickAccessKind.ThisPc));
        if (_recycleBin is not null)
            items.Add(new QuickAccessItem("Lixeira", QuickAccessKind.RecycleBin) { Place = Places.FirstOrDefault(p => p.Id == RecycleBinLocation.PlaceId) });
        return items;
    }

    /// <summary>O atalho leva ao que a aba ativa já mostra (destaque discreto na barra).</summary>
    public bool IsQuickAccessActive(QuickAccessItem item) => Screen == Screen.Browser && item.Kind switch
    {
        QuickAccessKind.Folder => Browser.Location is PhysicalLocation here && string.Equals(
            Path.TrimEndingDirectorySeparator(here.FullPath), Path.TrimEndingDirectorySeparator(item.Path ?? string.Empty), StringComparison.OrdinalIgnoreCase),
        QuickAccessKind.RecycleBin => Browser.Location is RecycleBinLocation,
        QuickAccessKind.ThisPc => Browser.Location is ThisPcLocation,
        _ => false,
    };

    /// <summary>Caminho mostrado na barra: raiz + segmentos reais (ou o nome do local virtual), nunca fixo.</summary>
    private IReadOnlyList<Breadcrumb> Trail(PaneState pane)
    {
        var path = BreadcrumbTrail.Collapse(BuildBreadcrumbs(pane));
        var root = new Breadcrumb(pane.Location is PhysicalLocation or ArchiveLocation ? "Meu computador" : "Locais", BreadcrumbKind.Root, null, null);
        if (pane.Location is ThisPcLocation) return [root, new Breadcrumb("Meu computador", BreadcrumbKind.Folder, null, null, IsCurrent: true)];
        if (path.Count > 0) return [root, .. path];
        var current = pane.Location is null ? "Carregando…" : TabTitle(pane);
        return [root, new Breadcrumb(current, BreadcrumbKind.Folder, null, null, IsCurrent: true)];
    }

    private void SetTopBar(PaneRegion region, int crumbFocus)
    {
        if (Screen == Screen.Home)
        {
            _homeRegion = region;
            _homeCrumbFocus = crumbFocus;
        }
        else
        {
            ActivePane.Region = region;
            ActivePane.BreadcrumbFocus = crumbFocus;
        }
    }

    /// <summary>Um alvo da barra superior: um segmento do caminho ou um atalho do acesso rápido.</summary>
    private readonly record struct TopBarTarget(PaneRegion Region, int Index);

    /// <summary>
    /// Alvos da barra, na ordem da tela. Só entra o que leva a outro lugar: a pasta atual (último segmento) é o rótulo
    /// do local, não uma ação; no início, "Locais › Início" já é aqui; o atalho do local atual também fica de fora.
    /// </summary>
    private List<TopBarTarget> TopBarTargets()
    {
        var targets = new List<TopBarTarget>();
        if (Screen != Screen.Home)
        {
            var crumbs = Breadcrumbs;
            for (var i = 0; i < crumbs.Count; i++)
                if (!crumbs[i].IsCurrent) targets.Add(new(PaneRegion.Breadcrumbs, i));
        }
        var quick = QuickAccess;
        for (var i = 0; i < quick.Count; i++)
            if (!IsQuickAccessActive(quick[i])) targets.Add(new(PaneRegion.QuickAccess, i));
        return targets;
    }

    /// <summary>O segmento leva a outro lugar (a pasta atual e o caminho do início não levam).</summary>
    public bool IsBreadcrumbTarget(int index) => Screen != Screen.Home && Breadcrumbs is { } crumbs && index >= 0 && index < crumbs.Count && !crumbs[index].IsCurrent;

    private void FocusTopBar(TopBarTarget target)
    {
        if (target.Region == PaneRegion.QuickAccess)
        {
            QuickAccessFocus = target.Index;
            SetTopBar(PaneRegion.QuickAccess, BreadcrumbFocus);
        }
        else SetTopBar(PaneRegion.Breadcrumbs, target.Index);
    }

    /// <summary>
    /// L1/R1 no conteúdo: a pasta atual fica entre o caminho e o acesso rápido, então L1 foca o alvo logo antes dela
    /// (a pasta de cima) e R1 o logo depois (o primeiro atalho). Sem alvo desse lado, o mais próximo do outro.
    /// </summary>
    private void EnterTopBar(bool forward)
    {
        var targets = TopBarTargets();
        if (targets.Count == 0) return;
        var crumbs = targets.Count(t => t.Region == PaneRegion.Breadcrumbs);
        var index = forward ? Math.Min(crumbs, targets.Count - 1) : Math.Max(crumbs - 1, 0);
        FocusTopBar(targets[index]);
    }

    /// <summary>Posição do foco entre os alvos; se ele ficou num item que não é alvo, o próximo alvo na ordem da tela.</summary>
    private int TopBarPosition(List<TopBarTarget> targets)
    {
        var current = FocusRegion == PaneRegion.QuickAccess ? new TopBarTarget(PaneRegion.QuickAccess, QuickAccessFocus) : new TopBarTarget(PaneRegion.Breadcrumbs, BreadcrumbFocus);
        var exact = targets.IndexOf(current);
        if (exact >= 0) return exact;
        static int Order(TopBarTarget t) => (t.Region == PaneRegion.QuickAccess ? 1 << 16 : 0) + t.Index;
        var next = targets.FindIndex(t => Order(t) > Order(current));
        return next >= 0 ? next : targets.Count - 1;
    }

    /// <summary>Com 2+ abas no navegador, a faixa de abas (acima da barra) recebe o foco com Cima.</summary>
    private bool CanFocusTabs => Screen == Screen.Browser && ActivePane.Mode == PaneMode.Browse && _tabs.Count > 1 && !FocusOnSecond;

    private void HandleTopBar(InputAction action)
    {
        var pane = Screen == Screen.Home ? null : ActivePane;
        var targets = TopBarTargets();
        if (targets.Count == 0 || pane is { IsLoading: true })
        {
            SetTopBar(PaneRegion.List, 0);
            return;
        }
        var at = TopBarPosition(targets);
        var target = targets[at];
        FocusTopBar(target);
        switch (action)
        {
            case InputAction.NavigateLeft:
            case InputAction.PreviousRegion:
                FocusTopBar(targets[Math.Max(0, at - 1)]);
                break;
            case InputAction.NavigateRight:
            case InputAction.NextRegion:
                FocusTopBar(targets[Math.Min(targets.Count - 1, at + 1)]);
                break;
            case InputAction.PageUp:
                FocusTopBar(targets.First(t => t.Region == target.Region));
                break;
            case InputAction.PageDown:
                FocusTopBar(targets.Last(t => t.Region == target.Region));
                break;
            case InputAction.NavigateUp:
                if (CanFocusTabs) ActivePane.Region = PaneRegion.Tabs;
                break;
            case InputAction.Confirm:
                if (target.Region == PaneRegion.QuickAccess) ActivateQuickAccess(QuickAccess[target.Index]);
                else if (pane is not null) ActivateBreadcrumb(pane, Breadcrumbs[target.Index]);
                break;
            case InputAction.NavigateDown:
            case InputAction.Back:
                SetTopBar(PaneRegion.List, BreadcrumbFocus);
                break;
            case InputAction.OpenContextMenu:
                if (target.Region == PaneRegion.Breadcrumbs && pane is not null) ShowPathMenu(pane);
                break;
            case InputAction.OpenAppMenu:
                SetTopBar(PaneRegion.List, BreadcrumbFocus);
                if (pane?.Mode == PaneMode.PickFolder) ShowPickerMenu();
                else ShowAppMenu();
                break;
        }
    }

    /// <summary>Raiz do caminho: "Locais" leva ao início e "Meu computador" às unidades; no seletor de pasta, aos outros locais.</summary>
    private void ActivateRoot(PaneState pane, Breadcrumb root)
    {
        pane.Region = PaneRegion.List;
        if (pane.Mode == PaneMode.PickFolder)
        {
            ShowPickerPlaces();
            return;
        }
        if (root.Label == "Meu computador") ShowThisPc();
        else GoHome();
    }

    private void ActivateQuickAccess(QuickAccessItem item)
    {
        switch (item.Kind)
        {
            case QuickAccessKind.Favorites:
                ShowFavoritesMenu(); // o foco continua no atalho: Voltar no menu devolve à barra
                break;
            case QuickAccessKind.Recents:
                if (Settings.RememberRecents) ShowRecents();
                else
                    PushModal(new MenuModal("Recentes",
                    [
                        new MenuItem("Lembrar pastas e arquivos abertos", ToggleRememberRecents, Detail: "Só neste computador. Também em Menu → Configurações → Recentes.", Icon: ActionIcon.Recent),
                    ]) { Icon = ActionIcon.Recent });
                break;
            case QuickAccessKind.Folder when item.Path is { } path:
                OpenFromTopBar(new PhysicalLocation(path));
                break;
            case QuickAccessKind.ThisPc:
                ShowThisPc();
                break;
            case QuickAccessKind.RecycleBin:
                OpenFromTopBar(RecycleBinLocation.Instance);
                break;
        }
    }

    /// <summary>No navegador, abre na aba atual guardando o histórico (Voltar retorna); no início, abre o navegador.</summary>
    private void OpenFromTopBar(Location target)
    {
        SetTopBar(PaneRegion.List, 0);
        if (Screen == Screen.Browser && IsSameLocation(Browser.Location, target)) return; // já é aqui: nada a recarregar
        if (Screen == Screen.Browser && Browser.Location is not null)
        {
            Track(NavigateAsync(Browser, target, pushHistory: true));
            return;
        }
        if (target is PhysicalLocation physical) OpenPhysical(physical.FullPath);
        else OpenVirtual(target);
    }

    private static bool IsSameLocation(Location? here, Location target) => (here, target) switch
    {
        (PhysicalLocation a, PhysicalLocation b) => string.Equals(Path.TrimEndingDirectorySeparator(a.FullPath), Path.TrimEndingDirectorySeparator(b.FullPath), StringComparison.OrdinalIgnoreCase),
        (RecycleBinLocation, RecycleBinLocation) => true,
        _ => false,
    };

    private void ShowFavoritesMenu()
    {
        var items = Settings.Favorites.Select(path => new MenuItem(FavoriteName(path), () => OpenFromTopBar(new PhysicalLocation(path)),
            SafeDirectoryExists(path) ? null : "A pasta não existe ou não está acessível agora.", path, Icon: ActionIcon.Folder)).ToList();
        if (items.Count == 0)
            items.Add(new MenuItem("Nenhum favorito ainda", null, "Adicione uma pasta pelo botão de ações (Norte) → Adicionar aos favoritos.", Icon: ActionIcon.Favorite));
        PushModal(new MenuModal("Favoritos", items) { Icon = ActionIcon.Favorite });
    }

    /// <summary>Mouse/toque num atalho: mesmo efeito de focar e confirmar.</summary>
    public void PointerActivateQuickAccess(int index)
    {
        if (TopModal is not null || index < 0 || index >= QuickAccess.Count) return;
        if (Screen != Screen.Home && ActivePane.IsLoading) return;
        if (IsQuickAccessActive(QuickAccess[index])) return; // o local atual: tocar não recarrega
        QuickAccessFocus = index;
        SetTopBar(PaneRegion.QuickAccess, BreadcrumbFocus);
        Handle(InputAction.Confirm);
    }

    /// <summary>Legendas com o foco na barra superior: L1/R1 andam pelos alvos; Cima leva às abas quando há mais de uma.</summary>
    private void AddTopBarHints(List<Hint> hints)
    {
        var targets = TopBarTargets();
        if (targets.Count == 0) return;
        var target = targets[TopBarPosition(targets)];
        if (target.Region == PaneRegion.QuickAccess)
        {
            var item = QuickAccess[target.Index];
            hints.Add(new(InputAction.Confirm, "Abrir"));
        }
        else
        {
            var crumb = Breadcrumbs[target.Index];
            hints.Add(new(InputAction.Confirm, crumb.Kind switch
            {
                BreadcrumbKind.Collapsed => "Mostrar pastas",
                BreadcrumbKind.Root when ActivePane.Mode == PaneMode.PickFolder => "Outros locais",
                _ => "Ir para",
            }));
            hints.Add(new(InputAction.OpenContextMenu, "Caminho completo"));
        }
        if (targets.Count > 1)
        {
            hints.Add(new(InputAction.PreviousRegion, "Anterior"));
            hints.Add(new(InputAction.NextRegion, "Próximo"));
        }
        if (CanFocusTabs) hints.Add(new(InputAction.NavigateUp, "Abas"));
        hints.Add(new(InputAction.Back, "Voltar à lista"));
    }

    /// <summary>Legendas de entrada na barra a partir do conteúdo (L1: caminho; R1: acesso rápido).</summary>
    private void AddTopBarEntryHints(List<Hint> hints)
    {
        var targets = TopBarTargets();
        if (targets.Count == 0) return;
        var hasCrumbs = targets[0].Region == PaneRegion.Breadcrumbs;
        var hasQuick = targets[^1].Region == PaneRegion.QuickAccess;
        hints.Add(new(InputAction.PreviousRegion, hasCrumbs ? "Caminho" : "Barra superior"));
        hints.Add(new(InputAction.NextRegion, hasQuick ? "Acesso rápido" : "Barra superior"));
    }
}
