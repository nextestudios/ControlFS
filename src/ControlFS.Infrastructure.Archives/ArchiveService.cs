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

    public ArchiveService() : this([new SharpCompressEngine()])
    {
    }

    public ArchiveService(IReadOnlyList<IArchiveEngine> engines) => _engines = engines;

    public ArchiveFormat Detect(string path)
    {
        try { return FormatDetector.Detect(path); }
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
        return new SafeExtractor(engine).ExtractAsync(format, request, interaction, progress, cancellationToken);
    }

    private IArchiveEngine EngineFor(ArchiveFormat format) =>
        _engines.FirstOrDefault(e => e.Supports(format))
        ?? throw new ArchiveAccessException(OperationErrorKind.UnsupportedFormat, format == ArchiveFormat.Unknown
            ? "Formato não reconhecido pelo conteúdo."
            : $"Formato {format} reconhecido, mas ainda não suportado nesta versão.");

    public Task<OperationResult> CompressAsync(CompressionRequest request, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        ArchiveCreator.CreateAsync(request, progress, cancellationToken);
}
