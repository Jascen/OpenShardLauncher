using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Shared.Tests.Files;

public sealed class IgnoreRulesTests
{
    [Theory]
    // An unanchored name matches at any depth
    [InlineData("*.log", "debug.log", "debug.log")]
    [InlineData("*.log", "Logs/old/debug.log", "Logs/old/debug.log")]
    // ...and a folder of that name ignores everything inside it, reported as the topmost folder
    [InlineData("Music", "Data/Music/intro.mp3", "Data/Music/")]
    // A trailing / matches folders only
    [InlineData("Music/", "Data/Music/intro.mp3", "Data/Music/")]
    [InlineData("Music/", "Data/Music", null)]
    // A leading / or a / in the middle anchors to the top
    [InlineData("/art.mul", "art.mul", "art.mul")]
    [InlineData("/art.mul", "Data/art.mul", null)]
    [InlineData("Data/*.mul", "Data/art.mul", "Data/art.mul")]
    [InlineData("Data/*.mul", "Other/Data/art.mul", null)]
    // * and ? stay within one name
    [InlineData("Data/*.mul", "Data/sub/art.mul", null)]
    [InlineData("art?.mul", "art2.mul", "art2.mul")]
    [InlineData("art?.mul", "art22.mul", null)]
    // ** spans any number of folders, including none
    [InlineData("**/Music/*.mp3", "intro/Music/a.mp3", "intro/Music/a.mp3")]
    [InlineData("**/Music/*.mp3", "Music/a.mp3", "Music/a.mp3")]
    [InlineData("Data/**/*.mp3", "Data/a/b/c.mp3", "Data/a/b/c.mp3")]
    [InlineData("Data/**", "Data/a/b.txt", "Data/")]
    // Case is ignored
    [InlineData("MUSIC/", "data/music/a.mp3", "data/music/")]
    [InlineData("*.LOG", "debug.log", "debug.log")]
    // Backslashes are folder separators
    [InlineData(@"Data\*.mul", "Data/art.mul", "Data/art.mul")]
    public void Match(string pattern, string name, string? expected)
    {
        Assert.Equal(expected, IgnoreRules.Parse(pattern).Match(name));
    }

    [Fact]
    public void LaterNegation_BringsAFileBack()
    {
        var rules = IgnoreRules.Parse("""
            *.mp3
            !keep.mp3
            """);

        Assert.True(rules.IsIgnored("Music/a.mp3"));
        Assert.False(rules.IsIgnored("Music/keep.mp3"));
    }

    [Fact]
    public void Negation_CantBringBackAFileInsideAnIgnoredFolder()
    {
        var rules = IgnoreRules.Parse("""
            Music/
            !Music/keep.mp3
            """);

        Assert.Equal("Music/", rules.Match("Music/keep.mp3"));
    }

    [Fact]
    public void CommentsBlanksAndEscapes()
    {
        var rules = IgnoreRules.Parse("""
            # a comment

              spaced.txt
            \#literal.txt
            \!bang.txt
            """);

        Assert.False(rules.IsIgnored("# a comment"));
        Assert.True(rules.IsIgnored("spaced.txt"));
        Assert.True(rules.IsIgnored("#literal.txt"));
        Assert.True(rules.IsIgnored("!bang.txt"));
    }

    [Fact]
    public void Combine_LetsTheSecondListOverrideTheFirst()
    {
        var rules = IgnoreRules.Combine(IgnoreRules.Parse("appsettings*.json"), IgnoreRules.Parse("!Data/appsettings.json"));

        Assert.True(rules.IsIgnored("appsettings.json"));
        Assert.False(rules.IsIgnored("Data/appsettings.json"));
    }

    [Fact]
    public void EmptyRules_IgnoreNothing()
    {
        Assert.True(IgnoreRules.Parse("\n# only a comment\n").IsEmpty);
        Assert.Null(IgnoreRules.None.Match("anything"));
    }
}
