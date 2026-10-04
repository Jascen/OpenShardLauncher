using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace OpenShardLauncher.Client.Services;

// Lets the player pick a folder on disk.
public interface IFolderPicker
{
    // The chosen folder's local path, or null when the player cancels.
    Task<string?> PickFolderAsync(string title, string? startFolder);
}

// The platform folder picker (IStorageProvider), opened over the main window. It starts in startFolder when that
// exists, otherwise in Documents.
public sealed class AvaloniaFolderPicker : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
        {
            return null;
        }

        var storage = window.StorageProvider;
        var start = startFolder is not null && Directory.Exists(startFolder) ? await storage.TryGetFolderFromPathAsync(startFolder) : null;
        start ??= await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
