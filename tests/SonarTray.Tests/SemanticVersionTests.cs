using SonarTray.Services;
using Xunit;

namespace SonarTray.Tests;

public sealed class SemanticVersionTests
{
    private static SemanticVersion Parse(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version), $"'{text}' should parse");
        return version;
    }

    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]          // release tags carry the v
    [InlineData("V1.2.3", 1, 2, 3, "")]
    [InlineData("0.1.0.0", 0, 1, 0, "")]         // what Assembly.Version looks like
    [InlineData("v0.2.0-beta.1", 0, 2, 0, "beta.1")]
    [InlineData("1.2.3+build7", 1, 2, 3, "")]    // build metadata carries no ordering
    [InlineData("1.2", 1, 2, 0, "")]
    public void TryParse_AcceptsTheShapesWeActuallySee(string text, int major, int minor, int patch, string pre)
    {
        var version = Parse(text);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(pre, version.PreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("banana")]
    [InlineData("1.2.x")]
    [InlineData("v")]
    [InlineData("1.2.3-")]          // a dash with nothing after it
    [InlineData("-1.2.3")]          // negative components are not a thing
    [InlineData("1.2.3.4.5")]
    public void TryParse_RejectsNonsense(string? text)
    {
        // Anything unparseable must be refused rather than silently read as 0.0.0, which would
        // make every release look newer than the running build.
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("0.9.9", "0.10.0")]   // not a string comparison
    public void Compare_OrdersByNumber(string lower, string higher)
    {
        Assert.True(Parse(lower).CompareTo(Parse(higher)) < 0);
        Assert.True(Parse(higher).CompareTo(Parse(lower)) > 0);
    }

    [Fact]
    public void Compare_TreatsEqualVersionsAsEqual()
    {
        Assert.Equal(0, Parse("1.2.3").CompareTo(Parse("v1.2.3")));
        Assert.Equal(0, Parse("0.1.0").CompareTo(Parse("0.1.0.0")));
    }

    [Fact]
    public void Compare_PreReleaseRanksBelowTheSameStableVersion()
    {
        // The reason this class exists: without it 0.2.0-beta.1 would look like 0.2.0 and a beta
        // would be offered to everyone running the stable build.
        Assert.True(Parse("0.2.0-beta.1").CompareTo(Parse("0.2.0")) < 0);
        Assert.True(Parse("0.2.0").CompareTo(Parse("0.2.0-beta.1")) > 0);
    }

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    [InlineData("1.0.0-beta.1", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.9", "1.0.0-beta.10")]   // numeric, not lexical
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]    // fewer identifiers rank lower
    [InlineData("1.0.0-1", "1.0.0-alpha")]          // numeric ranks below alphanumeric
    public void Compare_OrdersPreReleasesBySemverRules(string lower, string higher)
    {
        Assert.True(Parse(lower).CompareTo(Parse(higher)) < 0, $"{lower} should precede {higher}");
    }

    [Fact]
    public void Compare_AgainstNull_IsGreater()
    {
        Assert.True(Parse("1.0.0").CompareTo(null) > 0);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v0.2.0-beta.1", "0.2.0-beta.1")]
    public void ToString_RoundTrips(string input, string expected)
    {
        Assert.Equal(expected, Parse(input).ToString());
    }

    [Fact]
    public void IsPreRelease_ReflectsTheSuffix()
    {
        Assert.False(Parse("1.0.0").IsPreRelease);
        Assert.True(Parse("1.0.0-rc.1").IsPreRelease);
    }

    [Fact]
    public void Equals_MatchesCompareTo()
    {
        Assert.Equal(Parse("1.2.3"), Parse("v1.2.3"));
        Assert.NotEqual(Parse("1.2.3"), Parse("1.2.4"));
    }
}
