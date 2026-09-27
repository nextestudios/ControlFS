using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Pastas e arquivos recentes: gravados só em <see cref="Core.Contracts.AppSettings"/> (neste computador, nunca enviados),
/// limitados a <see cref="MaxRecents"/> cada, mostrados no início como "Recentes". Podem ser limpos e desligados.
/// </summary>
public sealed partial class AppController
{
    internal const int MaxRecents = 10;
    /// <summary>Id do local "Recentes" entre os locais do início.</summary>
    public const string RecentPlaceId = "recent:";

    public IReadOnlyList<string> RecentFolders => Settings.RecentFolders;
    public IReadOnlyList<string> RecentFiles => Settings.RecentFiles;

    private IEnumerable<FileEntry> RecentPlace()
    {
        if (!Settings.RememberRecents || Settings.RecentFolders.Count + Settings.RecentFiles.Count == 0) yield break;
        yield return new FileEntry(RecentPlaceId, "Recentes", EntryKind.KnownFolder,
            Detail: $"{Plural.Of(Settings.RecentFolders.Count, "pasta", "pastas")} · {Plural.Of(Settings.RecentFiles.Count, "arquivo", "arquivos")} abertos recentemente");
    }

    private static bool IsRecentPlace(FileEntry entry) => entry.Id == RecentPlaceId;

    /// <summary>Coloca <paramref name="path"/> no topo da lista (sem repetir) e corta o excesso.</summary>
    private static IReadOnlyList<string> PushRecent(IReadOnlyList<string> list, string path)
    {
        var key = Path.TrimEndingDirectorySeparator(path);
        if (list.Count > 0 && string.Equals(Path.TrimEndingDirectorySeparator(list[0]), key, StringComparison.OrdinalIgnoreCase)) return list;
        return [path, .. list.Where(p => !string.Equals(Path.TrimEndingDirectorySeparator(p), key, StringComparison.OrdinalIgnoreCase)).Take(MaxRecents - 1)];
    }

    /// <summary>Pasta aberta no navegador: vira a última localização e, se lembrar estiver ligado, a pasta recente mais nova.</summary>
    private void RecordVisit(string folder)
    {
        var recents = Settings.RememberRecents ? PushRecent(Settings.RecentFolders, folder) : Settings.RecentFolders;
        if (Settings.LastLocation == folder && ReferenceEquals(recents, Settings.RecentFolders)) return;
        UpdateSettings(s => s with { LastLocation = folder, RecentFolders = recents });
    }

    private void RecordRecentFile(string path)
    {
        if (!Settings.RememberRecents) return;
        var updated = PushRecent(Settings.RecentFiles, path);
        if (!ReferenceEquals(updated, Settings.RecentFiles)) UpdateSettings(s => s with { RecentFiles = updated });
    }

    internal void ClearRecents()
    {
        UpdateSettings(s => s with { RecentFolders = [], RecentFiles = [] });
        RefreshPlaces();
        StatusMessage = "Recentes apagados.";
    }

    internal void ToggleRememberRecents()
    {
        var remember = !Settings.RememberRecents;
        // Desligar também apaga o que já estava guardado: nada fica registrado.
        UpdateSettings(s => remember ? s with { RememberRecents = true } : s with { RememberRecents = false, RecentFolders = [], RecentFiles = [] });
        RefreshPlaces();
        StatusMessage = remember ? "Recentes: lembrando pastas e arquivos abertos." : "Recentes desligados e apagados.";
    }

    private void ShowRecents()
    {
        var items = new List<MenuItem>();
        foreach (var folder in Settings.RecentFolders)
        {
            var available = SafeDirectoryExists(folder);
            items.Add(new MenuItem(FavoriteName(folder), () => OpenPhysical(folder),
                available ? null : "A pasta não existe ou não está acessível agora.", "Pasta · " + folder, Icon: ActionIcon.Folder));
        }
        foreach (var file in Settings.RecentFiles)
        {
            var folder = Path.GetDirectoryName(file);
            var available = folder is not null && SafeDirectoryExists(folder);
            items.Add(new MenuItem(Path.GetFileName(file), () => Track(OpenRecentFileAsync(file)),
                available ? null : "A pasta do arquivo não existe ou não está acessível agora.", "Arquivo · " + file, Icon: ActionIcon.File));
        }
        items.Add(new MenuItem("Limpar recentes", ClearRecents, Detail: "Apaga as listas deste computador.", Icon: ActionIcon.Erase));
        PushModal(new MenuModal("Recentes", items) { Icon = ActionIcon.Recent });
    }

    /// <summary>Abre a pasta do arquivo com o foco nele e o abre como Confirmar faria (compactado explora, executável pede confirmação).</summary>
    private async Task OpenRecentFileAsync(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        Browser.Back.Clear();
        Browser.Forward.Clear();
        Browser.Location = null;
        Screen = Screen.Browser;
        await NavigateAsync(Browser, new PhysicalLocation(folder), pushHistory: false, focusId: name);
        if (Browser.Location is not PhysicalLocation) return;
        if (Browser.List.Focused is { Kind: EntryKind.File, IsBlocked: false } entry && string.Equals(entry.Id, name, StringComparison.OrdinalIgnoreCase))
            OpenEntry(Browser, entry);
        else
            SetStatus($"\"{name}\" não está mais nesta pasta.");
    }
}
