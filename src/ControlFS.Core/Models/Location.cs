namespace ControlFS.Core.Models;

/// <summary>
/// Onde o navegador está. Locais físicos, diretórios virtuais dentro de compactados
/// e atalhos da interface são tipos distintos e nunca intercambiáveis.
/// </summary>
public abstract record Location
{
    public abstract string DisplayPath { get; }
}

/// <summary>Tela inicial de locais (atalho da interface, não um caminho).</summary>
public sealed record HomeLocation : Location
{
    public static HomeLocation Instance { get; } = new();

    public override string DisplayPath => "Início";
}

/// <summary>Diretório físico acessível pela conta atual.</summary>
public sealed record PhysicalLocation(string FullPath) : Location
{
    public override string DisplayPath => FullPath;
}

/// <summary>
/// Diretório virtual dentro de um compactado. <see cref="InnerPath"/> é um caminho lógico
/// derivado de metadados não confiáveis, usado apenas para exibição e filtragem; nunca
/// é combinado diretamente com um caminho de disco.
/// </summary>
public sealed record ArchiveLocation(string ArchivePath, string InnerPath) : Location
{
    public override string DisplayPath =>
        InnerPath.Length == 0 ? $"{ArchivePath} ▸ /" : $"{ArchivePath} ▸ /{InnerPath}";
}
