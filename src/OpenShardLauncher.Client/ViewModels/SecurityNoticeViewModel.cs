using CommunityToolkit.Mvvm.ComponentModel;
using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Http;

namespace OpenShardLauncher.Client.ViewModels;

// The amber strip under the nav bar (and the read-only line in Settings) shown whenever the launcher is built with
// AllowUnsignedFeed. It can't be dismissed. Once a file list or manifest has actually been accepted without a
// signature, the text says the current feed *is* unsigned; a new server starts over.
public sealed partial class SecurityNoticeViewModel : ObservableObject, IDisposable
{
    private readonly FeedVerifier _verifier;
    private readonly ServerEndpoint _endpoint;
    private readonly CapturedContext _ui = new();

    public SecurityNoticeViewModel(LauncherOptions options, FeedVerifier verifier, ServerEndpoint endpoint)
    {
        _verifier = verifier;
        _endpoint = endpoint;
        IsVisible = options.AllowUnsignedFeed;
        _verifier.UnsignedAccepted += OnUnsignedAccepted;
        _endpoint.Changed += OnServerChanged;
    }

    public bool IsVisible { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial bool UnsignedInUse { get; private set; }

    public string Text => IsVisible
        ? UiText.Get(UnsignedInUse ? StringKeys.UnsignedFeedInUseNotice : StringKeys.UnsignedFeedAllowedNotice)
        : "";

    public void Dispose()
    {
        _verifier.UnsignedAccepted -= OnUnsignedAccepted;
        _endpoint.Changed -= OnServerChanged;
    }

    private void OnUnsignedAccepted(object? sender, EventArgs e) => _ui.Post(() => UnsignedInUse = true);

    private void OnServerChanged(object? sender, EventArgs e) => _ui.Post(() => UnsignedInUse = false);
}
