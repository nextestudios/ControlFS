using ControlFS.Core.Models;

namespace ControlFS.Core.Contracts;

/// <summary>Ícone já rasterizado: pixels BGRA de 32 bits com alfa pré-multiplicado, linha a linha de cima para baixo.</summary>
public sealed record IconImage(int Width, int Height, ReadOnlyMemory<byte> Pixels);

public enum IconSourceKind
{
    /// <summary>Ícone do tipo de arquivo pela extensão, sem tocar no disco.</summary>
    Extension,

    /// <summary>Pasta genérica, sem tocar no disco.</summary>
    Folder,

    /// <summary>Ícone do item real (unidade, pasta especial, programa): consulta o disco.</summary>
    Path,

    /// <summary>
    /// Atalho (.lnk, ou .url de jogo da Steam): o ícone que o próprio atalho declara, lido e validado pela
    /// infraestrutura (só caminhos locais em unidade fixa; nunca rede). <see cref="IconRequest.Value"/> é o caminho do atalho.
    /// </summary>
    Shortcut,
}

/// <summary>
/// Pedido de ícone. <see cref="Key"/> identifica o ícone no cache: por tipo/extensão para os casos comuns e por caminho
/// só onde o ícone é próprio do item (unidades, pastas especiais, .exe/.ico). Atalhos (.lnk e .url de jogos da Steam)
/// são chaveados pelo caminho mais data de modificação e tamanho: editar o atalho troca a chave e o ícone é lido de novo.
/// </summary>
public sealed record IconRequest(string Key, IconSourceKind Kind, string Value)
{
    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".ico" };

    /// <summary>Ícone de uma linha da lista, ou <c>null</c> quando a linha usa só o símbolo de aviso (entrada bloqueada).</summary>
    public static IconRequest? For(FileEntry entry, IReadOnlySet<string>? specialFolders = null)
    {
        if (entry.IsBlocked) return null;
        switch (entry.Kind)
        {
            case EntryKind.KnownFolder when entry.Id == RecycleBinLocation.PlaceId:
                return new IconRequest("recyclebin", IconSourceKind.Path, RecycleBinLocation.ShellParsingName);
            case EntryKind.Drive when entry.Drive == DriveKind.Network:
                return null; // só o símbolo de rede: pedir o ícone ao Shell contataria o servidor (#27)
            case EntryKind.Drive or EntryKind.KnownFolder when entry.FullPath is { } place:
                return ForPath(place);
            case EntryKind.Directory when entry.FullPath is { } dir && specialFolders is not null && specialFolders.Contains(dir):
                return ForPath(dir);
            case EntryKind.Drive or EntryKind.KnownFolder or EntryKind.Directory or EntryKind.ArchiveDirectory:
                return new IconRequest("folder", IconSourceKind.Folder, string.Empty);
            case EntryKind.File when entry.FullPath is { } file && PerFileExtensions.Contains(entry.Extension):
                return ForPath(file);
            case EntryKind.File when entry.FullPath is { } shortcut && (entry.IsSteamGame || entry.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)):
                return new IconRequest(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"shortcut:{shortcut.ToUpperInvariant()}|{entry.Modified?.UtcTicks}|{entry.Size}"),
                    IconSourceKind.Shortcut, shortcut);
            default:
                // Entradas de compactados e arquivos comuns: só a extensão (nunca lê o disco).
                var extension = entry.Extension.ToLowerInvariant();
                return new IconRequest("ext:" + extension, IconSourceKind.Extension, extension);
        }
    }

    private static IconRequest ForPath(string path) => new("path:" + path.ToUpperInvariant(), IconSourceKind.Path, path);
}

/// <summary>Ícones do sistema. Implementações nunca bloqueiam quem chama: o trabalho acontece fora da thread de UI.</summary>
public interface IIconProvider
{
    /// <summary>Ícone com lado de <paramref name="sizePx"/> pixels físicos, ou <c>null</c> quando não há (o chamador usa um símbolo).</summary>
    Task<IconImage?> GetIconAsync(IconRequest request, int sizePx, CancellationToken cancellationToken);
}
