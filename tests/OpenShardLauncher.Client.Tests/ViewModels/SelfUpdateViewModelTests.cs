using System.IO.Compression;
using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Client.ViewModels;
using OpenShardLauncher.Client.ViewModels.Dialogs;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Http;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Client.Tests.ViewModels;

// The launcher-update banner, the self-update result after a restart, the no-keys error and the unsigned-feed notice,
// as the main window shows them. The running launcher is version 1.0.0.
public sealed class SelfUpdateViewModelTests : IDisposable
{
    private LauncherTestHost _host = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void A_self_update_that_worked_is_reported_on_the_next_start()
    {
        UpdateResultMarker.WriteExpected(_host.DataFolder.UpdateResultFile, "1.0.0", "0.9.0");

        var viewModel = _host.Get<MainWindowViewModel>();

        Assert.Equal(UiText.Get(new LocalizedText(StringKeys.LauncherUpdatedNotice, "1.0.0")), viewModel.Status.Notice);
        Assert.False(File.Exists(_host.DataFolder.UpdateResultFile));
    }

    [Fact]
    public void A_self_update_that_failed_is_reported_with_the_reason()
    {
        UpdateResultMarker.WriteExpected(_host.DataFolder.UpdateResultFile, "1.1.0", "1.0.0");
        UpdateResultMarker.RecordError(_host.DataFolder.UpdateResultFile, "The old launcher didn't exit within 10 seconds.");

        var viewModel = _host.Get<MainWindowViewModel>();

        Assert.Equal(
            UiText.Get(new LocalizedText(StringKeys.LauncherUpdateFailedNotice, "1.1.0", "The old launcher didn't exit within 10 seconds.")),
            viewModel.Status.Notice);
    }

    [Fact]
    public async Task Without_keys_or_unsigned_mode_no_check_runs_and_the_error_says_how_to_fix_it()
    {
        Recreate(new LauncherOptions());
        var viewModel = _host.Get<MainWindowViewModel>();

        await viewModel.StartCommand.ExecuteAsync(null);
        await _host.Get<LauncherSelfUpdateService>().RefreshAsync(TestToken);

        Assert.Empty(_host.Server.Requests);
        Assert.Equal(UiText.Get(StringKeys.NoTrustedKeysError), viewModel.Status.Error);
        Assert.False(viewModel.VerifyCommand.CanExecute(null));
        Assert.False(viewModel.LauncherUpdate.IsVisible);
    }

    [Fact]
    public async Task The_banner_offers_a_newer_launcher_and_labels_an_unsigned_one()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        var changed = new List<string?>();
        viewModel.LauncherUpdate.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await OfferAsync("1.1.0", FeedTrust.Unsigned);

        Assert.Contains(nameof(LauncherUpdateBannerViewModel.CanUpdate), changed); // The Update button binds to it
        Assert.True(viewModel.LauncherUpdate.IsVisible);
        Assert.Equal(UiText.Get(new LocalizedText(StringKeys.LauncherUpdateAvailableUnsigned, "1.1.0")), viewModel.LauncherUpdate.Text);
        Assert.True(viewModel.UpdateLauncherCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_failed_update_shows_the_error_and_both_banner_buttons_work_again()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        await OfferAsync("1.1.0");
        _host.Server.Packages.Clear(); // The download fails
        bool? enabledAtLastChange = null; // The button only re-reads CanExecute when this event fires
        viewModel.DismissLauncherUpdateCommand.CanExecuteChanged += (_, _) =>
            enabledAtLastChange = viewModel.DismissLauncherUpdateCommand.CanExecute(null);

        await viewModel.UpdateLauncherCommand.ExecuteAsync(null);

        Assert.Equal(UiText.Get(StringKeys.SelfUpdateDownloadFailedError), viewModel.Status.Error);
        Assert.Equal(0, _host.Lifetime.Shutdowns);
        Assert.True(enabledAtLastChange);
        Assert.True(viewModel.UpdateLauncherCommand.CanExecute(null));
    }

    [Fact]
    public async Task Not_now_hides_the_banner()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        await OfferAsync("1.1.0");

        viewModel.DismissLauncherUpdateCommand.Execute(null);
        await _host.Get<LauncherSelfUpdateService>().RefreshAsync(TestToken);

        Assert.False(viewModel.LauncherUpdate.IsVisible);
    }

    [Fact]
    public async Task In_a_folder_the_launcher_cant_write_to_the_banner_explains_instead_of_offering_update()
    {
        _host.LauncherFolderWritable = false;
        var viewModel = _host.Get<MainWindowViewModel>();

        await OfferAsync("1.1.0");

        Assert.True(viewModel.LauncherUpdate.IsVisible);
        Assert.False(viewModel.LauncherUpdate.CanUpdate);
        Assert.NotNull(viewModel.LauncherUpdate.Warning);
        Assert.False(viewModel.UpdateLauncherCommand.CanExecute(null));
    }

    [Fact]
    public async Task Updating_while_a_check_runs_asks_first_then_cancels_it_and_hands_off()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        await OfferAsync("1.1.0");
        _host.Server.Hold = true;
        var launchCheck = viewModel.StartCommand.ExecuteAsync(null);
        await _host.Server.WaitForRequestAsync();

        var update = viewModel.UpdateLauncherCommand.ExecuteAsync(null);
        var confirm = Assert.IsType<ConfirmViewModel>(_host.Dialogs.Current);
        Assert.False(launchCheck.IsCompleted); // Nothing is cancelled until the player agrees
        confirm.ConfirmCommand.Execute(null);
        await update.WaitAsync(TestToken);
        await launchCheck.WaitAsync(TestToken);

        Assert.Equal(1, _host.Server.CancelledRequests);
        Assert.Equal("1.1.0", _host.SelfUpdater.HandedOffVersion);
        Assert.Equal(1, _host.Lifetime.Shutdowns);
    }

    [Fact]
    public async Task Saying_no_to_cancelling_the_running_check_changes_nothing()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        await OfferAsync("1.1.0");
        _host.Server.Hold = true;
        var launchCheck = viewModel.StartCommand.ExecuteAsync(null);
        await _host.Server.WaitForRequestAsync();

        var update = viewModel.UpdateLauncherCommand.ExecuteAsync(null);
        Assert.IsType<ConfirmViewModel>(_host.Dialogs.Current).CancelCommand.Execute(null);
        await update.WaitAsync(TestToken);

        Assert.Equal(LauncherState.Working, viewModel.State);
        Assert.Null(_host.SelfUpdater.HandedOffVersion);
        viewModel.CancelCommand.Execute(null);
        await launchCheck.WaitAsync(TestToken);
    }

    [Fact]
    public async Task The_unsigned_feed_notice_shows_in_unsigned_mode_and_changes_once_an_unsigned_feed_is_accepted()
    {
        var notice = _host.Get<MainWindowViewModel>().SecurityNotice;
        Assert.True(notice.IsVisible);
        Assert.Equal(UiText.Get(StringKeys.UnsignedFeedAllowedNotice), notice.Text);

        var verifier = _host.Get<FeedVerifier>();
        var list = new FileList(1, [], []).ToJsonBytes();
        await verifier.VerifyFileListAsync(_host.Get<ServerEndpoint>().Default, _ => Task.FromResult(new FeedDocument(list, null)), TestToken);

        Assert.Equal(UiText.Get(StringKeys.UnsignedFeedInUseNotice), notice.Text);
        Assert.Same(notice, _host.Get<SettingsViewModel>().SecurityNotice); // The same line in Settings
    }

    [Fact]
    public void Without_unsigned_mode_there_is_no_notice()
    {
        using var key = Shared.Signing.P256SigningKey.Generate();
        Recreate(new LauncherOptions { TrustedPublicKeys = [key.PublicKey] });

        Assert.False(_host.Get<MainWindowViewModel>().SecurityNotice.IsVisible);
    }

    public void Dispose() => _host.Dispose();

    private void Recreate(LauncherOptions options)
    {
        _host.Dispose();
        _host = new LauncherTestHost(options);
    }

    // The server offers launcher {version} for this platform, as a zip with this launcher's exe.
    private async Task OfferAsync(string version, FeedTrust trust = FeedTrust.Signed)
    {
        var rid = _host.Get<PlatformInfo>().Rid!;
        var zip = Zip((LauncherTestHost.ExeName, "new exe"));
        var file = PackageFileName.Format(PackageRole.Launcher, version, rid);
        _host.Server.Packages[file] = zip;
        var package = new PackageEntry(PackageRole.Launcher, version, rid, file, Sha256Hex.Of(zip), zip.Length);
        _host.Server.Manifest = FeedResult<PackageManifest>.Success(new PackageManifest(DateTimeOffset.UtcNow, [package]), trust);
        await _host.Get<LauncherSelfUpdateService>().RefreshAsync(TestToken);
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
