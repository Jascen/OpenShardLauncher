const string Usage = """
    Usage:
      publisher publish --source <game files> --packages <zips> --out <feed> --key <key> [--key <key2>]
      publisher publish --source <game files> --packages <zips> --out <feed> --unsigned
      publisher keygen [--out <dir>]
      publisher verify --feed <feed> --pub <key>
    """;

if (args is [] or ["-h" or "--help" or "help"])
{
    Console.WriteLine(Usage);
    return 0;
}

Console.Error.WriteLine($"Unknown command: {args[0]}");
Console.Error.WriteLine(Usage);
return 1;
