using ControlFS.Core.Contracts;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Windows.FileSystem;

namespace ControlFS.Infrastructure.Archives.Security;

/// <summary>
/// Cria e verifica a cadeia de diretórios abaixo da raiz autorizada, recusando qualquer componente existente que seja
/// link simbólico, junction/reparse point ou arquivo. No Windows a verificação é feita por handle
/// (<see cref="PinnedDirectory"/>): a cadeia fica presa (não pode ser renomeada nem trocada por junction) enquanto o
/// arquivo é colocado no lugar, e a movimentação final é relativa ao handle da pasta. A identidade da raiz é fixada
/// na primeira verificação: se a raiz for trocada no meio da operação, as gravações seguintes são recusadas.
/// Limites restantes: docs/security-model.md.
/// </summary>
internal sealed class DestinationGuard
{
    private readonly string _root;
    private readonly string _rootWithSeparator;
    private readonly StringComparison _comparison;
    private string? _rootIdentity;

    public DestinationGuard(string root)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _rootWithSeparator = _root + Path.DirectorySeparatorChar;
        _comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public string Root => _root;

    /// <summary>Garante que root/c1/c2/... exista como diretórios reais. Componentes já foram validados pela política.</summary>
    public string EnsureDirectories(IReadOnlyList<string> components, int count)
    {
        using var pinned = Pin(components, count, create: true);
        return pinned.FullPath;
    }

    /// <summary>
    /// Prende root/c1/.../c<paramref name="count"/> (criando o que faltar, se pedido) e confere que continua contida na
    /// raiz. O chamador grava e move enquanto a instância existe e a descarta logo depois.
    /// </summary>
    public PinnedDirectory Pin(IReadOnlyList<string> components, int count, bool create)
    {
        var pinned = PinnedDirectory.Open(_root, components.Take(count).ToList(), create, _rootIdentity);
        try
        {
            _rootIdentity ??= pinned.RootIdentity;
            AssertContained(pinned.FullPath, allowRoot: true);
            return pinned;
        }
        catch
        {
            pinned.Dispose();
            throw;
        }
    }

    /// <summary>Defesa em profundidade: o caminho final normalizado precisa estar abaixo da raiz.</summary>
    public void AssertContained(string path, bool allowRoot = false)
    {
        var full = Path.GetFullPath(path);
        if (allowRoot && string.Equals(Path.TrimEndingDirectorySeparator(full), _root, _comparison)) return;
        if (!full.StartsWith(_rootWithSeparator, _comparison))
            throw new FileOperationException(OperationErrorKind.PathRejected, "Caminho final fora da pasta de destino autorizada.");
    }

    public static bool IsLink(FileSystemInfo info) =>
        info.Exists && (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0);
}
