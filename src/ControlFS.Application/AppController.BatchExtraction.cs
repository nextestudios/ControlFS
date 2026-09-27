using ControlFS.Application.State;
using ControlFS.Core.Actions;
using ControlFS.Core.Models;
using ControlFS.Core.Text;

namespace ControlFS.Application;

/// <summary>
/// Vários compactados marcados, extraídos de uma vez, cada um na sua própria pasta nova (#69). Cada compactado vira uma
/// operação separada na fila (progresso, cancelamento e "tentar de novo" próprios); um resumo único mostra o resultado
/// de cada um quando todos terminam. Conteúdos nunca se misturam: a pasta dedicada nunca reaproveita uma existente.
/// </summary>
public sealed partial class AppController
{
    internal sealed class ExtractionBatch(IReadOnlyList<string> archives, string destination)
    {
        public IReadOnlyList<string> Archives { get; } = archives;
        public string Destination { get; } = destination;
        public Dictionary<string, (OperationState State, string Detail)> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>O resumo já foi mostrado; tentativas posteriores (tentar de novo) seguem como extrações avulsas.</summary>
        public bool Reported { get; set; }
    }

    /// <summary>
    /// Caminhos dos itens marcados que parecem compactados (pela extensão; o formato real é detectado ao iniciar).
    /// Volumes de um mesmo compactado dividido contam uma vez só (qualquer volume abre o conjunto).
    /// </summary>
    private static List<string> MarkedArchives(IEnumerable<FileEntry> marked) =>
        marked.Where(e => e.Kind == EntryKind.File && e.FullPath is not null && ArchiveFormats.HasExtractableExtension(e.Name))
            .Select(e => e.FullPath!)
            .DistinctBy(path => ArchiveFormats.VolumeSetKey(path) ?? path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal void BeginBatchExtraction(PaneState pane, IReadOnlyList<string> archives)
    {
        if (pane.Location is not PhysicalLocation here || archives.Count == 0) return;
        var lines = new List<(string, string)>
        {
            ("Destino", here.FullPath),
            ("Pastas", "uma pasta nova para cada compactado (se o nome existir, \"(2)\")"),
        };
        foreach (var archive in archives.Take(8))
            lines.Add(("• " + Path.GetFileName(archive), "→ " + ArchiveFormats.StemOf(archive)));
        if (archives.Count > 8) lines.Add(("…", $"mais {archives.Count - 8}"));
        lines.Add(("Segurança", "conteúdos nunca se misturam; links bloqueados; nada é executado"));

        var dialog = new DialogModal($"Extrair {Plural.Of(archives.Count, "compactado", "compactados")}", lines)
        {
            Message = "Cada compactado entra na fila como uma operação própria. Os protegidos por senha pedem a senha na vez deles.",
            Icon = ActionIcon.Extract,
        };
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(new DialogOption("Extrair", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            pane.List.ClearSelection();
            Track(StartBatchExtractionAsync(new ExtractionBatch(archives, here.FullPath)));
        }, icon: ActionIcon.Extract));
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        PushModal(dialog);
    }

    private async Task StartBatchExtractionAsync(ExtractionBatch batch)
    {
        foreach (var archive in batch.Archives)
        {
            var format = await Task.Run(() => _archives.Detect(archive));
            if (!ArchiveFormats.CanExtract(format))
            {
                RecordBatchResult(batch, archive, OperationState.Failed, "não é um compactado suportado (conteúdo)");
                continue;
            }
            // Sem perguntar senha antes: a fila é serial, então quem precisar de senha pergunta na sua vez (uma de cada vez).
            Enqueue(new ExtractionPlan(archive, batch.Destination, Dedicated: true, Selected: null, BasePath: string.Empty, batch), password: null);
        }
        StatusMessage = $"{Plural.Of(batch.Archives.Count, "extração", "extrações")} na fila, cada uma na sua pasta.";
        RaiseChanged();
    }

    private void RecordBatchResult(ExtractionBatch batch, string archive, OperationResult result)
    {
        var folder = result.Destination is { } dest ? " → " + Path.GetFileName(dest) : string.Empty;
        var problems = result.Count(ItemOutcome.Failed) + result.Count(ItemOutcome.Blocked) + result.Count(ItemOutcome.NotProcessed);
        var detail = result.FinalState switch
        {
            OperationState.Completed => $"concluída ({Plural.Of(result.Count(ItemOutcome.Succeeded), "arquivo", "arquivos")}){folder}",
            OperationState.CompletedWithWarnings => $"com avisos ({Plural.Of(problems, "problema", "problemas")}){folder}",
            OperationState.Cancelled => "cancelada" + folder,
            _ => "falhou: " + (result.Message ?? result.Error.ToString()),
        };
        RecordBatchResult(batch, archive, result.FinalState, detail);
    }

    private void RecordBatchResult(ExtractionBatch batch, string archive, OperationState state, string detail)
    {
        if (batch.Reported) return;
        batch.Results[archive] = (state, detail);
        if (batch.Results.Count < batch.Archives.Count) return;
        batch.Reported = true;
        ShowBatchResult(batch);
    }

    private void ShowBatchResult(ExtractionBatch batch)
    {
        var allOk = batch.Results.Values.All(r => r.State == OperationState.Completed);
        var lines = new List<(string, string)> { ("Destino", batch.Destination) };
        foreach (var archive in batch.Archives)
            lines.Add((Path.GetFileName(archive), batch.Results[archive].Detail));
        var dialog = new DialogModal($"Extração de {Plural.Of(batch.Archives.Count, "compactado", "compactados")} " + (allOk ? "concluída" : "com problemas"), lines)
        {
            Message = "Detalhes de cada compactado (e tentar de novo) em Menu → Operações.",
            Icon = allOk ? ActionIcon.Success : ActionIcon.Warning,
        };
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Close);
        if (Directory.Exists(batch.Destination))
            dialog.Options.Add(new DialogOption("Abrir pasta de destino", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                Screen = Screen.Browser;
                Track(NavigateAsync(Browser, new PhysicalLocation(batch.Destination), pushHistory: Browser.Location is not null));
            }, icon: ActionIcon.OpenFolder));
        dialog.Options.Add(close);
        dialog.BackOption = close;
        PushModal(dialog);
    }
}
