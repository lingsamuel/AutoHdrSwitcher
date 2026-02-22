using System;
using System.IO;
using System.Text.Json;

namespace AutoHdrSwitcher.Config;

public static class WatchConfigurationLoader
{
    public readonly record struct StartupOptions(
        bool AutoRequestAdminForTrace,
        bool EnableLogging);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static WatchConfiguration LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Configuration path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file was not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<WatchConfiguration>(json, JsonOptions);
        return config ?? new WatchConfiguration();
    }

    public static WatchConfiguration LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            return LoadFromFile(path);
        }

        var config = new WatchConfiguration();
        SaveToFile(path, config);
        return config;
    }

    public static void SaveToFile(string path, WatchConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Configuration path is required.", nameof(path));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static StartupOptions ReadStartupOptions(string path)
    {
        var options = new StartupOptions(
            AutoRequestAdminForTrace: false,
            EnableLogging: true);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return options;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("autoRequestAdminForTrace", out var autoAdminProperty) &&
                (autoAdminProperty.ValueKind == JsonValueKind.True || autoAdminProperty.ValueKind == JsonValueKind.False))
            {
                options = options with { AutoRequestAdminForTrace = autoAdminProperty.GetBoolean() };
            }

            if (document.RootElement.TryGetProperty("enableLogging", out var loggingProperty) &&
                (loggingProperty.ValueKind == JsonValueKind.True || loggingProperty.ValueKind == JsonValueKind.False))
            {
                options = options with { EnableLogging = loggingProperty.GetBoolean() };
            }

            return options;
        }
        catch
        {
            return options;
        }
    }
}
