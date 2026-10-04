using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Client.ViewModels.Dialogs;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Client.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly LauncherTestHost _host = new(new LauncherOptions { UpdateUrl = "https://updates.example.com/" });

    [Theory]
    [InlineData("https://mirror.example.com", false, null)]
    [InlineData("https://mirror.example.com/feed/", false, null)]
    [InlineData("http://127.0.0.1:8080", false, null)]
    [InlineData("http://localhost/feed", false, null)]
    [InlineData("  ", false, null)] // Blank means the default
    [InlineData("http://mirror.example.com", false, StringKeys.ServerUrlInsecureError)]
    [InlineData("http://mirror.example.com", true, null)]
    [InlineData("mirror.example.com", true, StringKeys.ServerUrlInvalidError)]
    [InlineData("ftp://mirror.example.com", true, StringKeys.ServerUrlInvalidError)]
    [InlineData("https://user:secret@mirror.example.com", true, StringKeys.ServerUrlInvalidError)]
    [InlineData("https://mirror.example.com/?channel=beta", true, StringKeys.ServerUrlInvalidError)]
    [InlineData("https://mirror.example.com/#feed", true, StringKeys.ServerUrlInvalidError)]
    public void Server_address_must_be_a_plain_http_or_https_url_and_secure_unless_insecure_downloads_are_allowed(
        string url, bool allowInsecure, string? expectedError)
    {
        Assert.Equal(expectedError, SettingsViewModel.ValidateServerUrl(url, allowInsecure));
    }

    [Fact]
    public void Ticking_allow_insecure_downloads_accepts_a_plain_http_server()
    {
        var settings = _host.Get<SettingsViewModel>();

        settings.ServerUrl = "http://mirror.example.com/";
        Assert.True(settings.HasServerUrlError);
        Assert.False(settings.SaveCommand.CanExecute(null));

        settings.AllowInsecureDownloads = true;
        Assert.False(settings.HasServerUrlError);
        Assert.True(settings.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Reset_is_offered_only_for_another_server_and_saving_the_default_stores_no_override()
    {
        var settings = _host.Get<SettingsViewModel>();
        Assert.False(settings.ResetServerUrlCommand.CanExecute(null));

        settings.ServerUrl = "https://mirror.example.com/feed";
        Assert.True(settings.ResetServerUrlCommand.CanExecute(null));
        Assert.True(settings.ShowMirrorNote);

        // The same address as the default, written differently
        settings.ServerUrl = "https://updates.example.com";
        Assert.False(settings.ResetServerUrlCommand.CanExecute(null));
        settings.SaveCommand.Execute(null);

        Assert.Null(_host.Settings.Current.ServerUrlOverride);
        Assert.True(_host.Get<ServerEndpoint>().IsDefault);
    }

    [Fact]
    public void Save_applies_the_server_and_the_insecure_downloads_rule_without_a_restart()
    {
        var settings = _host.Get<SettingsViewModel>();
        settings.AllowInsecureDownloads = true;
        settings.ServerUrl = "http://mirror.example.com/feed";

        settings.SaveCommand.Execute(null);

        Assert.Equal("http://mirror.example.com/feed/", _host.Settings.Current.ServerUrlOverride);
        Assert.Equal(new Uri("http://mirror.example.com/feed/"), _host.Get<ServerEndpoint>().Current);
        Assert.True(_host.Get<Infrastructure.Http.TransportPolicy>().AllowInsecureDownloads);
        Assert.Equal(_host.Settings.Current, _host.Get<SettingsStore>().Load()); // And saved to settings.json
    }

    [Fact]
    public async Task Cancel_leaves_the_settings_and_the_ignore_list_unchanged()
    {
        var before = _host.Settings.Current;
        var settings = _host.Get<SettingsViewModel>();
        var closed = _host.Dialogs.ShowAsync(settings);
        var otherFolder = Directory.CreateDirectory(Path.Combine(_host.Root, "Other")).FullName;

        _host.FolderPicker.Next = otherFolder;
        await settings.ChangeFolderCommand.ExecuteAsync(null);
        settings.VerifyOnLaunch = !settings.VerifyOnLaunch;
        settings.WarnIfNotVerified = !settings.WarnIfNotVerified;
        settings.AllowInsecureDownloads = true;
        settings.ServerUrl = "http://mirror.example.com/";
        settings.IgnoreList.Text = "Music/";
        settings.CancelCommand.Execute(null);

        Assert.False(await closed.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Same(before, _host.Settings.Current);
        Assert.False(File.Exists(_host.Get<LauncherDataFolder>().SettingsFile));
        Assert.True(_host.Get<ServerEndpoint>().IsDefault);
        Assert.False(_host.Get<Infrastructure.Http.TransportPolicy>().AllowInsecureDownloads);
        Assert.False(File.Exists(Path.Combine(otherFolder, InstallFolder.IgnoreFileName)));
    }

    [Fact]
    public async Task A_folder_that_is_the_launcher_folder_or_contains_it_is_refused()
    {
        var settings = _host.Get<SettingsViewModel>();
        var original = settings.InstallPath;

        foreach (var folder in new[] { _host.LauncherFolder, _host.Root })
        {
            _host.FolderPicker.Next = folder;
            await settings.ChangeFolderCommand.ExecuteAsync(null);

            Assert.Equal(original, settings.InstallPath);
            Assert.Equal(UiText.Get(StringKeys.InstallFolderNotAllowedError), settings.FolderError);
            Assert.False(settings.SaveCommand.CanExecute(null));
        }

        // A subfolder of the launcher folder is fine, and clears the error
        _host.FolderPicker.Next = Path.Combine(_host.LauncherFolder, "Games", "Shard");
        await settings.ChangeFolderCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(_host.LauncherFolder, "Games", "Shard"), settings.InstallPath);
        Assert.False(settings.HasFolderError);
        Assert.True(settings.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_ignore_list_shown_and_saved_is_the_one_in_the_selected_folder()
    {
        var first = Directory.CreateDirectory(Path.Combine(_host.Root, "First")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_host.Root, "Second")).FullName;
        await File.WriteAllTextAsync(Path.Combine(first, InstallFolder.IgnoreFileName), "*.cfg\n", TestContext.Current.CancellationToken);
        var settings = _host.Get<SettingsViewModel>();

        _host.FolderPicker.Next = first;
        await settings.ChangeFolderCommand.ExecuteAsync(null);
        Assert.Equal("*.cfg\n", settings.IgnoreList.Text);

        _host.FolderPicker.Next = second;
        await settings.ChangeFolderCommand.ExecuteAsync(null);
        Assert.Equal("", settings.IgnoreList.Text);
        settings.IgnoreList.Text = "Music/";
        settings.SaveCommand.Execute(null);

        Assert.Equal("Music/", await File.ReadAllTextAsync(Path.Combine(second, InstallFolder.IgnoreFileName), TestContext.Current.CancellationToken));
        Assert.Equal("*.cfg\n", await File.ReadAllTextAsync(Path.Combine(first, InstallFolder.IgnoreFileName), TestContext.Current.CancellationToken));
        Assert.Equal(second, _host.Settings.Current.InstallPath);
    }

    [Fact]
    public async Task An_untouched_ignore_list_is_not_written_back()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_host.Root, "Game")).FullName;
        var ignoreFile = Path.Combine(folder, InstallFolder.IgnoreFileName);
        await File.WriteAllTextAsync(ignoreFile, "*.cfg\n", TestContext.Current.CancellationToken);
        var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(ignoreFile, written);
        var settings = _host.Get<SettingsViewModel>();

        _host.FolderPicker.Next = folder;
        await settings.ChangeFolderCommand.ExecuteAsync(null);
        settings.SaveCommand.Execute(null);

        Assert.Equal(written, File.GetLastWriteTimeUtc(ignoreFile));
    }

    public void Dispose() => _host.Dispose();
}
