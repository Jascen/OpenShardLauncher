using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OpenShardLauncher.Client.Views;

// The replaceable images in Assets/. A missing image is null, so that part is left blank instead of stopping the
// launcher.
public static class LauncherArt
{
    public static Bitmap? Background { get; } = Load("background.png");

    public static Bitmap? PlayButton { get; } = Load("play-button.png");

    public static Bitmap? ProgressFrame { get; } = Load("progress-frame.png");

    public static Bitmap? ProgressTotal { get; } = Load("progress-total.png");

    public static Bitmap? ProgressFile { get; } = Load("progress-file.png");

    private static Bitmap? Load(string fileName)
    {
        var uri = new Uri($"avares://{typeof(LauncherArt).Assembly.GetName().Name}/Assets/{fileName}");
        return AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
    }
}
