using ControlFS.Application.State;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>O que o Narrador deve dizer: onde o foco lógico está (<see cref="Context"/>) e o item focado nele.</summary>
public sealed record FocusAnnouncement(string Context, string Item);

/// <summary>
/// Leitores de tela: o foco do XAML fica na raiz da janela e o foco lógico vive aqui, então a janela pede, a cada
/// mudança, o texto a anunciar. Estados (marcado, recortado, bloqueado, indisponível) sempre vão por extenso — nada
/// depende só de cor, som ou vibração.
/// </summary>
public sealed partial class AppController
{
    private FocusAnnouncement? _lastAnnouncement;

    /// <summary>Descrição do foco lógico atual (tela, modal, lista, barra de caminho ou abas).</summary>
    public FocusAnnouncement DescribeFocus()
    {
        switch (TopModal)
        {
            case MenuModal menu:
                if (menu.Items.Count == 0) return new($"Menu {menu.Title}", "sem opções");
                var item = menu.Items[Math.Clamp(menu.FocusIndex, 0, menu.Items.Count - 1)];
                var state = (item.IsDestructive ? ", ação perigosa" : string.Empty) + (item.IsEnabled ? string.Empty : ", indisponível" + (item.DisabledReason is { } why ? ": " + why : string.Empty));
                var detail = item.Detail is { Length: > 0 } d ? ", " + d : string.Empty;
                // Grade: "ação rápida 2 de 8" (Configurações: "bloco 2 de 8" do grupo); lista: a posição entre os itens da
                // lista (os blocos contam à parte). O rótulo por extenso já diz o valor de um ajuste ("Itens ocultos: escondidos").
                var grid = menu.GridOf(menu.FocusIndex);
                var listBefore = Enumerable.Range(0, menu.FocusIndex).Count(i => !menu.IsQuick(i));
                var spot = grid is not null ? (menu.HasSectionGrids ? "bloco " : "ação rápida ") + Position(menu.FocusIndex - grid.Start, grid.Count)
                    : Position(listBefore, menu.Items.Count - menu.QuickCount);
                return new($"Menu {menu.Title}", $"{item.Label}{state}{detail}, {spot}");
            case DialogModal dialog:
                var body = string.Join(" ", dialog.Lines.Select(l => $"{l.Label}: {l.Value}.").Append(dialog.Message ?? string.Empty).Where(t => t.Length > 0));
                var context = Sentence($"Diálogo {dialog.Title}", body);
                if (dialog.Options.Count == 0) return new(context, string.Empty);
                var option = dialog.Options[Math.Clamp(dialog.FocusIndex, 0, dialog.Options.Count - 1)];
                var toggle = option.Kind == DialogOptionKind.Toggle ? (option.IsChecked ? ", marcado" : ", desmarcado") : option.IsDestructive ? ", ação perigosa" : string.Empty;
                return new(context, $"{option.Label}{toggle}, botão {Position(dialog.FocusIndex, dialog.Options.Count)}");
            case KeyboardModal keyboard:
                var kb = keyboard.Keyboard;
                if (kb.FocusedSuggestion is { } suggestion)
                    return new($"Teclado virtual: {kb.Title}", $"sugestão {suggestion}, {Position(kb.SuggestionIndex ?? 0, kb.Suggestions.Count)}");
                var key = kb.FocusedKey;
                return new($"Teclado virtual: {kb.Title}", "tecla " + key.Name + (kb.IsKeyEnabled(key) ? string.Empty : ", indisponível neste campo"));
            case OnboardingModal onboarding:
                var chosen = onboarding.FocusedOption;
                return new(Sentence($"Boas-vindas, passo {onboarding.StepIndex + 1} de {onboarding.StepCount}: {onboarding.StepTitle}", onboarding.StepBody),
                    chosen is null ? string.Empty : $"{chosen.Label}{(chosen.Detail is { Length: > 0 } more ? ", " + more : string.Empty)}, {Position(onboarding.FocusIndex, onboarding.Options.Count)}");
            case MappingWizardModal wizard:
                return new(wizard.Title, wizard.Wizard.Phase == Core.Input.Mapping.MappingPhase.Review
                    ? MappingWizardModal.ReviewOptions[wizard.ReviewFocus]
                    : wizard.Wizard.Current is { } target ? "Pressione: " + target.Label : string.Empty);
            case { } other:
                return new(other.Title, string.Empty);
        }

        var quick = QuickAccess;
        if (FocusRegion == PaneRegion.QuickAccess && quick.Count > 0)
        {
            var index = Math.Clamp(QuickAccessFocus, 0, quick.Count - 1);
            var item = quick[index];
            return new("Acesso rápido", $"{item.Label}{(IsQuickAccessActive(item) ? ", local atual" : string.Empty)}, {Position(index, quick.Count)}");
        }
        if (FocusRegion == PaneRegion.Breadcrumbs && Breadcrumbs is { Count: > 0 } trail)
        {
            var index = Math.Clamp(BreadcrumbFocus, 0, trail.Count - 1);
            var crumb = trail[index];
            var label = crumb.Kind == BreadcrumbKind.Collapsed ? $"{crumb.Hidden.Count} pastas recolhidas" : crumb.Label;
            return new("Barra de caminho", $"{label}{(crumb.IsCurrent ? ", pasta atual" : string.Empty)}, {Position(index, trail.Count)}");
        }

        if (Screen == Screen.Home)
        {
            if (Places.Count == 0) return new("Início", "nenhum local");
            var place = Places[Math.Clamp(PlacesFocus, 0, Places.Count - 1)];
            var (primary, secondary) = DescribePlace(place);
            var about = place.IsBlocked ? "bloqueado: " + place.BlockedReason
                : IsGrid ? primary + (secondary is null ? string.Empty : ", " + secondary)
                : place.Detail ?? EntryText.TypeName(place);
            return new("Início", $"{place.Name}, {about}, {Position(PlacesFocus, Places.Count)}");
        }

        var pane = ActivePane;
        if (pane.Region == PaneRegion.Tabs)
        {
            var tabs = Tabs;
            return new("Abas", $"Aba {ActiveTab + 1} de {tabs.Count}: {TabLabel(ActiveTab)}");
        }

        // Dois painéis: o contexto diz qual está ativo (trocar de painel com a mesma pasta nos dois também é anunciado).
        var side = Screen != Screen.FolderPicker && DualPaneActive ? PaneName(pane) + ", " : string.Empty;
        var where = side + (Screen == Screen.FolderPicker ? "Escolher pasta: " : string.Empty) + pane.Location switch
        {
            null => "Carregando",
            ArchiveLocation archive => "Compactado " + Path.GetFileName(archive.ArchivePath) + (archive.InnerPath.Length > 0 ? ", " + archive.InnerPath : string.Empty),
            SearchLocation => "Resultados da busca",
            RecycleBinLocation => "Lixeira",
            ThisPcLocation => "Meu computador",
            PhysicalLocation physical => "Pasta " + physical.FullPath,
            { } location => location.DisplayPath,
        };
        var list = pane.List;
        if (pane.IsLoading) return new(where, "carregando");
        if (list.Focused is not { } entry) return new(where, pane.ActiveSearch is { IsRunning: true } ? "buscando" : "vazia");
        return new(where, $"{DescribeEntry(entry)}, {Position(list.FocusIndex, list.Items.Count)}");
    }

    /// <summary>Nome, tipo, tamanho e estados por extenso.</summary>
    private string DescribeEntry(FileEntry entry)
    {
        if (entry.IsBlocked) return $"{entry.Name}, bloqueado: {entry.BlockedReason}";
        var parts = new List<string> { EntryText.DisplayName(entry), EntryText.TypeName(entry).ToLowerInvariant() };
        if (entry.Size is long size && !entry.IsContainer) parts.Add(EntryText.Size(size));
        if (entry.FoundIn is { } folder) parts.Add("em " + folder);
        if (ActivePane.List.IsSelected(entry)) parts.Add("marcado");
        if (IsCut(entry)) parts.Add("recortado");
        if (GitState(entry) is { } git) parts.Add(git.ToLowerInvariant());
        if (entry.IsEncrypted) parts.Add("com senha");
        if (entry.IsHidden) parts.Add("oculto");
        return string.Join(", ", parts);
    }

    private static string Position(int index, int count) => $"{Math.Clamp(index, 0, Math.Max(0, count - 1)) + 1} de {count}";

    /// <summary>
    /// Texto novo para o Narrador desde a última chamada, ou <c>null</c> se nada mudou: o contexto inteiro quando ele
    /// muda (abriu um menu, entrou numa pasta), só o item quando o foco anda dentro do mesmo contexto.
    /// </summary>
    public string? TakeAnnouncement()
    {
        var now = DescribeFocus();
        var previous = _lastAnnouncement;
        _lastAnnouncement = now;
        var tutorial = TakeTutorialAnnouncement(); // passo novo do tutorial (#231) vem antes do foco
        string? focus = previous == now ? null
            : previous is null || previous.Context != now.Context ? Sentence(now.Context, now.Item)
            : now.Item;
        return tutorial is null ? focus : focus is null ? tutorial : Sentence(tutorial, focus);
    }

    /// <summary>Junta duas frases sem pontuação dobrada ("Sair?" + "Cancelar" → "Sair? Cancelar").</summary>
    private static string Sentence(string first, string second) =>
        second.Length == 0 ? first : first.Length > 0 && first[^1] is '.' or '?' or '!' or ':' ? $"{first} {second}" : $"{first}. {second}";
}
