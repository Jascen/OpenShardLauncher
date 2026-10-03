namespace OpenShardLauncher.Client.Presentation;

// A string key (see StringKeys) and its format arguments. Resolved through Strings.resx by the view layer, so the
// presentation logic stays free of player-facing text.
public sealed class LocalizedText(string key, params object[] args)
{
    public string Key { get; } = key;

    public IReadOnlyList<object> Args { get; } = args;

    public override string ToString() => Args.Count == 0 ? Key : $"{Key}({string.Join(", ", Args)})";
}
