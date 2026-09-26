using ControlFS.Core.Models;

namespace ControlFS.Core.Contracts;

public sealed record DirectoryListing(string Path, IReadOnlyList<FileEntry> Entries, int InaccessibleCount);

public interface IFileSystemProvider
{
    /// <summary>Locais iniciais: pastas conhecidas obtidas do sistema e unidades prontas.</summary>
    IReadOnlyList<FileEntry> GetPlaces();

    Task<DirectoryListing> ListAsync(string path, bool includeHidden, CancellationToken cancellationToken);

    string? GetParent(string path);

    bool DirectoryExists(string path);

    /// <summary>Cria uma pasta nova. Nunca reutiliza nem sobrescreve uma existente.</summary>
    /// <exception cref="FileOperationException">Com <see cref="OperationErrorKind"/> específico.</exception>
    FileEntry CreateDirectory(string parentPath, string name);
}

public sealed class FileOperationException(OperationErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public OperationErrorKind Kind { get; } = kind;
}
