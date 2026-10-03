using System.Text;

namespace OpenShardLauncher.Publisher.Cli;

// Key passwords come from PUBLISHER_KEY_PASSWORD, or are asked for without echo
internal static class Passwords
{
    public const string EnvironmentVariable = "PUBLISHER_KEY_PASSWORD";

    public static string ForExistingKey(string keyFile) =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) ?? Read($"Password for {keyFile}: ");

    public static string ForNewKey()
    {
        var password = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (password is null)
        {
            password = Read("New key password: ");
            if (Read("Repeat password: ") != password)
            {
                throw new UsageException("The passwords didn't match.");
            }
        }

        if (password.Length < 8)
        {
            throw new UsageException("Use a key password of at least 8 characters.");
        }

        return password;
    }

    private static string Read(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new UsageException($"Set {EnvironmentVariable} when input is redirected.");
        }

        Console.Error.Write(prompt);
        var password = new StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return password.ToString();
    }
}
