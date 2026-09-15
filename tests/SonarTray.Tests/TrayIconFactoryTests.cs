using SonarTray.Tray;
using Xunit;

namespace SonarTray.Tests;

public sealed class TrayIconFactoryTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.19, 0)]
    [InlineData(0.2, 1)]
    [InlineData(0.5, 2)]
    [InlineData(0.99, 4)]
    [InlineData(1.0, 4)]        // must not spill into a sixth bucket
    public void LevelFor_QuantisesIntoFiveBuckets(double volume, int expected)
    {
        Assert.Equal(expected, TrayIconFactory.LevelFor(volume));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(2.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void LevelFor_StaysInRangeForNonsenseInput(double volume)
    {
        var level = TrayIconFactory.LevelFor(volume);

        Assert.InRange(level, 0, TrayIconFactory.MaxLevel);
    }

    [Fact]
    public void LevelFor_IsMonotonic()
    {
        int previous = 0;
        for (double v = 0.0; v <= 1.0; v += 0.01)
        {
            var level = TrayIconFactory.LevelFor(v);
            Assert.True(level >= previous, $"level went backwards at {v}");
            previous = level;
        }
    }

    [Fact]
    public void LevelFor_UsesEveryBucket()
    {
        var seen = new HashSet<int>();
        for (double v = 0.0; v <= 1.0; v += 0.005) seen.Add(TrayIconFactory.LevelFor(v));

        Assert.Equal(TrayIconFactory.MaxLevel + 1, seen.Count);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void Create_ProducesADisposableIconAtTheRequestedSize(int size)
    {
        using var owned = TrayIconFactory.Create(size, IconTone.Online, level: 3, muted: false);

        Assert.Equal(size, owned.Icon.Width);
        Assert.Equal(size, owned.Icon.Height);
    }

    [Fact]
    public void Create_HandlesEveryToneAndLevelWithoutThrowing()
    {
        // IconTone is internal, so this cannot be a [Theory] parameter; the loop covers the matrix.
        foreach (var tone in Enum.GetValues<IconTone>())
        {
            for (int level = 0; level <= TrayIconFactory.MaxLevel; level++)
            {
                using var normal = TrayIconFactory.Create(16, tone, level, muted: false);
                using var muted = TrayIconFactory.Create(16, tone, level, muted: true);
                Assert.NotNull(normal.Icon);
                Assert.NotNull(muted.Icon);
            }
        }
    }
}
