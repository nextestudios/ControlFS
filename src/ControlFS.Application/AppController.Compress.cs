using ControlFS.Application.Operations;
using ControlFS.Application.State;
using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Core.Text;

namespace ControlFS.Application;

public sealed partial class AppController
{
    private readonly Dictionary<int, CompressPlan> _compressions = [];

    internal sealed record CompressPlan(string Folder, IReadOnlyList<string> Sources, string BaseName, CompressionFormat Format, CompressionStrength Strength)
    {
        public string FileName(Func<string, bool> exists) => UniqueNames.Next(BaseName + CompressionRequest.Extension(Format), exists);
    }

    /// <summary>Compactar itens de uma pasta do disco: resumo com nome, formato e compressão antes de iniciar.</summary>
    internal void BeginCompress(PaneState pane, IReadOnlyList<FileEntry> entries)
    {
        if (pane.Location is not PhysicalLocation here || entries.Count == 0) return;
        var sources = entries.Where(e => e.FullPath is not null).Select(e => e.FullPath!).ToList();
        var baseName = entries.Count == 1
            ? (entries[0].IsContainer ? entries[0].Name : Path.GetFileNameWithoutExtension(entries[0].Name))
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(here.FullPath));
        if (!WindowsNameRules.ValidateComponent(baseName).IsValid) baseName = "Compactado";
        ShowCompressDialog(pane, new CompressPlan(here.FullPath, sources, baseName, CompressionFormat.Zip, CompressionStrength.Normal));
    }

    private void ShowCompressDialog(PaneState pane, CompressPlan initial)
    {
        var plan = initial;
        var dialog = new DialogModal("Compactar", []);
        bool Exists(string name) => File.Exists(Path.Join(plan.Folder, name)) || Directory.Exists(Path.Join(plan.Folder, name));
        void Refill()
        {
            var names = plan.Sources.Select(Path.GetFileName).Take(3).ToList();
            var more = plan.Sources.Count > 3 ? $" e mais {plan.Sources.Count - 3}" : string.Empty;
            dialog.Lines =
            [
                ("Itens", $"{plan.Sources.Count}: {string.Join(", ", names)}{more}"),
                ("Destino", plan.Folder),
                ("Arquivo", plan.FileName(Exists) + (Exists(plan.BaseName + CompressionRequest.Extension(plan.Format)) ? "  (o nome já existia: numerado)" : string.Empty)),
                ("Formato", plan.Format == CompressionFormat.Zip ? "ZIP — abre em qualquer Windows" : "TAR.GZ — comum em Linux/macOS"),
                ("Compressão", StrengthLabel(plan.Strength)),
                ("Segurança", "links e junctions não são seguidos; nada é sobrescrito"),
            ];
        }
        Refill();
        DialogOption? name = null, format = null, strength = null;
        name = new DialogOption($"Nome: {plan.BaseName}…", DialogOptionKind.Toggle, () => AskCompressName(plan, newName =>
        {
            plan = plan with { BaseName = newName };
            name!.Label = $"Nome: {plan.BaseName}…";
            Refill();
        }));
        format = new DialogOption(FormatLabel(plan.Format), DialogOptionKind.Toggle, () =>
        {
            plan = plan with { Format = plan.Format == CompressionFormat.Zip ? CompressionFormat.TarGZip : CompressionFormat.Zip };
            format!.Label = FormatLabel(plan.Format);
            Refill();
        });
        strength = new DialogOption(StrengthOption(plan.Strength), DialogOptionKind.Toggle, () =>
        {
            plan = plan with { Strength = (CompressionStrength)(((int)plan.Strength + 1) % 3) };
            strength!.Label = StrengthOption(plan.Strength);
            Refill();
        });
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog));
        dialog.Options.Add(new DialogOption("Compactar", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            EnqueueCompression(plan, plan.FileName(Exists));
        }));
        dialog.Options.Add(name);
        dialog.Options.Add(format);
        dialog.Options.Add(strength);
        dialog.Options.Add(cancel);
        dialog.BackOption = cancel;
        PushModal(dialog);
    }

    private void AskCompressName(CompressPlan plan, Action<string> apply)
    {
        var extension = CompressionRequest.Extension(plan.Format);
        var keyboard = new VirtualKeyboard(TextFieldKind.FileName, $"Nome do compactado (sem \"{extension}\")", plan.BaseName,
            validator: text => WindowsNameRules.ValidateComponent(text + extension) is { IsValid: false } v ? v.Message : null);
        KeyboardModal? modal = null;
        modal = new KeyboardModal(keyboard, k =>
        {
            apply(k.Text);
            CloseModal(modal!);
            return Task.CompletedTask;
        });
        PushModal(modal);
    }

    private void EnqueueCompression(CompressPlan plan, string fileName)
    {
        var request = new CompressionRequest
        {
            SourcePaths = plan.Sources,
            DestinationPath = Path.Join(plan.Folder, fileName),
            Format = plan.Format,
            Strength = plan.Strength,
        };
        var item = Operations.Enqueue($"Compactar {fileName}", OperationKind.Compress, async (op, ct) =>
        {
            var progress = new Progress<OperationProgress>(p => Operations.ReportProgress(op, p));
            return await _archives.CompressAsync(request, progress, ct);
        });
        _compressions[item.Id] = plan;
        StatusMessage = "Compactação iniciada. Você pode continuar navegando.";
        RaiseChanged();
    }

    private void OnCompressionCompleted(OperationItem item, CompressPlan plan, OperationResult result)
    {
        var title = result.FinalState switch
        {
            OperationState.Completed => "Compactado criado",
            OperationState.CompletedWithWarnings => "Compactado criado com avisos",
            OperationState.Cancelled => "Compactação cancelada",
            _ => "Compactação falhou",
        };
        var lines = new List<(string, string)>();
        if (result.Destination is { } created) lines.Add(("Arquivo", created));
        var included = result.Count(ItemOutcome.Succeeded);
        if (included > 0) lines.Add(("Arquivos incluídos", included.ToString()));
        foreach (var problem in result.Items.Where(i => i.Outcome is ItemOutcome.Failed or ItemOutcome.Skipped).Take(6))
            lines.Add(("• " + problem.Name, problem.Message ?? problem.Error.ToString()));
        var dialog = new DialogModal(title, lines) { Message = result.Message };
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog));
        if (result.Destination is { } path && File.Exists(path))
            dialog.Options.Add(new DialogOption("Mostrar o arquivo", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                Screen = Screen.Browser;
                Track(NavigateAsync(Browser, new PhysicalLocation(plan.Folder), pushHistory: Browser.Location is PhysicalLocation p && p.FullPath != plan.Folder,
                    focusId: Path.GetFileName(path)));
            }));
        dialog.Options.Add(close);
        dialog.BackOption = close;
        PushModal(dialog);
        if (Browser.Location is PhysicalLocation here && string.Equals(here.FullPath, plan.Folder, StringComparison.OrdinalIgnoreCase))
            Refresh(Browser, result.Destination is { } d ? Path.GetFileName(d) : null);
    }

    private static string FormatLabel(CompressionFormat format) => format == CompressionFormat.Zip ? "Formato: ZIP" : "Formato: TAR.GZ";

    private static string StrengthOption(CompressionStrength strength) => $"Compressão: {StrengthLabel(strength)}";

    private static string StrengthLabel(CompressionStrength strength) => strength switch
    {
        CompressionStrength.Fast => "rápida",
        CompressionStrength.Maximum => "máxima (mais lenta)",
        _ => "normal",
    };
}
