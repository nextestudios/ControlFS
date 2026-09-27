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

/// <summary>
/// Lixeira do Windows: local virtual (não é uma pasta física). Os itens só podem ser restaurados ao local original ou
/// excluídos de vez; nunca abertos, copiados ou movidos como arquivos comuns.
/// </summary>
public sealed record RecycleBinLocation : Location
{
    /// <summary>Id da Lixeira entre os locais da tela inicial.</summary>
    public const string PlaceId = "recyclebin:";

    /// <summary>Nome de análise do Shell para a Lixeira (usado só para pedir o ícone do Windows).</summary>
    public const string ShellParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    public static RecycleBinLocation Instance { get; } = new();

    public override string DisplayPath => "Lixeira";
}
