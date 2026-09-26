using System.Globalization;

namespace ControlFS.Core.Models;

/// <summary>Versão SemVer 2.0 (MAJOR.MINOR.PATCH[-pré-release]); metadados de build (+...) são ignorados.</summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private ReleaseVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public IReadOnlyList<string> Prerelease { get; }
    public bool IsPrerelease => Prerelease.Count > 0;

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64) return false;
        var s = text.Trim().TrimStart('v', 'V');
        var plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0) s = s[..plus];
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        var core = dash >= 0 ? s[..dash] : s;
        var parts = core.Split('.');
        if (parts.Length != 3) return false;
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
            if (!IsNumeric(parts[i]) || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        var pre = Array.Empty<string>();
        if (dash >= 0)
        {
            pre = s[(dash + 1)..].Split('.');
            foreach (var id in pre)
                if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') || (IsNumeric(id) && id.Length > 1 && id[0] == '0')) return false;
        }
        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public static ReleaseVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"Versão inválida: {text}");

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;   // 1.0.0 > 1.0.0-alpha
        if (!other.IsPrerelease) return -1;
        for (var i = 0; i < Math.Min(Prerelease.Count, other.Prerelease.Count); i++)
        {
            var a = Prerelease[i];
            var b = other.Prerelease[i];
            var an = IsNumeric(a);
            var bn = IsNumeric(b);
            c = an && bn ? CompareNumeric(a, b) : an ? -1 : bn ? 1 : string.CompareOrdinal(a, b);
            if (c != 0) return Math.Sign(c);
        }
        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public bool Equals(ReleaseVersion? other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is ReleaseVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Prerelease));
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-" + string.Join('.', Prerelease) : string.Empty);

    public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) <= 0;
    public static bool operator ==(ReleaseVersion? a, ReleaseVersion? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(ReleaseVersion? a, ReleaseVersion? b) => !(a == b);

    private static bool IsNumeric(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);

    private static int CompareNumeric(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    }
}

/// <summary>Arquivo publicado numa release, com integridade declarada no manifesto assinado.</summary>
public sealed record ReleaseAsset(string Name, long Size, string Sha256);

/// <summary>Manifesto de release já com assinatura verificada.</summary>
public sealed record UpdateManifest(ReleaseVersion Version, string Repository, DateTimeOffset? ReleasedAt, ReleaseAsset Installer, ReleaseAsset? Portable, string ReleasePageUrl);

public enum UpdateCheckOutcome
{
    UpToDate,
    UpdateAvailable,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, UpdateManifest? Manifest = null, string? Message = null);

/// <summary>Instalador baixado e com SHA-256 e tamanho conferidos contra o manifesto assinado.</summary>
public sealed record ReadyUpdate(UpdateManifest Manifest, string InstallerPath);
