using System.Reflection;
using OpenShardLauncher.Client.Presentation;

namespace OpenShardLauncher.Client.Tests.Presentation;

public sealed class UiTextTests
{
    [Fact]
    public void Every_string_key_has_a_text()
    {
        var keys = typeof(StringKeys).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetValue(null)!);

        Assert.Equal([], keys.Where(key => !UiText.Exists(key)));
    }
}
