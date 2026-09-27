using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Pastas favoritas: persistidas em <see cref="Core.Contracts.AppSettings.Favorites"/>, mostradas primeiro no
/// início e no seletor de pasta. Uma favorita ausente aparece como indisponível; nunca é removida sozinha.
/// </summary>
public sealed partial class AppController
{
    internal const string FavoriteIdPrefix = "fav:";

    public IReadOnlyList<string> Favorites => Settings.Favorites;

    public bool IsFavorite(string path) => IndexOfFavorite(path) >= 0;

    private int IndexOfFavorite(string path)
    {
        var key = NormalizeFavorite(path);
        for (var i = 0; i < Settings.Favorites.Count; i++)
            if (string.Equals(NormalizeFavorite(Settings.Favorites[i]), key, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string NormalizeFavorite(string path) => Path.TrimEndingDirectorySeparator(path);

    internal static bool IsFavoriteEntry(FileEntry entry) => entry.Id.StartsWith(FavoriteIdPrefix, StringComparison.Ordinal);

    /// <summary>Favoritas primeiro, depois "Recentes" (quando há) e os locais do sistema.</summary>
    private IReadOnlyList<FileEntry> BuildPlaces() => [.. FavoriteEntries(), .. RecentPlace(), .. _fs.GetPlaces(), .. RecycleBinPlace()];

    private List<FileEntry> FavoriteEntries()
    {
        var entries = new List<FileEntry>();
        foreach (var path in Settings.Favorites)
        {
            var name = FavoriteName(path);
            var available = SafeDirectoryExists(path);
            entries.Add(new FileEntry(FavoriteIdPrefix + path, name, EntryKind.KnownFolder, FullPath: path,
                Detail: available ? "★ Favorito · " + path : null,
                BlockedReason: available ? null : $"favorito indisponível ({path})"));
        }
        return entries;
    }

    /// <param name="waitForNetwork">
    /// Falso (padrão, thread de UI): um local de rede é dado como disponível sem conferir, porque um servidor desligado
    /// pode levar dezenas de segundos para responder (#27); abri-lo diz se ele está lá. Verdadeiro só fora da thread de UI.
    /// </param>
    private bool SafeDirectoryExists(string path, bool waitForNetwork = false)
    {
        try { return !waitForNetwork && _fs.IsNetworkPath(path) || _fs.DirectoryExists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static string FavoriteName(string path)
    {
        var trimmed = NormalizeFavorite(path);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    internal void AddFavorite(string path)
    {
        if (IsFavorite(path)) return;
        UpdateSettings(s => s with { Favorites = [.. s.Favorites, path] });
        RefreshPlaces();
        StatusMessage = $"\"{FavoriteName(path)}\" adicionada aos favoritos.";
    }

    internal void RemoveFavorite(string path)
    {
        var index = IndexOfFavorite(path);
        if (index < 0) return;
        UpdateSettings(s => s with { Favorites = [.. s.Favorites.Where((_, i) => i != index)] });
        RefreshPlaces();
        StatusMessage = $"\"{FavoriteName(path)}\" removida dos favoritos.";
    }

    /// <summary>Move a favorita uma posição (−1 para cima, +1 para baixo) e mantém o foco nela no início.</summary>
    internal void MoveFavorite(string path, int delta)
    {
        var index = IndexOfFavorite(path);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Settings.Favorites.Count) return;
        var list = Settings.Favorites.ToList();
        (list[index], list[target]) = (list[target], list[index]);
        UpdateSettings(s => s with { Favorites = list });
        RefreshPlaces();
        if (Screen == Screen.Home) PlacesFocus = target;
    }

    private void RefreshPlaces()
    {
        Places = BuildPlaces();
        PlacesFocus = Math.Clamp(PlacesFocus, 0, Math.Max(0, Places.Count - 1));
    }

    /// <summary>
    /// Unidades mudaram (pendrive conectado/removido, unidade de rede mapeada): refaz os locais sem reiniciar. O foco fica
    /// no mesmo local quando ele continua na lista; favoritos numa unidade que voltou ficam disponíveis de novo.
    /// </summary>
    public void RefreshDrives()
    {
        var focusedId = PlacesFocus >= 0 && PlacesFocus < Places.Count ? Places[PlacesFocus].Id : null;
        Places = BuildPlaces();
        var index = focusedId is null ? -1 : IndexOfPlace(focusedId);
        PlacesFocus = index >= 0 ? index : Math.Clamp(PlacesFocus, 0, Math.Max(0, Places.Count - 1));
        // Abas em Meu computador: a lista de unidades acompanha (o foco fica na mesma unidade, se ela continua lá).
        foreach (var tab in _tabs)
            if (tab.Location is ThisPcLocation && !tab.IsLoading) Refresh(tab);
        RaiseChanged();
    }

    private int IndexOfPlace(string id)
    {
        for (var i = 0; i < Places.Count; i++)
            if (Places[i].Id == id) return i;
        return -1;
    }

    /// <summary>Item de menu que adiciona ou remove <paramref name="path"/> dos favoritos.</summary>
    private MenuItem FavoriteToggleItem(string path, string? label = null, string? section = null) => IsFavorite(path)
        ? new MenuItem(label is null ? "Remover dos favoritos" : $"Remover {label} dos favoritos", () => RemoveFavorite(path), Icon: ActionIcon.Unfavorite, Section: section)
        : new MenuItem(label is null ? "Adicionar aos favoritos" : $"Adicionar {label} aos favoritos", () => AddFavorite(path), Icon: ActionIcon.Favorite, Section: section);

    // ---------- Início ----------

    private void ShowHomeMenu(string? image = null, bool imageChecked = false)
    {
        if (PlacesFocus >= 0 && PlacesFocus < Places.Count && IsRecentPlace(Places[PlacesFocus]))
        {
            PushModal(new MenuModal("Recentes",
            [
                new("Abrir", ShowRecents, Icon: ActionIcon.Recent),
                new("Limpar recentes", ClearRecents, Detail: "Apaga as listas deste computador.", Icon: ActionIcon.Erase),
                new("Desligar recentes", ToggleRememberRecents, Detail: "Para de lembrar e apaga as listas. Religue em Menu → Configurações.", Icon: ActionIcon.Erase),
            ]) { Icon = ActionIcon.Recent });
            return;
        }
        if (PlacesFocus < 0 || PlacesFocus >= Places.Count || Places[PlacesFocus] is not { FullPath: { } path } place) return;
        if (!imageChecked && place.Kind == EntryKind.Drive && DeferForImage(path, found => ShowHomeMenu(found, imageChecked: true))) return;
        var items = new List<MenuItem>
        {
            new("Abrir", () => OpenPlace(place), place.IsBlocked ? "Pasta indisponível no momento." : null, Icon: ActionIcon.OpenFolder),
            FavoriteToggleItem(path),
        };
        if (IsFavoriteEntry(place))
        {
            var index = IndexOfFavorite(path);
            items.Add(new MenuItem("Mover favorito para cima", () => MoveFavorite(path, -1), index <= 0 ? "Já é o primeiro favorito." : null, Icon: ActionIcon.MoveUp));
            items.Add(new MenuItem("Mover favorito para baixo", () => MoveFavorite(path, 1),
                index >= Settings.Favorites.Count - 1 ? "Já é o último favorito." : null, Icon: ActionIcon.MoveDown));
        }
        if (image is not null) items.Add(UnmountItem(path, image));
        PushModal(new MenuModal(place.Name, items) { Icon = PlaceIcon(place), Subtitle = place.Detail });
    }

    private void OpenPlace(FileEntry place)
    {
        if (IsRecentPlace(place))
        {
            ShowRecents();
            return;
        }
        if (place.Id == RecycleBinLocation.PlaceId)
        {
            OpenRecycleBin();
            return;
        }
        if (place.FullPath is not { } path) return;
        if (place.IsBlocked)
        {
            ShowUnavailableFavorite(path);
            return;
        }
        OpenPhysical(path);
    }

    private void ShowUnavailableFavorite(string path)
    {
        var dialog = new DialogModal("Favorito indisponível", [("Pasta", path)])
        {
            Message = "A pasta não existe ou não está acessível agora (unidade desconectada?). O favorito foi mantido.",
        };
        var keep = new DialogOption("Manter", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Favorite);
        dialog.Options.Add(keep);
        dialog.Options.Add(new DialogOption("Remover dos favoritos", DialogOptionKind.Danger, () =>
        {
            CloseModal(dialog);
            RemoveFavorite(path);
        }, icon: ActionIcon.Unfavorite));
        dialog.BackOption = keep;
        PushModal(dialog);
    }
}
