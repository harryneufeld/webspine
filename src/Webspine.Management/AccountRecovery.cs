using System.Text;

namespace Webspine.Management;

internal static class AccountRecovery
{
    public static async Task<int> RunAsync(AccountAdministration accounts, string username)
    {
        Console.WriteLine("Local account recovery. Enter a new password and repeat it; passwords are not command-line arguments.");
        var password = ReadPassword("New password: ");
        var confirmation = ReadPassword("Repeat password: ");
        if (password != confirmation) { Console.Error.WriteLine("Passwords do not match. Nothing changed."); return 1; }
        try
        {
            await accounts.RecoverAsync(username, password);
            Console.WriteLine("Account recovered. Previous sessions and app credentials are invalidated.");
            return 0;
        }
        catch (AccountChangeException error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(key.KeyChar) && value.Length < 1024) value.Append(key.KeyChar);
        }
    }
}
