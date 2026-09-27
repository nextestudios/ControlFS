using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Barra superior compartilhada (início e navegador): à esquerda o caminho (raiz "Locais"/"Meu computador" e os
/// segmentos), à direita o acesso rápido. LB entra (no navegador, na pasta de cima; no início, no primeiro atalho);
/// esquerda/direita andam pela barra inteira, do caminho para o acesso rápido; Sul abre; baixo, Leste, LB e RB voltam
/// ao conteúdo. As abas continuam no RB.
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
    /// Favoritos, Arquivos recentes, as pastas do Windows que existem (as mesmas do início), Meu computador e a Lixeira.
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
        var items = new List<QuickAccessItem> { new("Favoritos", QuickAccessKind.Favorites), new("Arquivos recentes", QuickAccessKind.Recents) };
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

    /// <summary>LB no início: o foco vai para o primeiro atalho (Favoritos).</summary>
    private void EnterHomeTopBar()
    {
        if (QuickAccess.Count > 0)
        {
            QuickAccessFocus = 0;
            SetTopBar(PaneRegion.QuickAccess, 0);
        }
        else SetTopBar(PaneRegion.Breadcrumbs, 0);
    }

    private void HandleTopBar(InputAction action)
    {
        var pane = Screen == Screen.Home ? null : ActivePane;
        var crumbs = Breadcrumbs;
        if (crumbs.Count == 0 || pane is { IsLoading: true })
        {
            SetTopBar(PaneRegion.List, 0);
            return;
        }
        var quick = QuickAccess;
        var inQuick = FocusRegion == PaneRegion.QuickAccess && quick.Count > 0;
        var crumb = Math.Clamp(BreadcrumbFocus, 0, crumbs.Count - 1);
        var q = Math.Clamp(QuickAccessFocus, 0, Math.Max(0, quick.Count - 1));
        switch (action)
        {
            case InputAction.NavigateLeft:
                if (!inQuick) SetTopBar(PaneRegion.Breadcrumbs, Math.Max(0, crumb - 1));
                else if (q > 0) QuickAccessFocus = q - 1;
                else SetTopBar(PaneRegion.Breadcrumbs, crumbs.Count - 1);
                break;
            case InputAction.NavigateRight:
                if (inQuick) QuickAccessFocus = Math.Min(quick.Count - 1, q + 1);
                else if (crumb < crumbs.Count - 1) SetTopBar(PaneRegion.Breadcrumbs, crumb + 1);
                else if (quick.Count > 0)
                {
                    QuickAccessFocus = 0;
                    SetTopBar(PaneRegion.QuickAccess, crumb);
                }
                break;
            case InputAction.PageUp:
                if (inQuick) QuickAccessFocus = 0;
                else SetTopBar(PaneRegion.Breadcrumbs, 0);
                break;
            case InputAction.PageDown:
                if (inQuick) QuickAccessFocus = quick.Count - 1;
                else SetTopBar(PaneRegion.Breadcrumbs, crumbs.Count - 1);
                break;
            case InputAction.Confirm:
                if (inQuick) ActivateQuickAccess(quick[q]);
                else if (pane is null) SetTopBar(PaneRegion.List, crumb); // início: "Locais" e "Início" já são aqui
                else ActivateBreadcrumb(pane, crumbs[crumb]);
                break;
            case InputAction.NextRegion:
            case InputAction.PreviousRegion:
            case InputAction.NavigateDown:
            case InputAction.Back:
                SetTopBar(PaneRegion.List, crumb);
                break;
            case InputAction.OpenContextMenu:
                if (!inQuick && pane is not null) ShowPathMenu(pane);
                break;
            case InputAction.OpenAppMenu:
                SetTopBar(PaneRegion.List, crumb);
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
                    PushModal(new MenuModal("Arquivos recentes",
                    [
                        new MenuItem("Lembrar pastas e arquivos abertos", ToggleRememberRecents, Detail: "Só neste computador. Também em Menu → Recentes."),
                    ]));
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
        if (Screen == Screen.Browser && Browser.Location is not null)
        {
            Track(NavigateAsync(Browser, target, pushHistory: true));
            return;
        }
        if (target is PhysicalLocation physical) OpenPhysical(physical.FullPath);
        else OpenVirtual(target);
    }

    private void ShowFavoritesMenu()
    {
        var items = Settings.Favorites.Select(path => new MenuItem(FavoriteName(path), () => OpenFromTopBar(new PhysicalLocation(path)),
            SafeDirectoryExists(path) ? null : "A pasta não existe ou não está acessível agora.", path)).ToList();
        if (items.Count == 0)
            items.Add(new MenuItem("Nenhum favorito ainda", null, "Adicione uma pasta pelo botão de ações (Norte) → Adicionar aos favoritos."));
        PushModal(new MenuModal("Favoritos", items));
    }

    /// <summary>Mouse/toque num atalho: mesmo efeito de focar e confirmar.</summary>
    public void PointerActivateQuickAccess(int index)
    {
        if (TopModal is not null || index < 0 || index >= QuickAccess.Count) return;
        if (Screen != Screen.Home && ActivePane.IsLoading) return;
        QuickAccessFocus = index;
        SetTopBar(PaneRegion.QuickAccess, BreadcrumbFocus);
        Handle(InputAction.Confirm);
    }

    /// <summary>Legendas com o foco na barra superior.</summary>
    private void AddTopBarHints(List<Hint> hints)
    {
        var quick = QuickAccess;
        if (FocusRegion == PaneRegion.QuickAccess && quick.Count > 0)
        {
            var item = quick[Math.Clamp(QuickAccessFocus, 0, quick.Count - 1)];
            hints.Add(new(InputAction.Confirm, item.Kind is QuickAccessKind.Favorites or QuickAccessKind.Recents ? "Mostrar" : "Abrir"));
        }
        else if (Screen != Screen.Home && Breadcrumbs is { Count: > 0 } crumbs)
        {
            var crumb = crumbs[Math.Clamp(BreadcrumbFocus, 0, crumbs.Count - 1)];
            hints.Add(new(InputAction.Confirm, crumb.Kind switch
            {
                BreadcrumbKind.Collapsed => "Mostrar pastas",
                BreadcrumbKind.Root when ActivePane.Mode == PaneMode.PickFolder => "Outros locais",
                _ when crumb.IsCurrent => "Voltar à lista",
                _ => "Ir para",
            }));
            hints.Add(new(InputAction.OpenContextMenu, "Caminho completo"));
        }
        hints.Add(new(InputAction.Back, "Voltar à lista"));
    }
}
