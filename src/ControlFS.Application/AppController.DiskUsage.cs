using System.Globalization;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Analisar uso do disco (#72): Norte numa pasta ou unidade → "Analisar uso do disco". A árvore é lida uma vez fora da
/// thread de UI (mesmo percurso do tamanho de pasta, #55: junções e links nunca são seguidos); Voltar cancela na hora.
/// O resultado é uma lista em ordem de tamanho: Sul numa pasta desce nela (sem ler o disco de novo), Voltar sobe, e um
/// arquivo abre a pasta dele no navegador com o foco nele.
/// </summary>
public sealed partial class AppController
{
    /// <summary>Subpastas listadas por nível (as maiores); o resto entra só nos totais.</summary>
    internal const int DiskUsageMaxFolders = 200;

    private readonly List<MenuModal> _usageMenus = [];

    private MenuItem DiskUsageItem(string path, string? section = null) => new("Analisar uso do disco", () => BeginDiskUsage(path),
        Detail: "Pastas e arquivos que mais ocupam espaço, em ordem de tamanho. Junções e links não são seguidos.", Icon: ActionIcon.Properties, Section: section);

    internal void BeginDiskUsage(string path)
    {
        var cts = new CancellationTokenSource();
        var dialog = new DialogModal("Analisando uso do disco", [("Pasta", path), ("Lido até agora", "Começando…")]) { Icon = ActionIcon.Properties };
        var cancel = new DialogOption("Cancelar análise", DialogOptionKind.Safe, cts.Cancel, icon: ActionIcon.Cancel);
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        PushModal(dialog);
        Track(AnalyzeDiskUsageAsync(path, dialog, cts));
    }

    private async Task AnalyzeDiskUsageAsync(string path, DialogModal dialog, CancellationTokenSource cts)
    {
        var finished = false;
        var progress = new Progress<FolderSize>(partial =>
        {
            if (finished) return;
            dialog.Lines = [("Pasta", path), ("Lido até agora", $"{FormatBytes(partial.Bytes)} em {Plural.Of(partial.Files, "arquivo", "arquivos")}, {Plural.Of(partial.Folders, "pasta", "pastas")}")];
            RaiseChanged();
        });
        DiskUsage? usage = null;
        string? error = null;
        try
        {
            usage = await Task.Run(() => _fs.AnalyzeDiskUsage(path, progress, cts.Token), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (FileOperationException ex)
        {
            error = ex.Message;
        }
        finally
        {
            finished = true;
            cts.Dispose();
        }
        CloseModal(dialog);
        if (usage is not null) ShowDiskUsage(usage, usage.Root);
        else if (error is not null) ShowMessage("Não foi possível analisar", [("Pasta", path)], error, icon: ActionIcon.Error);
        else SetStatus("Análise do uso do disco cancelada.");
    }

    private static string Percent(long part, long total) =>
        total <= 0 ? "0%" : (part / (double)total).ToString(part * 1000 < total ? "0.#%" : "0%", CultureInfo.CurrentCulture);

    /// <summary>Uma pasta da análise: subpastas e arquivos, cada grupo do maior para o menor.</summary>
    private void ShowDiskUsage(DiskUsage usage, DiskUsageNode node)
    {
        var items = new List<MenuItem>
        {
            new("Abrir esta pasta", () => OpenUsageLocation(node.FullPath, null), Icon: ActionIcon.OpenFolder, Section: "Pasta"),
        };
        foreach (var folder in node.Subfolders.Take(DiskUsageMaxFolders))
            items.Add(new MenuItem($"{folder.Name} — {FormatBytes(folder.Bytes)} ({Percent(folder.Bytes, node.Bytes)})", () => ShowDiskUsage(usage, folder),
                Detail: $"{Plural.Of(folder.Files, "arquivo", "arquivos")}, {Plural.Of(folder.Folders, "pasta", "pastas")}. Sul mostra o que há dentro.",
                Icon: ActionIcon.Folder, Section: "Pastas maiores", KeepOpen: true));
        if (node.Subfolders.Count > DiskUsageMaxFolders)
        {
            var rest = node.Subfolders.Skip(DiskUsageMaxFolders).ToList();
            items.Add(new MenuItem($"Outras {rest.Count} pastas — {FormatBytes(rest.Sum(r => r.Bytes))}", null, "Pastas menores, só nos totais.", Icon: ActionIcon.Folder, Section: "Pastas maiores"));
        }
        foreach (var file in node.LargestFiles)
            items.Add(new MenuItem($"{file.Name} — {FormatBytes(file.Bytes)} ({Percent(file.Bytes, node.Bytes)})", () => OpenUsageLocation(node.FullPath, file.Name),
                Detail: "Sul abre a pasta com o foco neste arquivo.", Icon: ActionIcon.File, Section: "Arquivos maiores"));
        if (node.OtherFiles > 0)
            items.Add(new MenuItem($"Outros {Plural.Of(node.OtherFiles, "arquivo", "arquivos")} — {FormatBytes(node.OtherFilesBytes)}", null, "Arquivos menores, só nos totais.",
                Icon: ActionIcon.File, Section: "Arquivos maiores"));
        if (ReferenceEquals(node, usage.Root))
        {
            if (usage.Inaccessible.Count > 0)
                items.Add(new MenuItem($"{Plural.Of(usage.Inaccessible.Count, "pasta não lida", "pastas não lidas")} (fora da soma)", null,
                    string.Join(", ", usage.Inaccessible.Take(3).Select(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p)))) + (usage.Inaccessible.Count > 3 ? "…" : string.Empty),
                    Icon: ActionIcon.Warning, Section: "Avisos"));
            if (usage.LinksNotFollowed > 0)
                items.Add(new MenuItem(Plural.Of(usage.LinksNotFollowed, "junção ou link não seguido", "junções ou links não seguidos"), null,
                    "O conteúdo para onde eles apontam não entra na soma.", Icon: ActionIcon.Info, Section: "Avisos"));
        }
        var menu = new MenuModal($"Uso do disco: {node.Name}", items)
        {
            Icon = ActionIcon.Properties,
            Subtitle = $"{FormatBytes(node.Bytes)} · {Plural.Of(node.Files, "arquivo", "arquivos")} · {Plural.Of(node.Folders, "pasta", "pastas")}",
        };
        // A maior pasta (ou arquivo) já em foco: Sul desce direto no que mais ocupa.
        if (items.Count > 1 && items[1].IsEnabled) menu.FocusIndex = 1;
        _usageMenus.RemoveAll(m => !Modals.Contains(m));
        _usageMenus.Add(menu);
        PushModal(menu);
    }

    /// <summary>Fecha todos os níveis da análise e abre a pasta no navegador (com o foco no arquivo escolhido).</summary>
    private void OpenUsageLocation(string folder, string? focusName)
    {
        foreach (var menu in _usageMenus) CloseModal(menu);
        _usageMenus.Clear();
        Screen = Screen.Browser;
        Track(NavigateAsync(Browser, new PhysicalLocation(folder), pushHistory: Browser.Location is not null, focusId: focusName));
    }
}
