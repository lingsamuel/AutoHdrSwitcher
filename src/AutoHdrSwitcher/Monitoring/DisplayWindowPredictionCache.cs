using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoHdrSwitcher.Monitoring;

internal sealed class DisplayWindowPredictionCache
{
    private const string CacheFileName = "cache.json";
    private const string LegacyCacheFileName = "display-window-cache.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly TimeSpan PersistThrottle = TimeSpan.FromSeconds(3);

    private readonly object _sync = new();
    private readonly string _cachePath;
    private readonly Dictionary<string, CacheEntry> _entries;
    private readonly Dictionary<string, JsonElement> _extensionData;
    private readonly int _cacheVersion;
    private DateTimeOffset _lastPersistAt = DateTimeOffset.MinValue;
    private bool _dirty;

    public DisplayWindowPredictionCache()
    {
        _cachePath = ResolveDefaultCachePath();
        var loaded = LoadEntries(_cachePath, ResolveLegacyCachePath());
        _entries = loaded.Entries;
        _extensionData = loaded.ExtensionData;
        _cacheVersion = loaded.Version;
        _dirty = loaded.LoadedFromLegacyPath;
        if (_dirty)
        {
            PersistIfDue(force: true);
        }
    }

    public void RecordDisplay(IEnumerable<string> keys, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var normalizedKeys = NormalizeKeys(keys);
        if (normalizedKeys.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            var mappingChanged = false;
            foreach (var key in normalizedKeys)
            {
                if (_entries.TryGetValue(key, out var existing) &&
                    string.Equals(existing.Display, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _entries[key] = new CacheEntry
                {
                    Display = displayName
                };
                mappingChanged = true;
            }

            if (mappingChanged)
            {
                _dirty = true;
            }

            PersistIfDue(force: false);
        }
    }

    public IReadOnlyList<string> GetPredictedDisplays(IEnumerable<string> keys)
    {
        var normalizedKeys = NormalizeKeys(keys);
        if (normalizedKeys.Count == 0)
        {
            return Array.Empty<string>();
        }

        lock (_sync)
        {
            var matchedDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in normalizedKeys)
            {
                if (!_entries.TryGetValue(key, out var entry) ||
                    string.IsNullOrWhiteSpace(entry.Display))
                {
                    continue;
                }

                _ = matchedDisplays.Add(entry.Display);
            }

            if (matchedDisplays.Count == 0)
            {
                return Array.Empty<string>();
            }

            return matchedDisplays
                .OrderBy(static display => display, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public void Flush()
    {
        lock (_sync)
        {
            PersistIfDue(force: true);
        }
    }

    private static string ResolveDefaultCachePath()
    {
        return Path.Combine(AppContext.BaseDirectory, CacheFileName);
    }

    private static string ResolveLegacyCachePath()
    {
        return Path.Combine(AppContext.BaseDirectory, LegacyCacheFileName);
    }

    private static LoadResult LoadEntries(string path, string legacyPath)
    {
        if (TryLoadEntriesFromPath(path, out var directResult))
        {
            return directResult;
        }

        if (TryLoadEntriesFromPath(legacyPath, out var legacyResult))
        {
            return new LoadResult
            {
                Entries = legacyResult.Entries,
                ExtensionData = legacyResult.ExtensionData,
                Version = legacyResult.Version,
                LoadedFromLegacyPath = true
            };
        }

        return new LoadResult();
    }

    private static bool TryLoadEntriesFromPath(string path, out LoadResult result)
    {
        result = new LoadResult();
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<CacheFilePayload>(json, JsonOptions);
            if (payload is null)
            {
                return true;
            }

            var nestedEntries = payload.DisplayWindowPrediction?.Entries;
            var entries = nestedEntries is { Count: > 0 }
                ? nestedEntries
                : payload.Entries;
            result = new LoadResult
            {
                Entries = entries is null
                    ? new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, CacheEntry>(entries, StringComparer.OrdinalIgnoreCase),
                ExtensionData = payload.ExtensionData is null
                    ? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, JsonElement>(payload.ExtensionData, StringComparer.OrdinalIgnoreCase),
                Version = payload.Version > 0 ? payload.Version : 1
            };
            return true;
        }
        catch
        {
            return true;
        }
    }

    private static List<string> NormalizeKeys(IEnumerable<string> keys)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            _ = unique.Add(key.Trim());
        }

        return unique.ToList();
    }

    private void PersistIfDue(bool force)
    {
        if (!_dirty)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && (now - _lastPersistAt) < PersistThrottle)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new CacheFilePayload
            {
                Version = _cacheVersion,
                DisplayWindowPrediction = new DisplayWindowPredictionSection
                {
                    UpdatedAtUtc = now,
                    Entries = new Dictionary<string, CacheEntry>(_entries, StringComparer.OrdinalIgnoreCase)
                },
                ExtensionData = _extensionData.Count == 0
                    ? null
                    : new Dictionary<string, JsonElement>(_extensionData, StringComparer.OrdinalIgnoreCase)
            };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(_cachePath, json);
            _dirty = false;
            _lastPersistAt = now;
        }
        catch
        {
            // Cache persistence should never break monitor flow.
        }
    }

    private sealed class CacheFilePayload
    {
        public int Version { get; init; } = 1;

        public DisplayWindowPredictionSection DisplayWindowPrediction { get; init; } = new();

        // Backward compatibility for older flat cache format.
        public Dictionary<string, CacheEntry>? Entries { get; init; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    }

    private sealed class DisplayWindowPredictionSection
    {
        public DateTimeOffset UpdatedAtUtc { get; init; }

        public Dictionary<string, CacheEntry> Entries { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CacheEntry
    {
        public string Display { get; init; } = string.Empty;
    }

    private sealed class LoadResult
    {
        public Dictionary<string, CacheEntry> Entries { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, JsonElement> ExtensionData { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public int Version { get; init; } = 1;

        public bool LoadedFromLegacyPath { get; init; }
    }
}
