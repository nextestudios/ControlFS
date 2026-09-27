using System.Globalization;
using ControlFS.Core.Policies;

namespace ControlFS.Application.State;

public enum BatchRenameMode
{
    /// <summary>"Nome 001.ext", "Nome 002.ext"… na ordem da lista.</summary>
    Numbering,
    /// <summary>Localizar e substituir no nome (sem a extensão), com ou sem diferenciar maiúsculas.</summary>
    FindReplace,
    /// <summary>Texto antes e/ou depois do nome (o sufixo fica antes da extensão).</summary>
    PrefixSuffix,
    /// <summary>Converte o nome para minúsculas, MAIÚSCULAS ou Iniciais Maiúsculas.</summary>
    Case,
}

public enum BatchRenameCase
{
    Lower,
    Upper,
    Title,
}

/// <summary>Um item a renomear: caminho completo e se é arquivo (só arquivos têm a extensão preservada).</summary>
public sealed record BatchRenameSource(string FullPath, bool IsFile)
{
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(FullPath));
}

/// <summary>Os parâmetros escolhidos no diálogo. Todos os modos mudam só o nome; a extensão de arquivos é preservada.</summary>
public sealed record BatchRenameOptions
{
    public BatchRenameMode Mode { get; init; } = BatchRenameMode.Numbering;
    public string BaseName { get; init; } = string.Empty;
    public int Start { get; init; } = 1;
    public int Digits { get; init; } = 3;
    public string Find { get; init; } = string.Empty;
    public string Replace { get; init; } = string.Empty;
    public bool MatchCase { get; init; }
    public string Prefix { get; init; } = string.Empty;
    public string Suffix { get; init; } = string.Empty;
    public BatchRenameCase Case { get; init; } = BatchRenameCase.Lower;
}

/// <summary>Um item do plano: nome atual, nome novo e, se houver, o motivo que impede aplicar.</summary>
public sealed record BatchRenameItem(string SourcePath, string OldName, string NewName, string? Problem)
{
    public bool IsUnchanged => Problem is null && string.Equals(OldName, NewName, StringComparison.Ordinal);
    public bool WillRename => Problem is null && !IsUnchanged;
}

/// <summary>
/// Plano de renomeação em lote (#71). A prévia e a execução usam o mesmo plano: o que a tela mostra é exatamente o que
/// será aplicado. Qualquer problema (nome inválido, dois itens com o mesmo nome novo, nome já usado na pasta) bloqueia o
/// lote inteiro antes de tocar no disco; nada é sobrescrito nem renomeado em cadeia.
/// </summary>
public sealed record BatchRenamePlan(IReadOnlyList<BatchRenameItem> Items)
{
    public int RenameCount => Items.Count(i => i.WillRename);
    public int ProblemCount => Items.Count(i => i.Problem is not null);

    /// <summary>Por que não dá para aplicar (null: pode aplicar).</summary>
    public string? BlockedReason => ProblemCount > 0
        ? $"{Core.Text.Plural.Of(ProblemCount, "item tem", "itens têm")} problema no nome novo; ajuste o padrão."
        : RenameCount == 0 ? "Nenhum nome muda com este padrão." : null;

    /// <param name="sources">Itens na ordem da lista (a numeração segue essa ordem).</param>
    /// <param name="existingNames">Todos os nomes da pasta agora (inclusive ocultos).</param>
    public static BatchRenamePlan Create(IReadOnlyList<BatchRenameSource> sources, IEnumerable<string> existingNames, BatchRenameOptions options)
    {
        var existing = new HashSet<string>(existingNames.Select(WindowsNameRules.CollisionKey), StringComparer.Ordinal);
        var names = sources.Select((s, i) => NewName(s, i, options)).ToList();
        var targets = names.GroupBy(WindowsNameRules.CollisionKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var items = new List<BatchRenameItem>(sources.Count);
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var old = source.Name;
            var name = names[i];
            string? problem = null;
            if (!string.Equals(old, name, StringComparison.Ordinal))
            {
                var key = WindowsNameRules.CollisionKey(name);
                var validation = WindowsNameRules.ValidateComponent(name);
                if (!validation.IsValid) problem = validation.Message;
                else if (targets[key] > 1) problem = $"Outro item marcado também ficaria \"{name}\".";
                else if (!string.Equals(key, WindowsNameRules.CollisionKey(old), StringComparison.Ordinal) && existing.Contains(key))
                    problem = $"Já existe \"{name}\" nesta pasta; nada é sobrescrito.";
            }
            items.Add(new BatchRenameItem(source.FullPath, old, name, problem));
        }
        return new BatchRenamePlan(items);
    }

    /// <summary>Nome novo de um item. A extensão de arquivos (último ponto) nunca muda.</summary>
    public static string NewName(BatchRenameSource source, int index, BatchRenameOptions options)
    {
        var name = source.Name;
        var extension = source.IsFile ? Path.GetExtension(name) : string.Empty;
        if (extension.Length == name.Length) extension = string.Empty; // ".gitignore": o nome todo é o nome
        var stem = name[..^extension.Length];
        var newStem = options.Mode switch
        {
            BatchRenameMode.Numbering => Number(options, index),
            BatchRenameMode.FindReplace => options.Find.Length == 0 ? stem
                : stem.Replace(options.Find, options.Replace, options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
            BatchRenameMode.PrefixSuffix => options.Prefix + stem + options.Suffix,
            _ => options.Case switch
            {
                BatchRenameCase.Upper => stem.ToUpper(CultureInfo.CurrentCulture),
                BatchRenameCase.Title => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(stem.ToLower(CultureInfo.CurrentCulture)),
                _ => stem.ToLower(CultureInfo.CurrentCulture),
            },
        };
        return newStem + extension;
    }

    private static string Number(BatchRenameOptions options, int index)
    {
        var number = ((long)options.Start + index).ToString(CultureInfo.InvariantCulture).PadLeft(options.Digits, '0');
        return options.BaseName.Length == 0 ? number : $"{options.BaseName} {number}";
    }
}
