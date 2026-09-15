using System.Windows;
using Xunit;

namespace SonarTray.Tests;

/// <summary>
/// Instantiates every view so the XAML is actually parsed. A misspelled StaticResource key or a
/// malformed binding is not a compile error - it throws the first time the view is built, which
/// without this test means "on the user's machine, when they open that page".
/// </summary>
public sealed class XamlLoadTests
{
    /// <summary>
    /// WPF needs an STA thread and an Application whose resources hold the theme, because the
    /// views resolve their brushes and control styles through StaticResource.
    /// </summary>
    private static void OnStaThreadWithTheme(Action body)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/SonarTray;component/Themes/Dark.xaml", UriKind.Absolute),
                });

                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");

        if (failure is not null) throw new Xunit.Sdk.XunitException($"view failed to load: {failure}");
    }

    [Fact]
    public void Theme_Loads()
    {
        OnStaThreadWithTheme(() => { });
    }

    [Fact]
    public void SettingsView_Loads()
    {
        OnStaThreadWithTheme(() => Assert.NotNull(new SonarTray.Views.SettingsView()));
    }

    [Fact]
    public void HotkeySettingsView_Loads()
    {
        OnStaThreadWithTheme(() => Assert.NotNull(new SonarTray.Views.HotkeySettingsView()));
    }

    [Fact]
    public void ChannelRow_Loads()
    {
        OnStaThreadWithTheme(() => Assert.NotNull(new SonarTray.Views.ChannelRow()));
    }

    [Fact]
    public void MasterRow_Loads()
    {
        OnStaThreadWithTheme(() => Assert.NotNull(new SonarTray.Views.MasterRow()));
    }

    [Fact]
    public void PopupWindow_Loads()
    {
        // The biggest XAML file in the project and the one most often edited. Building it needs a
        // whole view-model graph, which is why it is the last view to get this coverage rather
        // than the first.
        OnStaThreadWithTheme(() =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "SonarTrayTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using var connection = new SonarTray.Services.SonarConnection();

                // Every gesture cleared: registering the real ones would collide with whatever
                // copy of SonarTray the developer has running.
                var hotkeys = new SonarTray.Hotkeys.HotkeyConfig { SourcePath = Path.Combine(dir, "hotkeys.json") };
                foreach (var action in Enum.GetValues<SonarTray.Hotkeys.HotkeyAction>())
                    hotkeys.Set(action, string.Empty);

                using var manager = new SonarTray.Hotkeys.HotkeyManager(hotkeys, _ => { });

                var settings = new SonarTray.Services.AppSettings { SourcePath = Path.Combine(dir, "settings.json") };
                var settingsVm = new SonarTray.ViewModels.SettingsViewModel(settings, hotkeys, () => { });
                var presets = SonarTray.Services.PresetStore.Load(Path.Combine(dir, "presets.json"));

                var mixer = new SonarTray.ViewModels.MixerViewModel(
                    connection,
                    new SonarTray.ViewModels.HotkeySettingsViewModel(hotkeys, manager),
                    settingsVm, presets,
                    exit: () => { }, openGg: () => { });

                var window = new SonarTray.Views.PopupWindow(mixer);
                Assert.NotNull(window);
                window.AllowClose = true;
                window.Close();
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        });
    }

    [Fact]
    public void OsdWindow_Loads()
    {
        OnStaThreadWithTheme(() =>
        {
            var window = new SonarTray.Views.OsdWindow();
            Assert.NotNull(window);
            window.AllowClose = true;
            window.Close();
        });
    }
}
