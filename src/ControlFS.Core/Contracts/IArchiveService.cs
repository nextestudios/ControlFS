using ControlFS.Core.Models;
using ControlFS.Core.Policies;

namespace ControlFS.Core.Contracts;

public enum DestinationMode
{
    /// <summary>Cria uma pasta nova e exclusiva (padrão seguro). Se o nome existir, usa "nome (2)".</summary>
    CreateDedicatedFolder,
    /// <summary>Extrai dentro de uma pasta existente; conflitos são resolvidos pelo usuário.</summary>
    IntoExistingFolder,
}

/// <summary>
/// Pedido de extração. Classe (não record) para que a senha nunca apareça em ToString/logs.
/// </summary>
public sealed class ExtractionRequest
{
    public required string ArchivePath { get; init; }
    public required string DestinationDirectory { get; init; }
    public DestinationMode Mode { get; init; } = DestinationMode.CreateDedicatedFolder;
    /// <summary>Nome da pasta dedicada (somente <see cref="DestinationMode.CreateDedicatedFolder"/>).</summary>
    public string? DedicatedFolderName { get; init; }
    /// <summary>
    /// Caminhos lógicos sanitizados selecionados ("pasta" ou "pasta/arquivo.txt"). Null = todas as entradas.
    /// Uma pasta selecionada inclui seus descendentes.
    /// </summary>
    public IReadOnlyCollection<string>? SelectedPaths { get; init; }
    /// <summary>Prefixo lógico removido dos caminhos (extração de seleção a partir de uma subpasta do compactado).</summary>
    public string BaseInnerPath { get; init; } = string.Empty;
    public string? Password { get; init; }
    public ExtractionLimits Limits { get; init; } = ExtractionLimits.Default;

    public override string ToString() => $"Extract '{Path.GetFileName(ArchivePath)}' ({Mode})";
}

/// <summary>
/// Pedido de teste de integridade: lê todas as entradas até o fim e confere tamanho e CRC, sem gravar nada em disco.
/// Classe (não record) para que a senha nunca apareça em ToString/logs.
/// </summary>
public sealed class ArchiveTestRequest
{
    public required string ArchivePath { get; init; }
    public string? Password { get; init; }
    public ExtractionLimits Limits { get; init; } = ExtractionLimits.Default;

    public override string ToString() => $"Test '{Path.GetFileName(ArchivePath)}'";
}

public enum CompressionFormat
{
    Zip,
    TarGZip,
    /// <summary>7z sólido com LZMA (ver docs/decisions/0008).</summary>
    SevenZip,
}

public enum CompressionStrength
{
    Fast,
    Normal,
    Maximum,
}

/// <summary>Pedido de compactação de arquivos e pastas físicos (todos na mesma pasta de origem).</summary>
public sealed class CompressionRequest
{
    public required IReadOnlyList<string> SourcePaths { get; init; }
    /// <summary>Caminho final do compactado. Nunca é sobrescrito: se existir, a operação falha sem tocar nele.</summary>
    public required string DestinationPath { get; init; }
    public CompressionFormat Format { get; init; } = CompressionFormat.Zip;
    public CompressionStrength Strength { get; init; } = CompressionStrength.Normal;

    public static string Extension(CompressionFormat format) => format switch
    {
        CompressionFormat.TarGZip => ".tar.gz",
        CompressionFormat.SevenZip => ".7z",
        _ => ".zip",
    };
}

/// <summary>Conflitos durante a extração (mesmo contrato das demais operações de arquivo).</summary>
public interface IExtractionInteraction : IConflictInteraction
{
}

public interface IArchiveService
{
    /// <summary>Detecta o formato pelo conteúdo; a extensão é apenas pista.</summary>
    ArchiveFormat Detect(string path);

    /// <exception cref="ArchiveAccessException"/>
    Task<ArchiveInfo> InspectAsync(string archivePath, string? password, ExtractionLimits limits, CancellationToken cancellationToken);

    Task<OperationResult> ExtractAsync(ExtractionRequest request, IExtractionInteraction interaction, IProgress<OperationProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Testa a integridade sem extrair: cada entrada de arquivo é lida por completo (descartando os dados) e conferida
    /// com o tamanho declarado e o CRC, quando o formato guarda um. Um item por entrada no resultado.
    /// </summary>
    Task<OperationResult> TestAsync(ArchiveTestRequest request, IProgress<OperationProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Cria um compactado. Escreve num temporário na pasta de destino e só o torna visível ao concluir.</summary>
    Task<OperationResult> CompressAsync(CompressionRequest request, IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
}

public sealed class ArchiveAccessException(OperationErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public OperationErrorKind Kind { get; } = kind;
}
