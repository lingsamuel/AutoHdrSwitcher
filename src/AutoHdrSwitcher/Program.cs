using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using AutoHdrSwitcher.Config;
using AutoHdrSwitcher.UI;
using AutoHdrSwitcher.Logging;

namespace AutoHdrSwitcher;

internal static class Program
{
    private const string ElevatedRelaunchArg = "--elevated-relaunch";

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            var configPath = ResolveConfigPath(args);
            var startupOptions = WatchConfigurationLoader.ReadStartupOptions(configPath);
            AppLogger.Initialize(
                enabled: startupOptions.EnableLogging,
                clearOnStartup: true);

            AppLogger.Info($"Startup begin. Args: {string.Join(' ', args)}");
            if (ShouldAutoRequestAdmin(args, startupOptions.AutoRequestAdminForTrace) &&
                TryRestartAsAdministrator(args))
            {
                AppLogger.Info("Restarting as administrator due to autoRequestAdminForTrace.");
                return;
            }

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

    private static bool ShouldAutoRequestAdmin(string[] args, bool autoRequestAdminForTrace)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (HasArgument(args, ElevatedRelaunchArg))
        {
            return false;
        }

        if (IsRunningAsAdministrator())
        {
            return false;
        }

        if (!autoRequestAdminForTrace)
        {
            return false;
        }

        AppLogger.Warn("autoRequestAdminForTrace is enabled and current process is not elevated. Requesting elevation.");
        return true;
    }

    private static bool TryRestartAsAdministrator(string[] args)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            AppLogger.Warn("Cannot request elevation because process path is unavailable.");
            return false;
        }

        var restartArgs = new string[args.Length + 1];
        Array.Copy(args, restartArgs, args.Length);
        restartArgs[^1] = ElevatedRelaunchArg;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = BuildCommandLineArguments(restartArgs),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppLogger.Warn("User canceled UAC elevation prompt.");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to relaunch process as administrator.", ex);
            return false;
        }
    }

    private static bool HasArgument(string[] args, string expected)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to determine admin role: {ex.Message}");
            return false;
        }
    }

    private static string BuildCommandLineArguments(string[] args)
    {
        if (args.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(QuoteArgumentForWindows(args[i]));
        }

        return builder.ToString();
    }

    private static string QuoteArgumentForWindows(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var requiresQuotes = value.Any(static c => char.IsWhiteSpace(c) || c == '"');
        if (!requiresQuotes)
        {
            return value;
        }

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashCount = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashCount++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(c);
        }

        if (backslashCount > 0)
        {
            builder.Append('\\', backslashCount * 2);
        }

        builder.Append('"');
        return builder.ToString();
    }
}
