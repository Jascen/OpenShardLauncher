using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Client.ViewModels;

// The links along the top. A link whose target is "verify" runs the main window's Verify command instead of opening a
// page, and is disabled while Verify can't run.
public sealed partial class NavBarViewModel : ObservableObject
{
    private readonly IUrlLauncher _urls;
    private readonly IAsyncRelayCommand _verify;

    public NavBarViewModel(IReadOnlyList<NavLink> links, IUrlLauncher urls, IAsyncRelayCommand verify)
    {
        Links = links;
        _urls = urls;
        _verify = verify;
        _verify.CanExecuteChanged += (_, _) => OpenLinkCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<NavLink> Links { get; }

    // Concurrent, so a page opens while a verify started from the bar is still running.
    [RelayCommand(CanExecute = nameof(CanOpenLink), AllowConcurrentExecutions = true)]
    private async Task OpenLinkAsync(NavLink? link)
    {
        if (link is null)
        {
            return;
        }

        if (link.IsVerify)
        {
            if (_verify.CanExecute(null))
            {
                await _verify.ExecuteAsync(null);
            }
        }
        else if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            await _urls.OpenAsync(uri);
        }
    }

    private bool CanOpenLink(NavLink? link) => link is not null && (!link.IsVerify || _verify.CanExecute(null));
}
