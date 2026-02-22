using System.Text;

namespace AutoHdrSwitcher.Logging;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string LogDirectoryPath = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LogFilePath = Path.Combine(LogDirectoryPath, "autohdrswitcher.log");

    public static string CurrentLogPath => LogFilePath;

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
                File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
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
}
