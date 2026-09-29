using ControlFS.Application.Operations;
using ControlFS.Application.State;
using ControlFS.Core.Actions;
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
        var dialog = new DialogModal("Compactar", []) { Icon = ActionIcon.Compress };
        bool Exists(string name) => File.Exists(Path.Join(plan.Folder, name)) || Directory.Exists(Path.Join(plan.Folder, name));
        void Refill()
        {
            var names = plan.Sources.Select(Path.GetFileName).Take(3).ToList();
            var more = plan.Sources.Count > 3 ? $" e mais {plan.Sources.Count - 3}" : string.Empty;
            const string numbered = "  (o nome já existia: numerado)";
            // O painel tem o mesmo tamanho para ZIP, 7z e TAR.GZ e para cada compressão (#227): a altura já reserva o texto mais longo de cada linha.
            dialog.LineReserve = new Dictionary<string, IReadOnlyList<string>>
            {
                ["Arquivo"] = [plan.BaseName + CompressFormats.Select(c => CompressionRequest.Extension(c.Value)).MaxBy(e => e.Length) + numbered],
                ["Formato"] = [.. CompressFormats.Select(c => FormatDescription(c.Value))],
                ["Compressão"] = [.. StrengthChoices.Select(c => c.Label)],
            };
            dialog.Lines =
            [
                ("Itens", $"{plan.Sources.Count}: {string.Join(", ", names)}{more}"),
                ("Destino", plan.Folder),
                ("Arquivo", plan.FileName(Exists) + (Exists(plan.BaseName + CompressionRequest.Extension(plan.Format)) ? numbered : string.Empty)),
                ("Formato", FormatDescription(plan.Format)),
                ("Compressão", StrengthLabel(plan.Strength)),
                ("Segurança", "links e junctions não são seguidos; nada é sobrescrito"),
            ];
        }
        Refill();
        DialogOption? name = null;
        name = new DialogOption($"Nome: {plan.BaseName}…", DialogOptionKind.Toggle, () => AskCompressName(plan, newName =>
        {
            plan = plan with { BaseName = newName };
            name!.Label = $"Nome: {plan.BaseName}…";
            Refill();
        }), icon: ActionIcon.Rename);
        // Formato e compressão têm várias alternativas escondidas: abrem o seletor (#261) em vez de alternar às cegas.
        var format = ChoiceOption("Formato", () => plan.Format, AvailableCompressFormats(), picked =>
        {
            plan = plan with { Format = picked };
            Refill();
        }, ActionIcon.Archive, "Compactar");
        var strength = ChoiceOption("Compressão", () => plan.Strength, StrengthChoices, picked =>
        {
            plan = plan with { Strength = picked };
            Refill();
        }, ActionIcon.Density, "Compactar");
        var cancel = new DialogOption("Cancelar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Cancel);
        dialog.Options.Add(new DialogOption("Compactar", DialogOptionKind.Primary, () =>
        {
            CloseModal(dialog);
            EnqueueCompression(plan, plan.FileName(Exists));
        }, icon: ActionIcon.Compress));
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
        var keyboard = new VirtualKeyboard(TextFieldKind.FileName, $"Nome do compactado (sem “{extension}”)", plan.BaseName,
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
            var progress = Operations.ProgressFor(op);
            return await _archives.CompressAsync(request, progress, ct);
        });
        item.RetryAction = () => EnqueueCompression(plan, UniqueNames.Next(fileName, n => File.Exists(Path.Join(plan.Folder, n)) || Directory.Exists(Path.Join(plan.Folder, n))));
        item.Source = plan.Folder;
        item.Destination = request.DestinationPath;
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
        var dialog = new DialogModal(title, lines) { Message = result.Message, Icon = ResultIcon(result.FinalState) };
        var close = new DialogOption("Fechar", DialogOptionKind.Safe, () => CloseModal(dialog), icon: ActionIcon.Close);
        if (result.Destination is { } path && File.Exists(path))
            dialog.Options.Add(new DialogOption("Mostrar o arquivo", DialogOptionKind.Primary, () =>
            {
                CloseModal(dialog);
                Screen = Screen.Browser;
                Track(NavigateAsync(Browser, new PhysicalLocation(plan.Folder), pushHistory: Browser.Location is PhysicalLocation p && p.FullPath != plan.Folder,
                    focusId: Path.GetFileName(path)));
            }, icon: ActionIcon.Reveal));
        dialog.Options.Add(close);
        dialog.BackOption = close;
        ShowOperationResult(dialog);
        if (Browser.Location is PhysicalLocation here && string.Equals(here.FullPath, plan.Folder, StringComparison.OrdinalIgnoreCase))
            Refresh(Browser, result.Destination is { } d ? Path.GetFileName(d) : null);
    }

    /// <summary>
    /// Formatos que Compactar cria, na ordem do seletor (#261). Um formato novo é uma linha a mais aqui (mais o que o cria em
    /// <c>IArchiveService.CompressAsync</c>): o seletor, o texto do resumo e a reserva de altura do diálogo (#227) o pegam sozinhos.
    /// </summary>
    internal static IReadOnlyList<Choice<CompressionFormat>> CompressFormats { get; } =
    [
        new(CompressionFormat.Zip, "ZIP", "abre em qualquer Windows"),
        new(CompressionFormat.TarGZip, "TAR.GZ", "comum em Linux/macOS"),
        new(CompressionFormat.SevenZip, "7z", "menor; abre no 7-Zip e no Explorador do Windows 11 atual (mais lento para criar)"),
        new(CompressionFormat.Rar, "RAR", "abre no WinRAR; criado pelo WinRAR que você já tem instalado (o ControlFS não inclui nem baixa nada do RAR)"),
    ];

    /// <summary>
    /// Os formatos como o seletor os mostra agora: o RAR só fica disponível se o serviço achar o WinRAR do usuário; sem ele
    /// aparece desativado com o motivo (docs/decisions/0011). Os demais formatos sempre estão disponíveis.
    /// </summary>
    private IReadOnlyList<Choice<CompressionFormat>> AvailableCompressFormats() =>
        [.. CompressFormats.Select(c => _archives.GetCreationAvailability(c.Value) is { IsAvailable: false } none ? c with { DisabledReason = none.Reason ?? "Indisponível neste PC." } : c)];

    internal static IReadOnlyList<Choice<CompressionStrength>> StrengthChoices { get; } =
    [
        new(CompressionStrength.Fast, "rápida", "Comprime menos e termina antes."),
        new(CompressionStrength.Normal, "normal", "O equilíbrio entre tamanho e tempo."),
        new(CompressionStrength.Maximum, "máxima (mais lenta)", "O menor arquivo possível; demora mais."),
    ];

    /// <summary>A linha "Formato" do resumo: o nome e o que o formato tem de bom.</summary>
    private static string FormatDescription(CompressionFormat format) =>
        CompressFormats.FirstOrDefault(c => c.Value == format) is { } choice ? $"{choice.Label} — {choice.Description}" : format.ToString();

    private static string StrengthLabel(CompressionStrength strength) => StrengthChoices.First(c => c.Value == strength).Label;
}
