using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SonarTray.Services;

/// <summary>
/// Just enough of semver to compare a release tag with the running build.
///
/// <see cref="Version"/> would almost do, but it has no notion of a pre-release suffix and would
/// rank 0.2.0-beta.1 as equal to 0.2.0 - which would offer a beta to everyone on the stable build.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>Empty for a stable release; "beta.1" for 0.2.0-beta.1.</summary>
    public string PreRelease { get; }

    public bool IsPreRelease => PreRelease.Length > 0;

    private SemanticVersion(int major, int minor, int patch, string preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    /// <summary>Accepts "1.2.3", "v1.2.3" and "v1.2.3-beta.1"; build metadata is ignored.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var span = text.Trim();
        if (span.StartsWith("v", StringComparison.OrdinalIgnoreCase)) span = span[1..];

        // "+build" carries no ordering information in semver, so drop it before anything else.
        int plus = span.IndexOf('+');
        if (plus >= 0) span = span[..plus];

        string preRelease = "";
        int dash = span.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = span[(dash + 1)..];
            span = span[..dash];
            if (preRelease.Length == 0) return false;
        }

        var parts = span.Split('.');
        if (parts.Length is < 1 or > 4) return false;

        // A four-part number is what Assembly.GetName().Version produces (0.1.0.0); the fourth
        // component is not part of semver and is deliberately discarded.
        if (!TryPart(parts, 0, out int major)) return false;
        if (!TryPart(parts, 1, out int minor)) return false;
        if (!TryPart(parts, 2, out int patch)) return false;
        if (parts.Length == 4 && !TryPart(parts, 3, out _)) return false;

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;

        static bool TryPart(string[] parts, int index, out int value)
        {
            value = 0;
            if (index >= parts.Length) return true; // missing trailing components default to 0
            return int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // 1.0.0-beta precedes 1.0.0; two stable versions here are equal.
        if (!IsPreRelease && !other.IsPreRelease) return 0;
        if (!IsPreRelease) return 1;
        if (!other.IsPreRelease) return -1;

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>
    /// Dot-separated identifiers, compared left to right. Numeric identifiers compare as numbers
    /// (so beta.9 precedes beta.10) and always rank below alphanumeric ones.
    /// </summary>
    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');

        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            if (i >= a.Length) return -1; // a shorter set of identifiers ranks lower
            if (i >= b.Length) return 1;

            bool aNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out int an);
            bool bNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out int bn);

            int result;
            if (aNumeric && bNumeric) result = an.CompareTo(bn);
            else if (aNumeric) result = -1;
            else if (bNumeric) result = 1;
            else result = string.CompareOrdinal(a[i], b[i]);

            if (result != 0) return result;
        }

        return 0;
    }

    public override string ToString()
        => IsPreRelease
            ? $"{Major}.{Minor}.{Patch}-{PreRelease}"
            : $"{Major}.{Minor}.{Patch}";

    public override bool Equals(object? obj) => obj is SemanticVersion other && CompareTo(other) == 0;

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);
}
