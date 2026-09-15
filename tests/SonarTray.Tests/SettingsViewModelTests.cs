using SonarTray.Hotkeys;
using SonarTray.Resources;
using SonarTray.Services;
using SonarTray.ViewModels;
using Xunit;

namespace SonarTray.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SonarTrayTests", Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings;
    private readonly HotkeyConfig _hotkeys;
    private int _visibilityCallbacks;

    public SettingsViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        // SourcePath is what makes the parameterless Save() land here instead of in the real
        // %LOCALAPPDATA%\SonarTray. Without it these tests overwrite the developer's own config.
        _settings = new AppSettings { SourcePath = Path.Combine(_dir, "settings.json") };
        _hotkeys = new HotkeyConfig { SourcePath = Path.Combine(_dir, "hotkeys.json") };
    }

    private SettingsViewModel Build() => new(_settings, _hotkeys, () => _visibilityCallbacks++);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* cleanup noise is not interesting */ }
    }

    [Fact]
    public void Languages_StartWithAutoThenEverySupportedLanguage()
    {
        var vm = Build();

        Assert.Equal("auto", vm.Languages[0].Code);
        Assert.Equal(Strings.SupportedLanguages, vm.Languages.Skip(1).Select(l => l.Code).ToList());
    }

    [Fact]
    public void Languages_AreLabelledInTheirOwnLanguage()
    {
        var vm = Build();

        // Someone looking for Turkish looks for "Türkçe", not for "Turkish".
        Assert.Contains(vm.Languages, l => l.Code == "tr" && l.Display.StartsWith("Tü", StringComparison.Ordinal));
        Assert.Contains(vm.Languages, l => l.Code == "en" && l.Display.StartsWith("En", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectedLanguage_DefaultsToAuto()
    {
        Assert.Equal("auto", Build().SelectedLanguage.Code);
    }

    [Fact]
    public void SelectedLanguage_UnknownStoredValue_FallsBackToAuto()
    {
        _settings.Language = "klingon";

        Assert.Equal("auto", Build().SelectedLanguage.Code);
    }

    [Fact]
    public void SelectedLanguage_ChangingIt_RaisesTheRestartHint()
    {
        var vm = Build();
        Assert.False(vm.ShowRestartHint);

        vm.SelectedLanguage = vm.Languages.Single(l => l.Code == "tr");

        Assert.True(vm.ShowRestartHint);
        Assert.Equal("tr", _settings.Language);
    }

    [Fact]
    public void SelectedLanguage_SettingTheSameValue_DoesNothing()
    {
        var vm = Build();

        vm.SelectedLanguage = vm.Languages.Single(l => l.Code == "auto");

        Assert.False(vm.ShowRestartHint);
    }

    [Fact]
    public void ShowAux_NotifiesTheMixerOnlyWhenItActuallyChanges()
    {
        var vm = Build();

        vm.ShowAux = true;
        vm.ShowAux = true;   // same value again

        Assert.Equal(1, _visibilityCallbacks);
        Assert.True(_settings.ShowAux);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(0, 1)]       // clamped up
    [InlineData(-20, 1)]
    [InlineData(80, 50)]     // clamped down
    public void VolumeStepPercent_IsClampedToAUsableRange(double set, double expected)
    {
        var vm = Build();

        vm.VolumeStepPercent = set;

        Assert.Equal(expected, vm.VolumeStepPercent);
    }

    [Fact]
    public void VolumeStepPercent_StoresAFractionNotAPercentage()
    {
        var vm = Build();

        vm.VolumeStepPercent = 15;

        Assert.Equal(0.15, _hotkeys.VolumeStep, precision: 6);
    }

    [Fact]
    public void VolumeStepPercent_ReadsBackTheStoredFraction()
    {
        _hotkeys.VolumeStep = 0.25;

        Assert.Equal(25, Build().VolumeStepPercent);
    }

    [Theory]
    [InlineData(1.5, 1.5)]
    [InlineData(0.1, 0.3)]      // clamped up
    [InlineData(99.0, 10.0)]    // clamped down
    public void OsdSeconds_IsClampedToAUsableRange(double set, double expected)
    {
        var vm = Build();

        vm.OsdSeconds = set;

        Assert.Equal(expected, vm.OsdSeconds, precision: 3);
    }

    [Fact]
    public void OsdSeconds_StoresMilliseconds()
    {
        var vm = Build();

        vm.OsdSeconds = 2.5;

        Assert.Equal(2500, _settings.OsdDurationMs);
    }

    [Fact]
    public void OsdSettingsChanged_FiresForBothOsdSettings()
    {
        var vm = Build();
        int raised = 0;
        vm.OsdSettingsChanged += () => raised++;

        vm.OsdEnabled = false;
        vm.OsdSeconds = 3.0;

        Assert.Equal(2, raised);
    }

    [Fact]
    public void OsdSettingsChanged_DoesNotFireForUnrelatedSettings()
    {
        var vm = Build();
        int raised = 0;
        vm.OsdSettingsChanged += () => raised++;

        vm.TrayWheelVolume = false;
        vm.CheckForUpdates = false;
        vm.ShowAux = true;

        Assert.Equal(0, raised);
    }

    [Fact]
    public void TrayWheelChanged_FiresSoTheHookCanBeAddedOrRemovedImmediately()
    {
        var vm = Build();
        int raised = 0;
        vm.TrayWheelChanged += () => raised++;

        vm.TrayWheelVolume = false;
        vm.TrayWheelVolume = false;   // same value again
        vm.TrayWheelVolume = true;

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Toggles_RoundTripThroughTheSettingsObject()
    {
        var vm = Build();

        vm.TrayWheelVolume = false;
        vm.CheckForUpdates = false;
        vm.OsdEnabled = false;

        Assert.False(_settings.TrayWheelVolume);
        Assert.False(_settings.CheckForUpdates);
        Assert.False(_settings.OsdEnabled);
    }
}
