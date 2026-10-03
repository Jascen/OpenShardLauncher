using Avalonia.Markup.Xaml;
using OpenShardLauncher.Client.Presentation;

namespace OpenShardLauncher.Client.Views;

// {views:Text RetryButton}: a fixed string from Strings.resx, for labels and tooltips that never change.
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(Key);
}
