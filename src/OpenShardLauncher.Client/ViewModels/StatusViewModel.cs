using CommunityToolkit.Mvvm.ComponentModel;
using OpenShardLauncher.Client.Presentation;

namespace OpenShardLauncher.Client.ViewModels;

// The lines above the progress bars: a notice about the launcher itself, what the ignore list skipped, and the error
// line. Retry, Cancel and Show are the main window's commands.
public sealed partial class StatusViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIgnoredItems), nameof(IgnoredText))]
    public partial IReadOnlyList<string> IgnoredItems { get; set; } = [];

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasIgnoredItems => IgnoredItems.Count > 0;

    public string IgnoredText => HasIgnoredItems ? UiText.Get(new LocalizedText(StringKeys.IgnoredSkipped, IgnoredItems.Count)) : "";

    public void ShowError(LocalizedText? text) => Error = text is null ? null : UiText.Get(text);
}
