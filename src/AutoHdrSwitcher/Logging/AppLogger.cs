using System.Text;

namespace AutoHdrSwitcher.Logging;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string LogDirectoryPath = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LogFilePath = Path.Combine(LogDirectoryPath, "autohdrswitcher.log");
    private static bool _enabled = true;
    private static bool _initialized;

    public static string CurrentLogPath => LogFilePath;

    public static void Initialize(bool enabled, bool clearOnStartup)
    {
        lock (Sync)
        {
            _enabled = enabled;
            _initialized = true;
            if (!clearOnStartup)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(LogDirectoryPath);
                using var _ = new FileStream(
                    LogFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite);
            }
            catch
            {
                // Logging initialization must never crash the application.
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        lock (Sync)
        {
            _enabled = enabled;
            _initialized = true;
        }
    }

    public static void Info(string message)
    {
        Write("INFO", message, exception: null);
    }

    public static void Warn(string message)
    {
        Write("WARN", message, exception: null);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            EnsureInitialized();
            if (!_enabled)
            {
                return;
            }

            Directory.CreateDirectory(LogDirectoryPath);

            var builder = new StringBuilder(256);
            builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
            builder.Append(" [");
            builder.Append(level);
            builder.Append("] [tid:");
            builder.Append(Environment.CurrentManagedThreadId);
            builder.Append("] ");
            builder.AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(FormatExceptionChain(exception));
            }

            lock (Sync)
            {
                using var stream = new FileStream(
                    LogFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                stream.Seek(0, SeekOrigin.End);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(builder.ToString());
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }

    private static string FormatExceptionChain(Exception exception)
    {
        var builder = new StringBuilder(512);
        var current = exception;
        var depth = 0;
        while (current is not null)
        {
            builder.Append('[');
            builder.Append(depth);
            builder.Append("] ");
            builder.Append(current.GetType().FullName);
            builder.Append(": ");
            builder.Append(current.Message);
            builder.Append(" (HRESULT=0x");
            builder.Append((current.HResult & 0xFFFFFFFF).ToString("X8"));
            builder.Append(')');
            builder.AppendLine();

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            _enabled = true;
            _initialized = true;
        }
    }
}
