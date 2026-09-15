using System.Globalization;
using System.Resources;
using SonarTray.Models;
using SonarTray.Resources;
using Xunit;

namespace SonarTray.Tests;

public sealed class LocalisationTests : IDisposable
{
    public void Dispose() => Strings.Culture = null;

    private static IReadOnlyCollection<string> KeysIn(string baseName)
    {
        var manager = new ResourceManager(baseName, typeof(Strings).Assembly);
        using var set = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
                        ?? throw new InvalidOperationException($"resource set '{baseName}' is missing");

        return set.Cast<System.Collections.DictionaryEntry>()
                  .Select(e => (string)e.Key)
                  .ToList();
    }

    [Fact]
    public void BothLanguages_ExposeExactlyTheSameKeys()
    {
        // A key present in one table and missing from the other shows up as raw "Panel_Exit"
        // text in the UI for whoever is using the other language.
        var english = KeysIn("SonarTray.Resources.Strings").ToHashSet(StringComparer.Ordinal);
        var turkish = KeysIn("SonarTray.Resources.Strings_tr").ToHashSet(StringComparer.Ordinal);

        Assert.Equal(english, turkish);
    }

    [Fact]
    public void NeitherLanguage_HasAnEmptyValue()
    {
        foreach (var baseName in new[] { "SonarTray.Resources.Strings", "SonarTray.Resources.Strings_tr" })
        {
            var manager = new ResourceManager(baseName, typeof(Strings).Assembly);
            foreach (var key in KeysIn(baseName))
                Assert.False(string.IsNullOrWhiteSpace(manager.GetString(key, CultureInfo.InvariantCulture)),
                             $"{baseName}: '{key}' is empty");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("de")]      // any unsupported language falls back to English
    [InlineData("fr-CA")]
    public void Get_ReturnsEnglishForEverythingButTurkish(string culture)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);

        Assert.Equal("Exit", Strings.Panel_Exit);
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("tr-TR")]   // the two-letter code is what matters, not the region
    public void Get_ReturnsTurkishForTurkishCultures(string culture)
    {
        Strings.Culture = CultureInfo.GetCultureInfo(culture);

        Assert.Equal("Çıkış", Strings.Panel_Exit);
    }

    [Fact]
    public void Get_UnknownKey_ReturnsTheKeyRatherThanThrowing()
    {
        Assert.Equal("No_Such_Key", Strings.Get("No_Such_Key"));
    }

    [Fact]
    public void Format_PutsThePercentSignWhereEachLanguageWantsIt()
    {
        // Turkish writes %70, English writes 70% - the whole reason this is a format string.
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Equal("70%", Strings.Format("Osd_Percent", 70));

        Strings.Culture = CultureInfo.GetCultureInfo("tr");
        Assert.Equal("%70", Strings.Format("Osd_Percent", 70));
    }

    [Fact]
    public void Format_TrayVolume_SubstitutesTheNumber()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");

        Assert.Contains("42", Strings.Format("Tray_Volume", 42), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryChannelLabelKey_Resolves()
    {
        // ChannelSpec stores resource keys; a typo there would print "Channel_Game" in the mixer.
        foreach (var spec in ChannelSpec.All)
            Assert.NotEqual(spec.LabelKey, Strings.Get(spec.LabelKey));
    }

    [Fact]
    public void EveryHotkeyActionHasAnActionString()
    {
        foreach (var action in Enum.GetValues<SonarTray.Hotkeys.HotkeyAction>())
        {
            var key = $"Action_{action}";
            Assert.NotEqual(key, Strings.Get(key));
        }
    }

    [Fact]
    public void SupportedLanguages_MatchesWhatIsActuallyEmbedded()
    {
        Assert.Equal(new[] { "en", "tr" }, Strings.SupportedLanguages);
    }
}
