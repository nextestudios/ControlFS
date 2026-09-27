namespace ControlFS.Core.Policies;

/// <summary>
/// Locais de rede que o Windows já conhece (#27): unidades mapeadas e atalhos da pasta "Locais de rede". O destino vem
/// de arquivos do usuário (.lnk) e do registro, então é tratado como não confiável: só compartilhamentos SMB em forma
/// UNC (<c>\\servidor\compartilhamento[\pasta…]</c>) são aceitos; prefixos de dispositivo (<c>\\?\</c>, <c>\\.\</c>),
/// URLs (WebDAV, FTP), caminhos relativos, <c>.</c>/<c>..</c> e nomes com caracteres inválidos são recusados. Nada aqui
/// acessa a rede: o ControlFS só lista o que o Windows guardou e deixa o acesso para quando o usuário abrir o local.
/// </summary>
public static class NetworkPathPolicy
{
    /// <summary>Caminho UNC mais longo aceito.</summary>
    public const int MaxLength = 1024;

    private static readonly System.Buffers.SearchValues<char> InvalidInName = System.Buffers.SearchValues.Create("<>:\"|?*/\\");

    /// <summary>Compartilhamento em forma UNC normalizado (barras invertidas, sem barra final), ou <c>null</c> se recusado.</summary>
    public static string? NormalizeShare(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxLength) return null;
        var path = raw.Trim().Replace('/', '\\');
        if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        var parts = path[2..].TrimEnd('\\').Split('\\');
        if (parts.Length < 2) return null; // só o servidor: não é um compartilhamento
        if (parts[0] is "?" or ".") return null; // \\?\ e \\.\: prefixos de dispositivo
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')) return null;
            if (part.IndexOfAny(InvalidInName) >= 0 || part.Any(char.IsControl)) return null;
        }
        return @"\\" + string.Join('\\', parts);
    }

    /// <summary>Nome do compartilhamento (<c>\\nas\filmes</c> → "filmes"), para rotular um local sem nome próprio.</summary>
    public static string ShareName(string share)
    {
        var parts = share.TrimStart('\\').Split('\\');
        return parts.Length >= 2 ? parts[^1] : share;
    }

    /// <summary>Servidor do caminho UNC (<c>\\nas\filmes</c> → "nas").</summary>
    public static string Server(string share) => share.TrimStart('\\').Split('\\')[0];
}
