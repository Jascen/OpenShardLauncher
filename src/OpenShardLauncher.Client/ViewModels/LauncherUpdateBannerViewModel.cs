using CommunityToolkit.Mvvm.ComponentModel;
using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Client.ViewModels;

// The "launcher version X is available" banner. It follows LauncherSelfUpdateService.Offer. When an offer appears the
// launcher folder is probed: if the launcher can't write there (Program Files, a read-only macOS App Translocation
// copy) the banner explains why it can't update itself instead of offering Update. No elevation prompt, ever.
// Update and "Not now" are MainWindowViewModel's commands, since updating has to stop a running game update first.
public sealed partial class LauncherUpdateBannerViewModel : ObservableObject, IDisposable
{
    private readonly LauncherSelfUpdateService _service;
    private readonly InstalledLauncher _launcher;
    private readonly LauncherDataFolder _dataFolder;
    private readonly Func<string, bool> _isWritable;
    private readonly CapturedContext _ui = new();

    // isWritable is FolderProbe.IsWritable; tests replace it.
    public LauncherUpdateBannerViewModel(
        LauncherSelfUpdateService service, InstalledLauncher launcher, LauncherDataFolder dataFolder, Func<string, bool>? isWritable = null)
    {
        _service = service;
        _launcher = launcher;
        _dataFolder = dataFolder;
        _isWritable = isWritable ?? FolderProbe.IsWritable;
        _service.OfferChanged += OnOfferChanged;
        Refresh();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(Text), nameof(CanUpdate))]
    public partial LauncherUpdateOffer? Offer { get; private set; }

    // Why the launcher can't update itself here; null when it can.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate), nameof(HasWarning))]
    public partial string? Warning { get; private set; }

    public bool IsVisible => Offer is not null;

    public bool CanUpdate => Offer is not null && Warning is null;

    public bool HasWarning => Warning is not null;

    public string Text => Offer is not { } offer ? ""
        : UiText.Get(new LocalizedText(
            offer.Trust == FeedTrust.Unsigned ? StringKeys.LauncherUpdateAvailableUnsigned : StringKeys.LauncherUpdateAvailable,
            offer.Package.Version));

    public void Dispose() => _service.OfferChanged -= OnOfferChanged;

    private void OnOfferChanged(object? sender, EventArgs e) => _ui.Post(Refresh);

    private void Refresh()
    {
        var offer = _service.Offer;
        if (offer is not null && offer != Offer)
        {
            Warning = CheckFolder();
        }

        Offer = offer;
    }

    // The marker the applier writes lives in the portable data folder, so that has to be in use too.
    private string? CheckFolder()
    {
        if (_dataFolder.IsPortable && _isWritable(_launcher.Folder))
        {
            return null;
        }

        return UiText.Get(OperatingSystem.IsMacOS() ? StringKeys.LauncherTranslocated : StringKeys.LauncherFolderNotWritable);
    }
}
