using CommunityToolkit.Mvvm.ComponentModel;
using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Client.ViewModels;

// The two bars: everything (top) and the file currently downloading (bottom), each with its text. Fed by the progress
// reports of the running check or download, which arrive on the UI thread.
public sealed partial class ProgressViewModel : ObservableObject
{
    [ObservableProperty]
    public partial double Overall { get; private set; }

    [ObservableProperty]
    public partial string OverallText { get; private set; } = "";

    [ObservableProperty]
    public partial double File { get; private set; }

    [ObservableProperty]
    public partial string FileText { get; private set; } = "";

    public void Report(UpdateProgress progress)
    {
        Overall = progress.Percent;
        OverallText = UiText.Get(ProgressTextFormatter.Format(progress));
    }

    public void Report(FileProgress file)
    {
        File = file.Percent;
        FileText = UiText.Get(ProgressTextFormatter.Format(file));
    }

    // Empty bars with a message, at the start of a run or when there's nothing to show.
    public void Reset(LocalizedText text)
    {
        Overall = 0;
        File = 0;
        FileText = "";
        OverallText = UiText.Get(text);
    }

    // Full bars: everything was checked and downloaded.
    public void Complete(LocalizedText text)
    {
        Overall = 100;
        File = 100;
        FileText = "";
        OverallText = UiText.Get(text);
    }

    // Keeps the bars where the run left them.
    public void ShowText(LocalizedText text) => OverallText = UiText.Get(text);

    // What the bars show now, to put back after something else used them (a launcher update that didn't happen).
    public Snapshot Save() => new(Overall, OverallText, File, FileText);

    public void Restore(Snapshot snapshot) => (Overall, OverallText, File, FileText) = snapshot;

    public sealed record Snapshot(double Overall, string OverallText, double File, string FileText);
}
