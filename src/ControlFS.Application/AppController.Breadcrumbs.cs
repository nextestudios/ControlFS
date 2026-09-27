using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

public sealed partial class AppController
{
    /// <summary>
    /// Segmentos visíveis da barra superior: a raiz ("Locais"/"Meu computador") e o caminho real do painel ativo, com o
    /// meio recolhido em caminhos longos. No início: "Locais › Início".
    /// </summary>
    public IReadOnlyList<Breadcrumb> Breadcrumbs => Screen == Screen.Home ? HomeTrail : Trail(ActivePane);

    /// <summary>Caminho completo, da raiz até a pasta atual (a última é a atual).</summary>
    internal List<Breadcrumb> BuildBreadcrumbs(PaneState pane)
    {
        var crumbs = new List<Breadcrumb>();
        switch (pane.Location)
        {
            case PhysicalLocation physical:
                AddPhysicalChain(crumbs, physical.FullPath, childFocusOfLast: null);
                break;
            case ArchiveLocation archive:
                var folder = Path.GetDirectoryName(archive.ArchivePath);
                if (folder is not null) AddPhysicalChain(crumbs, folder, Path.GetFileName(archive.ArchivePath));
                var segments = archive.InnerPath.Length == 0 ? [] : archive.InnerPath.Split('/');
                crumbs.Add(new Breadcrumb(Path.GetFileName(archive.ArchivePath), BreadcrumbKind.Archive, archive with { InnerPath = string.Empty },
                    segments.Length > 0 ? ArchiveTree.IdPrefix + segments[0] + "/" : null));
                for (var i = 0; i < segments.Length; i++)
                {
                    var inner = string.Join('/', segments.Take(i + 1));
                    var child = i + 1 < segments.Length ? ArchiveTree.IdPrefix + inner + "/" + segments[i + 1] + "/" : null;
                    crumbs.Add(new Breadcrumb(segments[i], BreadcrumbKind.ArchiveFolder, archive with { InnerPath = inner }, child));
                }
                break;
        }
        if (crumbs.Count > 0) crumbs[^1] = crumbs[^1] with { IsCurrent = true };
        return crumbs;
    }

    private void AddPhysicalChain(List<Breadcrumb> crumbs, string path, string? childFocusOfLast)
    {
        var chain = new List<string>();
        for (string? current = path; current is not null && chain.Count < 256; current = _fs.GetParent(current)) chain.Add(current);
        chain.Reverse();
        for (var i = 0; i < chain.Count; i++)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(chain[i]));
            var label = name.Length > 0 ? name : chain[i];
            var child = i + 1 < chain.Count ? Path.GetFileName(Path.TrimEndingDirectorySeparator(chain[i + 1])) : childFocusOfLast;
            crumbs.Add(new Breadcrumb(label, BreadcrumbKind.Folder, new PhysicalLocation(chain[i]), child));
        }
    }

    /// <summary>LB leva à barra superior (foco na pasta de cima) e RB às abas; na barra, ver <see cref="HandleTopBar"/>.</summary>
    private bool HandleRegionSwitch(PaneState pane, InputAction action)
    {
        if (pane.Region == PaneRegion.Tabs)
        {
            HandleTabs(pane, action);
            return true;
        }
        if (pane.Region == PaneRegion.List)
        {
            if (action == InputAction.NextRegion && pane.Mode == PaneMode.Browse)
            {
                pane.Region = PaneRegion.Tabs;
                return true;
            }
            if (action != InputAction.PreviousRegion || pane.IsLoading) return false;
            var crumbs = Breadcrumbs;
            if (crumbs.Count < 2) return true;
            pane.Region = PaneRegion.Breadcrumbs;
            pane.BreadcrumbFocus = crumbs.Count - 2;
            return true;
        }
        HandleTopBar(action);
        return true;
    }

    private void ActivateBreadcrumb(PaneState pane, Breadcrumb crumb)
    {
        if (crumb.Kind == BreadcrumbKind.Collapsed)
        {
            ShowPathMenu(pane, crumb.Hidden);
            return;
        }
        if (crumb.Kind == BreadcrumbKind.Root)
        {
            ActivateRoot(pane, crumb);
            return;
        }
        pane.Region = PaneRegion.List;
        if (crumb.IsCurrent || crumb.Target is null) return;
        Track(NavigateAsync(pane, crumb.Target, pushHistory: true, focusId: crumb.ChildFocusId));
    }

    private static ActionIcon CrumbIcon(Breadcrumb crumb) => crumb.Kind switch
    {
        BreadcrumbKind.Archive => ActionIcon.Archive,
        _ when crumb.Target is ThisPcLocation => ActionIcon.ThisPc,
        _ when crumb.Target is PhysicalLocation { FullPath: { } path } && Path.GetPathRoot(path) is { } root && string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase) => ActionIcon.Drive,
        _ => ActionIcon.Folder,
    };

    /// <summary>Todas as pastas acima da atual, num menu (também é o caminho pelo menu do app e o conteúdo do "…").</summary>
    private void ShowPathMenu(PaneState pane, IReadOnlyList<Breadcrumb>? only = null)
    {
        var crumbs = (only ?? BuildBreadcrumbs(pane).Where(c => !c.IsCurrent).ToList()).ToList();
        if (crumbs.Count == 0) return;
        var items = crumbs.Select(c => new MenuItem(c.Label, () => ActivateBreadcrumb(pane, c),
            Detail: c.Kind switch
            {
                BreadcrumbKind.Archive => "Compactado (raiz)",
                BreadcrumbKind.ArchiveFolder => "Dentro do compactado",
                _ => c.Target?.DisplayPath,
            }, Icon: CrumbIcon(c))).ToList();
        PushModal(new MenuModal("Ir para", items) { Icon = ActionIcon.FolderUp, FocusIndex = items.Count - 1 });
    }

    /// <summary>Mouse/toque num segmento: mesmo efeito de focar e confirmar.</summary>
    public void PointerActivateBreadcrumb(int index)
    {
        if (TopModal is not null) return;
        var crumbs = Breadcrumbs;
        if (index < 0 || index >= crumbs.Count) return;
        SetTopBar(PaneRegion.Breadcrumbs, index);
        Handle(InputAction.Confirm);
    }
}
