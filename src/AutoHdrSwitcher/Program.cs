using AutoHdrSwitcher.UI;
using AutoHdrSwitcher.Logging;

namespace AutoHdrSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            AppLogger.Info($"Startup begin. Args: {string.Join(' ', args)}");
            ApplicationConfiguration.Initialize();
            var configPath = ResolveConfigPath(args);
            AppLogger.Info($"Launching MainForm with config path: {configPath}");
            Application.Run(new MainForm(configPath));
            AppLogger.Info("Application exited normally.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unhandled startup exception.", ex);
            MessageBox.Show(
                $"AutoHdrSwitcher failed to start.\n\n{ex}",
                "AutoHdrSwitcher Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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
