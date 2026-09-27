using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Core.Policies;
using ControlFS.Infrastructure.Archives.Creation;
using ControlFS.Infrastructure.Archives.Engines;
using ControlFS.Infrastructure.Archives.Extraction;
using ControlFS.Infrastructure.Archives.Inspection;

namespace ControlFS.Infrastructure.Archives;

public sealed class ArchiveService : IArchiveService
{
    private readonly IReadOnlyList<IArchiveEngine> _engines;
    private readonly ITemporaryJournal? _journal;

    /// <param name="journal">Registro dos temporários (staging, compactado parcial) para limpeza após uma queda.</param>
    public ArchiveService(ITemporaryJournal? journal = null) : this([new BclTarEngine(), new SharpCompressEngine()], journal)
    {
    }

    public ArchiveService(IReadOnlyList<IArchiveEngine> engines, ITemporaryJournal? journal = null)
    {
        _engines = engines;
        _journal = journal;
    }

    public ArchiveFormat Detect(string path)
    {
        try
        {
            // Volume de um compactado dividido: o formato vem do primeiro volume (os seguintes não têm assinatura). Sem ele,
            // o nome decide, para que abrir mostre quais volumes faltam em vez de tratar o arquivo como desconhecido.
            if (VolumeSet.Find(path) is not { } volumes) return FormatDetector.Detect(path);
            var format = volumes.DetectionPath is { } probe ? FormatDetector.Detect(probe) : ArchiveFormat.Unknown;
            return format == ArchiveFormat.Unknown && volumes.IsMultiPart ? volumes.NameHint : format;
        }
        catch (IOException) { return ArchiveFormat.Unknown; }
        catch (UnauthorizedAccessException) { return ArchiveFormat.Unknown; }
    }

    public Task<ArchiveInfo> InspectAsync(string archivePath, string? password, ExtractionLimits limits, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var format = Detect(archivePath);
            var engine = EngineFor(format);
            using var session = engine.Open(archivePath, format, password, limits, cancellationToken);
            return session.Info;
        }, cancellationToken);

    public Task<OperationResult> ExtractAsync(ExtractionRequest request, IExtractionInteraction interaction, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var format = Detect(request.ArchivePath);
        IArchiveEngine engine;
        try { engine = EngineFor(format); }
        catch (ArchiveAccessException ex) { return Task.FromResult(new OperationResult(OperationState.Failed, [], ex.Kind, ex.Message)); }
        return new SafeExtractor(engine, _journal).ExtractAsync(format, request, interaction, progress, cancellationToken);
    }

    public Task<OperationResult> TestAsync(ArchiveTestRequest request, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var format = Detect(request.ArchivePath);
        IArchiveEngine engine;
        try { engine = EngineFor(format); }
        catch (ArchiveAccessException ex) { return Task.FromResult(new OperationResult(OperationState.Failed, [], ex.Kind, ex.Message)); }
        return new SafeExtractor(engine, _journal).TestAsync(format, request, progress, cancellationToken);
    }

    private IArchiveEngine EngineFor(ArchiveFormat format) =>
        _engines.FirstOrDefault(e => e.Supports(format))
        ?? throw new ArchiveAccessException(OperationErrorKind.UnsupportedFormat, format == ArchiveFormat.Unknown
            ? "Formato não reconhecido pelo conteúdo."
            : $"Formato {format} reconhecido, mas ainda não suportado nesta versão.");

    public Task<OperationResult> CompressAsync(CompressionRequest request, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        ArchiveCreator.CreateAsync(request, progress, cancellationToken, _journal);
}
