using System.Reflection;
using System.Text.RegularExpressions;
using SonarTray.ViewModels;
using Xunit;

namespace SonarTray.Tests;

/// <summary>
/// Checks that every simple {Binding Path} in the XAML names a real member on one of the view
/// models that view is ever given.
///
/// WPF resolves bindings at runtime and a path that matches nothing fails silently - a mistyped
/// Command binding produces a button that looks fine and does nothing at all. The compiler cannot
/// see it and the XAML-load test cannot either, because the binding is not evaluated until the
/// view has a DataContext.
/// </summary>
public sealed class BindingPathTests
{
    /// <summary>Which view models each view is ever bound against, including item templates.</summary>
    private static readonly Dictionary<string, Type[]> ViewModelsByView = new()
    {
        ["PopupWindow.xaml"] = new[] { typeof(MixerViewModel), typeof(ChannelViewModel),
                                        typeof(PresetsViewModel), typeof(SonarTray.Models.MixerPreset) },
        ["ChannelRow.xaml"] = new[] { typeof(ChannelViewModel), typeof(AudioDeviceItem), typeof(AudioProfileItem) },
        ["MasterRow.xaml"] = new[] { typeof(ChannelViewModel), typeof(AudioDeviceItem) },
        ["SettingsView.xaml"] = new[] { typeof(SettingsViewModel), typeof(MixerViewModel), typeof(LanguageOption) },
        ["HotkeySettingsView.xaml"] = new[] { typeof(HotkeySettingsViewModel), typeof(HotkeyBindingViewModel) },
    };

    /// <summary>
    /// Paths that do not resolve against a view model: element-to-element bindings, and the
    /// properties of plain WPF types reached through RelativeSource Self.
    /// </summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "SelectionBoxItem.Name",   // ComboBox's own property, via RelativeSource Self
        "IsChecked",               // ElementName binding to a ToggleButton
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SonarTray.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// Pulls the property path out of every {Binding ...} that has one.
    ///
    /// Only the first argument matters: a binding whose first argument is a named parameter
    /// (RelativeSource=, ElementName=, Source=) resolves against something other than the
    /// DataContext, and an attached-property path in parentheses is not a view-model member
    /// either. Both are skipped rather than reported.
    /// </summary>
    private static IEnumerable<string> BindingPaths(string xaml)
    {
        foreach (Match match in Regex.Matches(xaml, @"\{Binding\s+([^\s,}]+)"))
        {
            var token = match.Groups[1].Value;

            if (token.StartsWith("Path=", StringComparison.Ordinal))
                token = token["Path=".Length..];

            if (token.Length == 0) continue;
            if (token[0] == '(') continue;          // attached property, e.g. (TextElement.Foreground)
            if (token.Contains('=')) continue;      // a named parameter, not a path

            yield return token;
        }
    }

    private static bool Resolves(string path, IEnumerable<Type> candidates)
    {
        // "DataContext.Foo" reaches the window's view model; the leading hop is not a member.
        if (path.StartsWith("DataContext.", StringComparison.Ordinal))
            path = path["DataContext.".Length..];

        foreach (var type in candidates)
        {
            if (ResolvesOn(path, type)) return true;
        }

        return false;
    }

    private static bool ResolvesOn(string path, Type type)
    {
        var current = type;

        foreach (var segment in path.Split('.'))
        {
            var member = current.GetMember(segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                                .FirstOrDefault();
            if (member is null) return false;

            current = member switch
            {
                PropertyInfo property => property.PropertyType,
                FieldInfo field => field.FieldType,
                _ => current,
            };

            // Count is on the collection interfaces rather than the declared type.
            if (current.IsGenericType && current.GetInterfaces().Any(i => i.Name.StartsWith("ICollection", StringComparison.Ordinal)))
                continue;
        }

        return true;
    }

    public static TheoryData<string> Views()
    {
        var data = new TheoryData<string>();
        foreach (var view in ViewModelsByView.Keys) data.Add(view);
        return data;
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void EveryBindingPath_ResolvesToARealMember(string view)
    {
        var path = Path.Combine(RepoRoot(), "Views", view);
        Assert.True(File.Exists(path), $"{view} not found at {path}");

        var candidates = ViewModelsByView[view];
        var unresolved = BindingPaths(File.ReadAllText(path))
                         .Distinct(StringComparer.Ordinal)
                         .Where(p => !Ignored.Contains(p))
                         .Where(p => !Resolves(p, candidates))
                         .ToList();

        Assert.True(unresolved.Count == 0,
            $"{view} binds to {string.Join(", ", unresolved)}, which no bound view model exposes");
    }

    [Fact]
    public void TheCheckActuallyResolvesSomething()
    {
        // Guards against the regex silently matching nothing, which would make every view "pass".
        var path = Path.Combine(RepoRoot(), "Views", "ChannelRow.xaml");
        var paths = BindingPaths(File.ReadAllText(path)).ToList();

        Assert.Contains("Volume", paths);
        Assert.Contains("IsMuted", paths);
    }

    [Fact]
    public void AMistypedPath_IsReported()
    {
        // Proves the resolver says no to something that does not exist.
        Assert.False(Resolves("NoSuchPropertyAtAll", new[] { typeof(MixerViewModel) }));
    }
}
