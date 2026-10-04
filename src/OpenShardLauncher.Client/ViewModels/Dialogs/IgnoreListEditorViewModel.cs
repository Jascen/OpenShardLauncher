using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Storage;

namespace OpenShardLauncher.Client.ViewModels.Dialogs;

// The player's ignore list (.openshardignore) in the install folder picked in Settings. It lives in that folder, so
// picking another folder shows that folder's list. An untouched list is never written back.
public sealed partial class IgnoreListEditorViewModel(ILogger logger) : ObservableObject
{
    private string _folder = "";
    private string _loaded = "";

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    public bool IsChanged => Text != _loaded;

    public void Load(string folder)
    {
        _folder = folder;
        var path = Path.Combine(folder, InstallFolder.IgnoreFileName);
        try
        {
            _loaded = File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not read the ignore list {Path}", path);
            _loaded = "";
        }

        Text = _loaded;
    }

    public void SaveIfChanged()
    {
        if (!IsChanged)
        {
            return;
        }

        var path = Path.Combine(_folder, InstallFolder.IgnoreFileName);
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(path, Text);
            _loaded = Text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Could not save the ignore list {Path}", path);
        }
    }
}
