using ControlFS.Core.Models;
using ControlFS.Core.Contracts;

namespace ControlFS.Infrastructure.Archives.Security;

/// <summary>
/// Cria e verifica a cadeia de diretórios abaixo da raiz autorizada, recusando qualquer
/// componente existente que seja link simbólico, junction/reparse point ou arquivo.
/// A verificação é repetida imediatamente antes de cada gravação final (revalidação),
/// o que reduz — mas NÃO elimina — a janela de corrida contra outro processo com os
/// mesmos privilégios (ver docs/security-model.md).
/// </summary>
internal sealed class DestinationGuard
{
    private readonly string _root;
    private readonly string _rootWithSeparator;
    private readonly StringComparison _comparison;

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
        var current = _root;
        for (var i = 0; i < count; i++)
        {
            current = Path.Join(current, components[i]);
            var info = new DirectoryInfo(current);
            if (!info.Exists)
            {
                if (File.Exists(current))
                    throw new FileOperationException(OperationErrorKind.NameCollision, $"\"{components[i]}\" já existe como arquivo; não pode ser usado como pasta.");
                if (IsLink(new FileInfo(current)))
                    throw new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"\"{components[i]}\" é um link; a extração não o seguirá.");
                Directory.CreateDirectory(current);
                info.Refresh();
            }
            if (IsLink(info))
                throw new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"\"{components[i]}\" é um link ou junction; a extração não o seguirá.");
        }
        AssertContained(current, allowRoot: true);
        return current;
    }

    /// <summary>Revalida a cadeia existente sem criar nada.</summary>
    public void Revalidate(IReadOnlyList<string> components, int count)
    {
        var current = _root;
        for (var i = 0; i < count; i++)
        {
            current = Path.Join(current, components[i]);
            var info = new DirectoryInfo(current);
            if (!info.Exists || IsLink(info))
                throw new FileOperationException(OperationErrorKind.DestinationTraversesLink, $"A pasta \"{components[i]}\" mudou durante a extração ou se tornou um link.");
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
