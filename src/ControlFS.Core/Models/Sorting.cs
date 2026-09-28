namespace ControlFS.Core.Models;

public enum SortField
{
    Name,
    Type,
    Size,
    Modified,
}

public sealed record SortOrder(SortField Field = SortField.Name, bool Descending = false)
{
    public static SortOrder Default { get; } = new();

    public IComparer<FileEntry> CreateComparer() => new EntryComparer(this);

    private sealed class EntryComparer(SortOrder order) : IComparer<FileEntry>
    {
        public int Compare(FileEntry? x, FileEntry? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            // Contêineres sempre antes de arquivos, independentemente da direção.
            var containers = y.IsContainer.CompareTo(x.IsContainer);
            if (containers != 0) return containers;

            var result = order.Field switch
            {
                SortField.Type => StringComparer.OrdinalIgnoreCase.Compare(x.Extension, y.Extension),
                SortField.Size => Nullable.Compare(x.Size, y.Size),
                SortField.Modified => Nullable.Compare(x.Modified, y.Modified),
                // Unidades (Meu computador) pela letra, como no Explorador, não pelo rótulo.
                _ when x.Kind == EntryKind.Drive && y.Kind == EntryKind.Drive => StringComparer.OrdinalIgnoreCase.Compare(x.FullPath, y.FullPath),
                _ => 0,
            };
            if (result == 0) result = NaturalNameComparer.Instance.Compare(x.Name, y.Name);
            if (result == 0) result = StringComparer.Ordinal.Compare(x.Id, y.Id);
            return order.Descending ? -result : result;
        }
    }
}

/// <summary>Comparação de nomes insensível a caixa que ordena "arquivo2" antes de "arquivo10".</summary>
public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer Instance { get; } = new();

    private static readonly bool AsciiFast = System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName is not ("tr" or "az");

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.CompareOrdinal(x, y);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var a = x.AsSpan(si, i - si).TrimStart('0');
                var b = y.AsSpan(sj, j - sj).TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                var cmp = a.SequenceCompareTo(b);
                if (cmp != 0) return cmp;
                continue;
            }
            var cx = x[i];
            var cy = y[j];
            // Letra ASCII contra letra ASCII: mesma ordem da comparação de cultura (alfabética, sem caixa), sem chamar o
            // sistema por caractere (uma pasta de 5.000 arquivos faz centenas de milhares de comparações).
            // Turco e azeri têm regras próprias para "i": ficam no caminho de cultura.
            var c = AsciiFast && char.IsAsciiLetter(cx) && char.IsAsciiLetter(cy)
                ? (cx | 0x20).CompareTo(cy | 0x20)
                : string.Compare(x, i, y, j, 1, StringComparison.CurrentCultureIgnoreCase);
            if (c != 0) return c;
            i++;
            j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
