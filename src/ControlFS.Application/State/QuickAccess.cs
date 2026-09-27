using ControlFS.Core.Models;

namespace ControlFS.Application.State;

public enum QuickAccessKind
{
    /// <summary>Menu com as pastas favoritas.</summary>
    Favorites,

    /// <summary>Menu com as pastas e arquivos recentes.</summary>
    Recents,

    /// <summary>Pasta do Windows (Downloads, Documentos…), aberta direto.</summary>
    Folder,

    /// <summary>Unidades e dispositivos (por enquanto, o início com o foco na primeira unidade).</summary>
    ThisPc,

    /// <summary>Lixeira do Windows.</summary>
    RecycleBin,
}

/// <summary>Item do acesso rápido da barra superior. <see cref="Place"/>: o local do início que ele abre (ícone do Windows).</summary>
public sealed record QuickAccessItem(string Label, QuickAccessKind Kind, string? Path = null)
{
    public FileEntry? Place { get; init; }
}
