using System.Runtime.CompilerServices;
using SonarTray.Services;

namespace SonarTray.Tests;

internal static class TestSetup
{
    /// <summary>
    /// Runs before any test. Several tests feed deliberately corrupt config files to prove the
    /// fallbacks work, and every one of those logs an exception - which, without this, lands in
    /// the developer's real %LOCALAPPDATA%\SonarTray\sonartray.log looking like a genuine crash.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialise()
    {
        var path = Path.Combine(Path.GetTempPath(), "SonarTrayTests", "test-run.log");
        Log.UseFile(path);
    }
}
