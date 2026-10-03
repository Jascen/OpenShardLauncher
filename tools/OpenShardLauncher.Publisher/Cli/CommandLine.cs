namespace OpenShardLauncher.Publisher.Cli;

public sealed class UsageException(string message) : Exception(message);

// "--name value" options (repeatable where allowed) and "--flag" switches, checked against what the command accepts
public sealed class CommandLine
{
    private readonly Dictionary<string, List<string>> _values;
    private readonly HashSet<string> _flags;

    private CommandLine(Dictionary<string, List<string>> values, HashSet<string> flags)
    {
        _values = values;
        _flags = flags;
    }

    public static CommandLine Parse(IReadOnlyList<string> args, IReadOnlyCollection<string> options, IReadOnlyCollection<string> flags)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var setFlags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"Unexpected argument '{arg}'.");
            }

            var name = arg[2..];
            if (flags.Contains(name))
            {
                setFlags.Add(name);
            }
            else if (options.Contains(name))
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new UsageException($"--{name} needs a value.");
                }

                if (!values.TryGetValue(name, out var list))
                {
                    values[name] = list = [];
                }

                list.Add(args[++i]);
            }
            else
            {
                throw new UsageException($"Unknown option '{arg}'.");
            }
        }

        return new CommandLine(values, setFlags);
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out var list) ? list : [];

    public string? Optional(string name) => All(name) switch
    {
        [] => null,
        [var single] => single,
        _ => throw new UsageException($"--{name} can only be given once."),
    };

    public string Required(string name) => Optional(name) ?? throw new UsageException($"Missing --{name}.");
}
