using ControlFS.Core.Models;
using ControlFS.Core.Policies;

namespace ControlFS.Infrastructure.Archives.Engines;

/// <summary>
/// Adaptador de um motor de descompactação. Cada formato pode ter estratégia própria
/// (acesso aleatório, sequencial, sólido); o extrator seguro consome entradas na ordem do arquivo.
/// </summary>
public interface IArchiveEngine
{
    string Name { get; }

    string Version { get; }

    bool Supports(ArchiveFormat format);

    /// <exception cref="Core.Contracts.ArchiveAccessException"/>
    IArchiveReadSession Open(string archivePath, ArchiveFormat format, string? password, ExtractionLimits limits, CancellationToken cancellationToken);
}

public interface IArchiveReadSession : IDisposable
{
    ArchiveInfo Info { get; }

    /// <summary>Abre o fluxo descompactado da entrada pelo índice de <see cref="ArchiveInfo.Entries"/>.</summary>
    Stream OpenEntry(int index);
}
