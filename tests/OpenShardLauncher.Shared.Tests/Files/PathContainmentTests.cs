using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Shared.Tests.Files;

public sealed class PathContainmentTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "osl-containment", "Game");

    [Theory]
    [InlineData("art.mul")]
    [InlineData("Data/Music/intro.mp3")]
    [InlineData("Data\\Music\\intro.mp3")]
    [InlineData("Data/../art.mul")]
    public void NamesInsideTheRoot_AreAccepted(string name)
    {
        Assert.True(PathContainment.TryResolve(Root, name, out var full));
        Assert.StartsWith(Root + Path.DirectorySeparatorChar, full, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("Data/../../outside.txt")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("Data/..")]
    [InlineData("")]
    [InlineData("a\0b")]
    // Shares the root's prefix ("Game" vs "Game2") without being inside it
    [InlineData("../Game2/art.mul")]
    public void NamesOutsideTheRoot_AreRejected(string name)
    {
        Assert.False(PathContainment.TryResolve(Root, name, out _));
    }

    [Fact]
    public void AbsoluteNames_AreRejected()
    {
        Assert.False(PathContainment.TryResolve(Root, Path.Combine(Root, "art.mul"), out _));
        Assert.False(PathContainment.TryResolve(Root, "/etc/passwd", out _));
    }

    [Fact]
    public void WindowsOnlyForms_AreRejected()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Drive letters and UNC paths only exist on Windows");

        var drive = Path.GetPathRoot(Root)![..1];
        Assert.False(PathContainment.TryResolve(Root, $"{drive}:art.mul", out _)); // Drive-relative, same drive
        Assert.False(PathContainment.TryResolve(Root, "Z:art.mul", out _));
        Assert.False(PathContainment.TryResolve(Root, @"C:\Windows\win.ini", out _));
        Assert.False(PathContainment.TryResolve(Root, @"\Windows\win.ini", out _));
        Assert.False(PathContainment.TryResolve(Root, @"\\server\share\x", out _));
    }

    [Fact]
    public void RootWithTrailingSeparator_BehavesTheSame()
    {
        Assert.True(PathContainment.TryResolve(Root + Path.DirectorySeparatorChar, "art.mul", out _));
        Assert.False(PathContainment.TryResolve(Root + Path.DirectorySeparatorChar, "../Game2/art.mul", out _));
    }
}
