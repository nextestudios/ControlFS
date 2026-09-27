using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using ControlFS.Core.Models;
using ControlFS.Infrastructure.Archives.Security;

namespace ControlFS.Infrastructure.Archives.Inspection;

/// <summary>
/// Volumes de um compactado dividido, reunidos a partir de qualquer um deles: "x.7z.001", "x.zip.001"… (divisão simples),
/// "x.part1.rar"…, "x.rar" + "x.r00"… (RAR antigo) e "x.z01"… + "x.zip" (ZIP dividido). <see cref="Missing"/> lista os
/// volumes que faltam: lacunas na numeração e, quando o formato diz quantos são, os do fim (7z pelo tamanho declarado no
/// cabeçalho, RAR pelo aviso de "há mais volumes" no fim do último, ZIP pelo número do disco no registro final).
/// </summary>
internal sealed partial record VolumeSet(IReadOnlyList<string> Parts, IReadOnlyList<string> Missing, ArchiveFormat NameHint)
{
    private const int MaxVolumes = 9999;

    /// <summary>Volume cujo conteúdo identifica o formato (o primeiro existente em ordem de gravação).</summary>
    public string? DetectionPath => Parts.Count == 0 ? null
        : IsSpannedZip ? Parts[1] // ZIP dividido: o ".zip" é o último
        : Parts[0];

    public bool IsMultiPart => Parts.Count + Missing.Count > 1;

    /// <summary>ZIP dividido do Info-ZIP/WinZip: ".zip" (último disco) seguido de ".z01"….</summary>
    public bool IsSpannedZip => Parts.Count > 1 && Parts[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static VolumeSet? Find(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (dir is null || !Directory.Exists(dir)) return null;
        try
        {
            if (RarPart().Match(name) is { Success: true } part) return RarParts(dir, part.Groups[1].Value, part.Groups[2].Value.Length);
            if (OldRar().Match(name) is { Success: true } old) return OldRarSet(dir, old.Groups[1].Value);
            if (ZipSpan().Match(name) is { Success: true } span) return SpannedZip(dir, span.Groups[1].Value);
            if (Numbered().Match(name) is { Success: true } numbered) return NumberedSet(dir, numbered.Groups[1].Value, numbered.Groups[2].Value.Length);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Volumes presentes com o mesmo prefixo, pelo número (o padrão de <paramref name="suffix"/> captura o número).</summary>
    private static SortedDictionary<int, string> Siblings(string dir, string prefix, Regex suffix)
    {
        var found = new SortedDictionary<int, string>();
        foreach (var file in Directory.EnumerateFiles(dir, prefix + ".*"))
        {
            var name = Path.GetFileName(file);
            if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var m = suffix.Match(name[prefix.Length..]);
            if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n > MaxVolumes) continue;
            found.TryAdd(n, file);
        }
        return found;
    }

    private static VolumeSet? RarParts(string dir, string prefix, int width)
    {
        var found = Siblings(dir, prefix, PartSuffix());
        string NameOf(int n) => $"{prefix}.part{n.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}.rar";
        if (found.Count == 1 && found.TryGetValue(1, out var only) && RarTail.MoreVolumesFollow(only) is not true) return null; // "x.part1.rar" comum
        return Sequential(found, 1, NameOf, ArchiveFormat.Rar, rarTail: true);
    }

    private static VolumeSet? OldRarSet(string dir, string prefix)
    {
        // ".rar" é o volume 0; ".r00"…".r99" são 1…100; ".s00"… seguem.
        var found = new SortedDictionary<int, string>();
        var first = Path.Join(dir, prefix + ".rar");
        if (File.Exists(first)) found[0] = first;
        foreach (var file in Directory.EnumerateFiles(dir, prefix + ".*"))
        {
            var name = Path.GetFileName(file);
            if (name.Length != prefix.Length + 4 || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (OldRarSuffix().Match(name[prefix.Length..]) is not { Success: true } m) continue;
            var series = char.ToLowerInvariant(m.Groups[1].Value[0]) == 'r' ? 0 : 100;
            found.TryAdd(series + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) + 1, file);
        }
        if (found.Count == 1 && found.ContainsKey(0) && RarTail.MoreVolumesFollow(first) is not true) return null; // "x.rar" comum
        string NameOf(int n) => n == 0 ? prefix + ".rar" : $"{prefix}.{(char)('r' + (n - 1) / 100)}{((n - 1) % 100).ToString("D2", CultureInfo.InvariantCulture)}";
        return Sequential(found, 0, NameOf, ArchiveFormat.Rar, rarTail: true);
    }

    private static VolumeSet? SpannedZip(string dir, string prefix)
    {
        var zips = Siblings(dir, prefix, ZipSuffix());
        var last = Path.Join(dir, prefix + ".zip");
        var hasLast = File.Exists(last);
        var lastDisk = hasLast ? ZipLastDisk(last) : null;
        if (zips.Count == 0 && lastDisk is null or 0) return null; // ".zip" comum
        var expected = Math.Max(zips.Count == 0 ? 0 : zips.Keys.Max(), lastDisk ?? 0);
        string NameOf(int n) => $"{prefix}.z{n.ToString("D2", CultureInfo.InvariantCulture)}";
        var parts = new List<string>();
        var missing = new List<string>();
        if (hasLast) parts.Add(last); else missing.Add(prefix + ".zip");
        for (var n = 1; n <= expected; n++)
        {
            if (zips.TryGetValue(n, out var file)) parts.Add(file);
            else missing.Add(NameOf(n));
        }
        return new VolumeSet(parts, missing, ArchiveFormat.Zip);
    }

    private static VolumeSet? NumberedSet(string dir, string prefix, int width)
    {
        var found = Siblings(dir, prefix, NumberSuffix());
        if (found.Count == 0) return null;
        string NameOf(int n) => $"{prefix}.{n.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}";
        var hint = HintFromName(prefix);
        var set = Sequential(found, 1, NameOf, hint, rarTail: false);
        if (set is null || set.Missing.Count > 0 || !found.TryGetValue(1, out var first)) return set;
        // Divisão simples: o formato do primeiro volume diz se dá para saber onde o conjunto termina.
        var next = NameOf(found.Keys.Max() + 1);
        ArchiveFormat format;
        using (var stream = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read, 1))
            format = FormatDetector.Detect(stream);
        var total = found.Values.Sum(f => new FileInfo(f).Length);
        if (format == ArchiveFormat.SevenZip && SevenZipDeclaredLength(first) is long declared && total < declared)
            return set with { Missing = [next] };
        if (format == ArchiveFormat.Rar && RarTail.MoreVolumesFollow(found.Values.Last()) is true)
            return set with { Missing = [next] };
        return set;
    }

    private static ArchiveFormat HintFromName(string stem) =>
        stem.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.SevenZip
        : stem.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Zip
        : stem.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ? ArchiveFormat.Rar
        : ArchiveFormat.Unknown;

    /// <summary>Volumes numerados a partir de <paramref name="firstNumber"/>, com lacunas e (RAR) o volume seguinte ao último.</summary>
    private static VolumeSet? Sequential(SortedDictionary<int, string> found, int firstNumber, Func<int, string> nameOf, ArchiveFormat hint, bool rarTail)
    {
        if (found.Count == 0) return null;
        var max = found.Keys.Max();
        var parts = new List<string>();
        var missing = new List<string>();
        for (var n = firstNumber; n <= max; n++)
        {
            if (found.TryGetValue(n, out var file)) parts.Add(file);
            else missing.Add(nameOf(n));
        }
        if (rarTail && RarTail.MoreVolumesFollow(found[max]) is true) missing.Add(nameOf(max + 1));
        return new VolumeSet(parts, missing, hint);
    }

    /// <summary>7z: o cabeçalho inicial diz onde termina o cabeçalho final (32 + deslocamento + tamanho).</summary>
    private static long? SevenZipDeclaredLength(string firstVolume)
    {
        Span<byte> header = stackalloc byte[32];
        using var stream = new FileStream(firstVolume, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        if (stream.ReadAtLeast(header, 32, throwOnEndOfStream: false) < 32) return null;
        var offset = BinaryPrimitives.ReadUInt64LittleEndian(header[12..]);
        var size = BinaryPrimitives.ReadUInt64LittleEndian(header[20..]);
        if (offset > long.MaxValue / 4 || size > long.MaxValue / 4) return null;
        return 32 + (long)offset + (long)size;
    }

    /// <summary>ZIP: número do disco do registro final (EOCD) do ".zip", que é o último volume; null se não achar ou ZIP64.</summary>
    private static int? ZipLastDisk(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1);
        var length = (int)Math.Min(stream.Length, 22 + ushort.MaxValue);
        if (length < 22) return null;
        var tail = new byte[length];
        stream.Seek(-length, SeekOrigin.End);
        stream.ReadExactly(tail);
        for (var i = length - 22; i >= 0; i--)
        {
            if (tail[i] != 'P' || tail[i + 1] != 'K' || tail[i + 2] != 5 || tail[i + 3] != 6) continue;
            var disk = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 4));
            return disk == ushort.MaxValue ? null : disk;
        }
        return null;
    }

    [GeneratedRegex(@"^(.+)\.part(\d{1,4})\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RarPart();

    [GeneratedRegex(@"^\.part(\d{1,4})\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartSuffix();

    [GeneratedRegex(@"^(.+)\.(?:rar|[rs]\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OldRar();

    [GeneratedRegex(@"^\.([rs])(\d{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OldRarSuffix();

    [GeneratedRegex(@"^(.+)\.(?:zip|z\d{2,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ZipSpan();

    [GeneratedRegex(@"^\.z(\d{2,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ZipSuffix();

    [GeneratedRegex(@"^(.+)\.(\d{3,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^\.(\d{3,4})$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberSuffix();
}
