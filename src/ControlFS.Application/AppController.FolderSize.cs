using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;

namespace ControlFS.Application;

/// <summary>
/// Propriedades → "Calcular tamanho": soma recursiva fora da thread de UI, com parciais no próprio diálogo. Voltar
/// (Leste/B) cancela na hora e mantém o parcial. Junções e links nunca são seguidos; pastas sem acesso são relatadas.
/// </summary>
public sealed partial class AppController
{
    private void StartFolderSize(DialogModal dialog, IReadOnlyList<(string, string)> baseLines, string folder)
    {
        var cts = new CancellationTokenSource();
        dialog.Options.Clear();
        var cancel = new DialogOption("Cancelar cálculo", DialogOptionKind.Safe, cts.Cancel);
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        dialog.FocusIndex = 0;
        dialog.Lines = [.. baseLines, ("Tamanho", "Calculando…")];
        Track(MeasureFolderAsync(dialog, baseLines, folder, cts));
    }

    private async Task MeasureFolderAsync(DialogModal dialog, IReadOnlyList<(string, string)> baseLines, string folder, CancellationTokenSource cts)
    {
        var finished = false;
        var last = FolderSize.Empty;
        // Progress<T> criado aqui (thread de UI) entrega os parciais de volta nela.
        var progress = new Progress<FolderSize>(partial =>
        {
            if (finished) return;
            last = partial;
            dialog.Lines = [.. baseLines, .. SizeLines(partial, "Calculando… ")];
            RaiseChanged();
        });
        string? outcome = null;
        FolderSize? result = null;
        try
        {
            result = await Task.Run(() => _fs.MeasureFolder(folder, progress, cts.Token), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            outcome = "Cálculo cancelado: o valor mostrado é parcial.";
        }
        catch (FileOperationException ex)
        {
            outcome = ex.Message;
        }
        finally
        {
            finished = true;
            cts.Dispose();
        }
        // Cancelado ou com erro: o último parcial continua visível, marcado como parcial.
        dialog.Lines = [.. baseLines, .. result is not null ? SizeLines(result, string.Empty) : SizeLines(last, "Parcial: ")];
        dialog.Message = outcome;
        dialog.Options.Clear();
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(new DialogOption(result is null ? "Calcular de novo" : "Recalcular", DialogOptionKind.Primary, () => StartFolderSize(dialog, baseLines, folder)));
        dialog.Options.Add(close);
        dialog.BackOption = close;
        dialog.FocusIndex = Math.Clamp(dialog.FocusIndex, 0, dialog.Options.Count - 1);
        RaiseChanged();
    }

    private static IEnumerable<(string, string)> SizeLines(FolderSize size, string prefix)
    {
        yield return ("Tamanho", $"{prefix}{FormatBytes(size.Bytes)} ({size.Bytes:N0} bytes)");
        yield return ("Conteúdo", $"{size.Files:N0} arquivo(s), {size.Folders:N0} pasta(s)");
        if (size.Inaccessible.Count > 0)
            yield return ("Sem acesso", $"{size.Inaccessible.Count} pasta(s) não lida(s), fora da soma: "
                + string.Join(", ", size.Inaccessible.Take(3).Select(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p)))) + (size.Inaccessible.Count > 3 ? "…" : string.Empty));
        if (size.LinksNotFollowed > 0)
            yield return ("Links", $"{size.LinksNotFollowed} junção(ões) ou link(s) não seguido(s)");
    }
}
