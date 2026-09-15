using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace SonarTray.Tests;

/// <summary>
/// Checks that every glyph the XAML asks for actually exists in the icon font.
///
/// A missing one renders as a tofu box - it does not throw, does not warn, and does not show up
/// in any build output. The project has shipped that bug before.
/// </summary>
public sealed class IconGlyphTests
{
    /// <summary>Matches the FontFamily in Themes/Dark.xaml.</summary>
    private static readonly string[] FontNames = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SonarTray.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Every &#xNNNN; escape across the XAML, plus the ones set from C#.</summary>
    private static IReadOnlyList<int> GlyphsInUse()
    {
        var root = RepoRoot();
        var codepoints = new SortedSet<int>();

        foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"&#x([0-9A-Fa-f]{4});"))
                codepoints.Add(int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        // The channel glyphs live in ChannelSpec as literal characters rather than escapes.
        foreach (var spec in SonarTray.Models.ChannelSpec.All)
            foreach (var ch in spec.Glyph)
                codepoints.Add(ch);

        return codepoints.ToList();
    }

    /// <summary>The first installed font from the family list, or null when none is present.</summary>
    private static GlyphTypeface? IconTypeface()
    {
        foreach (var name in FontNames)
        {
            var typeface = new Typeface(new FontFamily(name), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (typeface.TryGetGlyphTypeface(out var glyphTypeface)
                && glyphTypeface.FamilyNames.Values.Any(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase)))
            {
                return glyphTypeface;
            }
        }

        return null;
    }

    [Fact]
    public void EveryGlyphInUse_ExistsInTheIconFont()
    {
        // Both fonts ship with Windows 10 and 11, so a null here is itself worth failing on.
        var typeface = IconTypeface();
        Assert.True(typeface is not null, $"none of [{string.Join(", ", FontNames)}] is installed");

        var missing = GlyphsInUse()
                      .Where(cp => !typeface!.CharacterToGlyphMap.ContainsKey(cp))
                      .Select(cp => $"U+{cp:X4}")
                      .ToList();

        Assert.True(missing.Count == 0, $"the icon font has no glyph for {string.Join(", ", missing)}");
    }

    [Fact]
    public void TheCheckFindsTheGlyphsItShould()
    {
        // Guards against the scan returning nothing, which would make the test vacuous.
        var glyphs = GlyphsInUse();

        Assert.Contains(0xE767, glyphs);   // volume, on the master row
        Assert.Contains(0xE711, glyphs);   // clear
        Assert.True(glyphs.Count >= 10, $"only found {glyphs.Count} glyphs, expected the whole set");
    }

    [Fact]
    public void AnUnmappedCodepoint_IsReportedAsMissing()
    {
        // Proves the lookup says no to something the font does not have. U+E0FF sits in the
        // private-use area below the range these icon fonts actually populate.
        var typeface = IconTypeface();
        Assert.True(typeface is not null, "icon font not installed");

        Assert.False(typeface!.CharacterToGlyphMap.ContainsKey(0xE0FF));
    }
}
