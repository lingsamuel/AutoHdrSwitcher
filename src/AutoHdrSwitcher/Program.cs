using AutoHdrSwitcher.UI;

namespace AutoHdrSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var configPath = ResolveConfigPath(args);
        Application.Run(new MainForm(configPath));
    }

    private static string ResolveConfigPath(string[] args)
    {
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                break;
            }

            return Path.GetFullPath(args[i + 1]);
        }

        return defaultPath;
    }
}
