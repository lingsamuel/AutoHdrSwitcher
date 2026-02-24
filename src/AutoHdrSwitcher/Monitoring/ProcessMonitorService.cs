using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AutoHdrSwitcher.Display;
using AutoHdrSwitcher.Logging;
using AutoHdrSwitcher.Matching;
using System.Windows.Forms;

namespace AutoHdrSwitcher.Monitoring;

public sealed class ProcessMonitorService
{
    private const string DefaultWindowsPathPrefix = @"C:\Windows\";
    private static readonly string NormalizedDefaultWindowsPathPrefix = NormalizePath(DefaultWindowsPathPrefix);

    private static readonly string[] DefaultIgnoredProcessNameSeeds =
    {
        // System and shell infrastructure
        "TextInputHost",
        "dwm",
        "ctfmon",
        "WmiPrvSE",
        "svchost",
        "RuntimeBroker",
        "backgroundTaskHost",
        "taskhostw",
        "DllHost",
        "conhost",
        "explorer",
        "sihost",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchIndexer",
        "ApplicationFrameHost",
        "audiodg",
        "fontdrvhost",
        "smartscreen",
        "SecurityHealthSystray",
        "SecurityHealthService",
        "WUDFHost",
        "AggregatorHost",
        "LockApp",
        "MoUsoCoreWorker",
        "spoolsv",

        // Command helpers / telemetry noise
        "ping",
        "fping",
        "wsl",
        "wslhost",
        "nvngx_update",
        "backgroundtask",
        "gamebarpresenc",

        // Browsers and browser helpers
        "msedge",
        "msedgewebview2",
        "chrome",
        "chrome_proxy",
        "chrome_crashpad_handler",
        "firefox",
        "plugin-container",
        "brave",
        "brave_crashpad_handler",
        "opera",
        "opera_crashreporter",
        "vivaldi",
        "vivaldi_crashpad_handler",
        "steamwebhelper"
    };

    private static readonly HashSet<string> DefaultIgnoredProcessNames = BuildDefaultIgnoredProcessNames();

    private readonly ProcessDisplayResolver _displayResolver = new();
    private readonly HdrController _hdrController = new();
    private readonly HashSet<int> _processesWithObservedWindow = new();

    public static string DefaultWindowsPathPrefixIgnoreKey { get; } =
        BuildPathPrefixIgnoreKey(DefaultWindowsPathPrefix);

    public static IReadOnlyList<string> DefaultIgnoreKeys { get; } = BuildDefaultIgnoreKeys();

    public static bool IsDefaultIgnoredProcessName(string processName)
    {
        var normalizedName = NormalizeProcessNameForIgnore(processName);
        return !string.IsNullOrWhiteSpace(normalizedName) && DefaultIgnoredProcessNames.Contains(normalizedName);
    }

    public ProcessMonitorSnapshot Evaluate(
        IReadOnlyList<ProcessWatchRule> rules,
        bool monitorAllFullscreenProcesses,
        IReadOnlyDictionary<string, bool> fullscreenIgnoreMap,
        bool switchAllDisplaysTogether,
        IReadOnlyDictionary<string, bool> displayAutoModes,
        IReadOnlyDictionary<string, string> processTargetDisplayOverrides)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(fullscreenIgnoreMap);
        ArgumentNullException.ThrowIfNull(displayAutoModes);
        ArgumentNullException.ThrowIfNull(processTargetDisplayOverrides);

        var evaluationStopwatch = Stopwatch.StartNew();
        var processArray = Process.GetProcesses();
        var resolvedWindows = _displayResolver.CaptureProcessWindows();
        var activeProcessIds = BuildActiveProcessIdSet(processArray);
        var matches = new List<ProcessMatchResult>();
        var fullscreenProcesses = new List<FullscreenProcessInfo>();
        var matchedPids = new HashSet<int>();
        var matchedDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processNames = new Dictionary<int, string>();
        var processPaths = new Dictionary<int, string>();
        var availableDisplays = BuildAvailableDisplaySet();
        var forceSwitchAllDisplaysByTarget = false;
        var requiresPathCandidates = RulesRequirePathCandidates(rules);
        var hasPathTargetOverrides = HasPathTargetOverrides(processTargetDisplayOverrides);
        var pathLookupAttempts = 0;
        var pathLookupSucceeded = 0;

        foreach (var process in processArray)
        {
            using (process)
            {
                try
                {
                    var processId = process.Id;
                    var safeName = SafeProcessName(process);
                    processNames[processId] = safeName;
                    string? executablePath = null;
                    if (requiresPathCandidates &&
                        TryGetExecutablePathWithStats(process, ref pathLookupAttempts, ref pathLookupSucceeded, out var discoveredPath))
                    {
                        executablePath = discoveredPath;
                        processPaths[processId] = discoveredPath;
                    }

                    foreach (var candidate in BuildCandidates(safeName, executablePath))
                    {
                        if (!TryFindFirstMatchingRule(candidate, rules, out var matchedRule, out var matchedRuleIndex))
                        {
                            continue;
                        }

                        if (!matchedPids.Add(processId))
                        {
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(executablePath) &&
                            hasPathTargetOverrides &&
                            TryGetExecutablePathWithStats(process, ref pathLookupAttempts, ref pathLookupSucceeded, out discoveredPath))
                        {
                            executablePath = discoveredPath;
                            processPaths[processId] = discoveredPath;
                        }

                        if (resolvedWindows.ContainsKey(processId))
                        {
                            _processesWithObservedWindow.Add(processId);
                        }

                        var targetResolution = ResolveDisplayForRule(
                            processId,
                            resolvedWindows,
                            ResolveEffectiveTargetDisplay(
                                safeName,
                                executablePath,
                                matchedRule.TargetDisplay,
                                processTargetDisplayOverrides,
                                out var processTargetKey,
                                out var hasProcessTargetOverride),
                            availableDisplays,
                            _processesWithObservedWindow.Contains(processId));
                        if (!string.IsNullOrWhiteSpace(targetResolution.DesiredDisplay))
                        {
                            matchedDisplays.Add(targetResolution.DesiredDisplay);
                        }
                        if (targetResolution.RequestAllDisplays)
                        {
                            forceSwitchAllDisplaysByTarget = true;
                        }

                        matches.Add(new ProcessMatchResult
                        {
                            RuleIndex = matchedRuleIndex,
                            ProcessTargetKey = processTargetKey,
                            HasProcessTargetOverride = hasProcessTargetOverride,
                            ProcessId = processId,
                            ProcessName = safeName,
                            MatchInput = candidate,
                            RulePattern = matchedRule.Pattern,
                            Mode = DescribeMode(matchedRule),
                            Display = targetResolution.DisplayLabel,
                            IsFullscreenLike = targetResolution.IsFullscreenLike,
                            EffectiveTargetDisplay = string.IsNullOrWhiteSpace(targetResolution.ConfiguredTargetDisplay)
                                ? null
                                : targetResolution.ConfiguredTargetDisplay
                        });
                        break;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process exited during inspection.
                }
                catch (Win32Exception)
                {
                    // Access denied for process details.
                }
            }
        }

        foreach (var window in resolvedWindows.Values.Where(static value => value.IsFullscreenLike))
        {
            if (monitorAllFullscreenProcesses)
            {
                _processesWithObservedWindow.Add(window.ProcessId);
            }

            var name = processNames.TryGetValue(window.ProcessId, out var safeName)
                ? safeName
                : $"PID-{window.ProcessId}";
            _ = processPaths.TryGetValue(window.ProcessId, out var executablePath);
            if (string.IsNullOrWhiteSpace(executablePath) &&
                TryGetExecutablePathByProcessIdWithStats(
                    window.ProcessId,
                    ref pathLookupAttempts,
                    ref pathLookupSucceeded,
                    out var discoveredPath))
            {
                executablePath = discoveredPath;
                processPaths[window.ProcessId] = discoveredPath;
            }
            ResolveIgnoreState(
                name,
                executablePath,
                fullscreenIgnoreMap,
                out var ignoreKey,
                out var isIgnored,
                out var isDefaultIgnoreApplied);
            var matchedByRule = matchedPids.Contains(window.ProcessId);
            fullscreenProcesses.Add(new FullscreenProcessInfo
            {
                ProcessId = window.ProcessId,
                ProcessName = name,
                ExecutablePath = executablePath ?? string.Empty,
                IgnoreKey = ignoreKey,
                IsIgnored = isIgnored,
                IsDefaultIgnoreApplied = isDefaultIgnoreApplied,
                Display = window.GdiDeviceName,
                MatchedByRule = matchedByRule
            });

            if (monitorAllFullscreenProcesses && !isIgnored && !matchedByRule)
            {
                matchedDisplays.Add(window.GdiDeviceName);
            }
        }

        var displayStatuses = EvaluateDisplayHdrStates(
            matchedDisplays,
            switchAllDisplaysTogether,
            displayAutoModes,
            forceSwitchAllDisplaysByTarget);
        CleanupObservedWindowState(activeProcessIds);
        evaluationStopwatch.Stop();
        AppLogger.Info(
            $"Process evaluation finished. durationMs={evaluationStopwatch.Elapsed.TotalMilliseconds:F3}; processCount={processArray.Length}; windowCount={resolvedWindows.Count}; matches={matches.Count}; fullscreen={fullscreenProcesses.Count}; pathLookupAttempts={pathLookupAttempts}; pathLookupSucceeded={pathLookupSucceeded}; requiresPathCandidates={requiresPathCandidates}; hasPathTargetOverrides={hasPathTargetOverrides}");
        matches.Sort(static (a, b) => a.ProcessName.CompareTo(b.ProcessName));
        fullscreenProcesses.Sort(static (a, b) =>
        {
            var byName = string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
            {
                return byName;
            }

            return a.ProcessId.CompareTo(b.ProcessId);
        });
        return new ProcessMonitorSnapshot
        {
            CollectedAt = DateTimeOffset.Now,
            ProcessCount = processArray.Length,
            Matches = matches,
            FullscreenProcesses = fullscreenProcesses,
            Displays = displayStatuses
        };
    }

    public IReadOnlyList<HdrDisplayStatus> GetLiveDisplayHdrStates(
        IReadOnlyDictionary<string, bool> displayAutoModes)
    {
        ArgumentNullException.ThrowIfNull(displayAutoModes);
        var statuses = new List<HdrDisplayStatus>();
        var primaryDisplayName = GetPrimaryDisplayName();
        var displays = _hdrController.GetDisplays();
        var coveredDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in displays)
        {
            var isAuto = ResolveDisplayAutoMode(display.GdiDeviceName, displayAutoModes);
            statuses.Add(new HdrDisplayStatus
            {
                Display = display.GdiDeviceName,
                FriendlyName = display.FriendlyName,
                IsPrimary = IsPrimaryDisplay(display.GdiDeviceName, primaryDisplayName),
                IsHdrSupported = display.IsHdrSupported,
                IsHdrEnabled = display.IsHdrEnabled,
                DesiredHdrEnabled = display.IsHdrEnabled,
                LastAction = display.IsHdrSupported
                    ? $"Live status refreshed (Auto={isAuto})"
                    : "HDR unsupported"
            });
            coveredDisplays.Add(display.GdiDeviceName);
        }

        foreach (var screen in Screen.AllScreens)
        {
            if (!coveredDisplays.Add(screen.DeviceName))
            {
                continue;
            }

            statuses.Add(new HdrDisplayStatus
            {
                Display = screen.DeviceName,
                FriendlyName = screen.Primary ? $"{screen.DeviceName} (Primary)" : screen.DeviceName,
                IsPrimary = IsPrimaryDisplay(screen.DeviceName, primaryDisplayName),
                IsHdrSupported = false,
                IsHdrEnabled = false,
                DesiredHdrEnabled = false,
                LastAction = "Display detected; HDR state unavailable"
            });
        }

        statuses.Sort(static (a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));
        return statuses;
    }

    public bool TrySetDisplayHdr(string gdiDisplayName, bool enable, out string message)
    {
        AppLogger.Info($"Manual HDR request received. display={gdiDisplayName}; desired={enable}");
        if (string.IsNullOrWhiteSpace(gdiDisplayName))
        {
            message = "Display name is required";
            AppLogger.Warn($"Manual HDR request rejected. reason={message}");
            return false;
        }

        var display = _hdrController.GetDisplays().FirstOrDefault(
            d => string.Equals(d.GdiDeviceName, gdiDisplayName, StringComparison.OrdinalIgnoreCase));
        if (display is null)
        {
            message = $"Display not found: {gdiDisplayName}";
            AppLogger.Warn($"Manual HDR request rejected. reason={message}");
            return false;
        }

        if (!display.IsHdrSupported)
        {
            message = "HDR unsupported";
            AppLogger.Warn($"Manual HDR request rejected. display={gdiDisplayName}; reason={message}");
            return false;
        }

        if (display.IsHdrEnabled == enable)
        {
            message = enable ? "HDR already enabled" : "HDR already disabled";
            AppLogger.Info($"Manual HDR request no-op. display={gdiDisplayName}; current={display.IsHdrEnabled}; desired={enable}");
            return true;
        }

        var success = _hdrController.TrySetHdr(display, enable, out message);
        if (success)
        {
            AppLogger.Info($"Manual HDR request applied. display={gdiDisplayName}; desired={enable}; result={message}");
        }
        else
        {
            AppLogger.Warn($"Manual HDR request failed. display={gdiDisplayName}; desired={enable}; result={message}");
        }

        return success;
    }

    private static IEnumerable<string> BuildCandidates(string processName, string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(processName))
        {
            yield return processName;
        }

        var processNameWithExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";
        if (!string.Equals(processNameWithExe, processName, StringComparison.OrdinalIgnoreCase))
        {
            yield return processNameWithExe;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            yield break;
        }

        yield return executablePath;
        var executableName = Path.GetFileName(executablePath);
        if (!string.IsNullOrWhiteSpace(executableName) &&
            !string.Equals(executableName, processName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(executableName, processNameWithExe, StringComparison.OrdinalIgnoreCase))
        {
            yield return executableName;
        }
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return string.IsNullOrWhiteSpace(process.ProcessName) ? "<unknown>" : process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return "<exited>";
        }
    }

    private static string DescribeMode(ProcessWatchRule rule)
    {
        if (rule.RegexMode)
        {
            return "regex";
        }

        if (rule.ExactMatch)
        {
            return rule.CaseSensitive ? "exact/case-sensitive" : "exact/case-insensitive";
        }

        return rule.CaseSensitive ? "includes-or-wildcard/case-sensitive" : "includes-or-wildcard/case-insensitive";
    }

    private static bool TryFindFirstMatchingRule(
        string candidate,
        IReadOnlyList<ProcessWatchRule> rules,
        out ProcessWatchRule matchedRule,
        out int ruleIndex)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!ProcessWatchMatcher.IsMatch(candidate, rule))
            {
                continue;
            }

            matchedRule = rule;
            ruleIndex = i;
            return true;
        }

        matchedRule = null!;
        ruleIndex = -1;
        return false;
    }

    private HashSet<string> BuildAvailableDisplaySet()
    {
        var displays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in _hdrController.GetDisplays())
        {
            if (!string.IsNullOrWhiteSpace(display.GdiDeviceName))
            {
                displays.Add(display.GdiDeviceName);
            }
        }

        foreach (var screen in Screen.AllScreens)
        {
            if (!string.IsNullOrWhiteSpace(screen.DeviceName))
            {
                displays.Add(screen.DeviceName);
            }
        }

        return displays;
    }

    private DisplayTargetResolution ResolveDisplayForRule(
        int processId,
        IReadOnlyDictionary<int, ResolvedProcessWindow> resolvedWindows,
        string? configuredTargetDisplay,
        IReadOnlySet<string> availableDisplays,
        bool hasObservedWindow)
    {
        var configured = string.IsNullOrWhiteSpace(configuredTargetDisplay)
            ? null
            : configuredTargetDisplay.Trim();
        if (!resolvedWindows.ContainsKey(processId) && hasObservedWindow)
        {
            var exitingLabel = string.IsNullOrWhiteSpace(configured)
                ? "(window disappeared -> exiting)"
                : $"(window disappeared -> exiting; target suspended: {configured})";
            return new DisplayTargetResolution
            {
                DesiredDisplay = null,
                ConfiguredTargetDisplay = configured,
                DisplayLabel = exitingLabel,
                IsFullscreenLike = false,
                RequestAllDisplays = false
            };
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (string.Equals(
                    configured,
                    ProcessWatchRule.SwitchAllDisplaysTargetValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                var isFullscreenLike = resolvedWindows.TryGetValue(processId, out var allWindow) &&
                    allWindow.IsFullscreenLike;
                var displayLabel = allWindow is null
                    ? "(switch all displays)"
                    : $"{allWindow.GdiDeviceName} (switch all displays)";
                return new DisplayTargetResolution
                {
                    DesiredDisplay = null,
                    ConfiguredTargetDisplay = ProcessWatchRule.SwitchAllDisplaysTargetValue,
                    DisplayLabel = displayLabel,
                    IsFullscreenLike = isFullscreenLike,
                    RequestAllDisplays = true
                };
            }

            if (availableDisplays.Contains(configured))
            {
                var isFullscreenLike = resolvedWindows.TryGetValue(processId, out var forcedWindow) &&
                    forcedWindow.IsFullscreenLike;
                var displayLabel = forcedWindow is null
                    ? $"(forced target: {configured})"
                    : $"{forcedWindow.GdiDeviceName} (forced target: {configured})";
                return new DisplayTargetResolution
                {
                    DesiredDisplay = configured,
                    ConfiguredTargetDisplay = configured,
                    DisplayLabel = displayLabel,
                    IsFullscreenLike = isFullscreenLike,
                    RequestAllDisplays = false
                };
            }

            var fallback = ResolveDefaultDisplay(processId, resolvedWindows);
            var fallbackLabel = fallback.DisplayLabel;
            if (string.IsNullOrWhiteSpace(fallbackLabel))
            {
                fallbackLabel = "(window not found)";
            }

            return new DisplayTargetResolution
            {
                DesiredDisplay = fallback.DesiredDisplay,
                ConfiguredTargetDisplay = configured,
                DisplayLabel = $"{fallbackLabel} (target unavailable: {configured}; using Default)",
                IsFullscreenLike = fallback.IsFullscreenLike,
                RequestAllDisplays = false
            };
        }

        return ResolveDefaultDisplay(processId, resolvedWindows);
    }

    private static HashSet<int> BuildActiveProcessIdSet(IEnumerable<Process> processes)
    {
        var ids = new HashSet<int>();
        foreach (var process in processes)
        {
            try
            {
                ids.Add(process.Id);
            }
            catch (InvalidOperationException)
            {
                // Process exited while enumerating.
            }
        }

        return ids;
    }

    private void CleanupObservedWindowState(IReadOnlySet<int> activeProcessIds)
    {
        _processesWithObservedWindow.RemoveWhere(pid => !activeProcessIds.Contains(pid));
    }

    private DisplayTargetResolution ResolveDefaultDisplay(
        int processId,
        IReadOnlyDictionary<int, ResolvedProcessWindow> resolvedWindows)
    {
        if (resolvedWindows.TryGetValue(processId, out var resolved))
        {
            return new DisplayTargetResolution
            {
                DesiredDisplay = resolved.GdiDeviceName,
                ConfiguredTargetDisplay = null,
                DisplayLabel = resolved.GdiDeviceName,
                IsFullscreenLike = resolved.IsFullscreenLike,
                RequestAllDisplays = false
            };
        }

        if (_displayResolver.TryGetPrimaryDisplay(out var primaryDisplay) &&
            !string.IsNullOrWhiteSpace(primaryDisplay))
        {
            return new DisplayTargetResolution
            {
                DesiredDisplay = primaryDisplay,
                ConfiguredTargetDisplay = null,
                DisplayLabel = $"(window not found -> primary {primaryDisplay})",
                IsFullscreenLike = false,
                RequestAllDisplays = false
            };
        }

        return new DisplayTargetResolution
        {
            DesiredDisplay = null,
            ConfiguredTargetDisplay = null,
            DisplayLabel = "(window not found)",
            IsFullscreenLike = false,
            RequestAllDisplays = false
        };
    }

    private static string? ResolveEffectiveTargetDisplay(
        string processName,
        string? executablePath,
        string? ruleTargetDisplay,
        IReadOnlyDictionary<string, string> processTargetDisplayOverrides,
        out string processTargetKey,
        out bool hasProcessTargetOverride)
    {
        ResolveProcessTargetDisplayOverride(
            processName,
            executablePath,
            processTargetDisplayOverrides,
            out processTargetKey,
            out var overrideTargetDisplay,
            out hasProcessTargetOverride);
        if (!string.IsNullOrWhiteSpace(overrideTargetDisplay))
        {
            return overrideTargetDisplay;
        }

        if (string.IsNullOrWhiteSpace(ruleTargetDisplay))
        {
            return null;
        }

        return ruleTargetDisplay.Trim();
    }

    private List<HdrDisplayStatus> EvaluateDisplayHdrStates(
        HashSet<string> matchedDisplays,
        bool switchAllDisplaysTogether,
        IReadOnlyDictionary<string, bool> displayAutoModes,
        bool forceSwitchAllDisplaysByTarget)
    {
        var statuses = new List<HdrDisplayStatus>();
        var primaryDisplayName = GetPrimaryDisplayName();
        var displays = _hdrController.GetDisplays();
        var coveredDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var desiredForAllDisplays =
            (switchAllDisplaysTogether && matchedDisplays.Count > 0) ||
            forceSwitchAllDisplaysByTarget;
        var matchedDisplayList = matchedDisplays.Count == 0
            ? "<none>"
            : string.Join(",", matchedDisplays.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
        AppLogger.Info(
            $"HDR evaluation start. displays={displays.Count}; matchedDisplays={matchedDisplays.Count}; matchedList={matchedDisplayList}; switchAllDisplaysTogether={switchAllDisplaysTogether}; forceSwitchAllDisplaysByTarget={forceSwitchAllDisplaysByTarget}; desiredForAllDisplays={desiredForAllDisplays}");
        foreach (var display in displays)
        {
            var isAuto = ResolveDisplayAutoMode(display.GdiDeviceName, displayAutoModes);
            var desired = isAuto
                ? display.IsHdrSupported &&
                    (desiredForAllDisplays ||
                     matchedDisplays.Contains(display.GdiDeviceName))
                : display.IsHdrEnabled;
            var hdrEnabled = display.IsHdrEnabled;
            var action = isAuto
                ? "No change (Auto=true)"
                : "Skipped auto control (Auto=false)";
            var isMatched = matchedDisplays.Contains(display.GdiDeviceName);
            AppLogger.Info(
                $"HDR decision. display={display.GdiDeviceName}; monitor={display.FriendlyName}; auto={isAuto}; supported={display.IsHdrSupported}; current={display.IsHdrEnabled}; desired={desired}; matched={isMatched}; desiredForAllDisplays={desiredForAllDisplays}");

            if (!display.IsHdrSupported)
            {
                action = "HDR unsupported";
                AppLogger.Info($"HDR decision skipped. display={display.GdiDeviceName}; reason={action}");
            }
            else if (isAuto && display.IsHdrEnabled != desired)
            {
                AppLogger.Info(
                    $"HDR apply begin. display={display.GdiDeviceName}; from={display.IsHdrEnabled}; to={desired}");
                if (_hdrController.TrySetHdr(display, desired, out var actionText))
                {
                    hdrEnabled = desired;
                    action = actionText;
                    AppLogger.Info(
                        $"HDR apply success. display={display.GdiDeviceName}; from={display.IsHdrEnabled}; to={desired}; result={actionText}");
                }
                else
                {
                    action = actionText;
                    AppLogger.Warn(
                        $"HDR apply failed. display={display.GdiDeviceName}; from={display.IsHdrEnabled}; to={desired}; result={actionText}");
                }
            }
            else
            {
                AppLogger.Info(
                    $"HDR apply skipped. display={display.GdiDeviceName}; current={display.IsHdrEnabled}; desired={desired}; auto={isAuto}; reason={action}");
            }

            statuses.Add(new HdrDisplayStatus
            {
                Display = display.GdiDeviceName,
                FriendlyName = display.FriendlyName,
                IsPrimary = IsPrimaryDisplay(display.GdiDeviceName, primaryDisplayName),
                IsHdrSupported = display.IsHdrSupported,
                IsHdrEnabled = hdrEnabled,
                DesiredHdrEnabled = desired,
                LastAction = action
            });
            coveredDisplays.Add(display.GdiDeviceName);
        }

        // Ensure all active screens are visible in UI even if HDR query temporarily fails.
        foreach (var screen in Screen.AllScreens)
        {
            if (!coveredDisplays.Add(screen.DeviceName))
            {
                continue;
            }

            AppLogger.Warn(
                $"HDR status unavailable for active screen. display={screen.DeviceName}; primary={screen.Primary}; desired={ResolveDisplayAutoMode(screen.DeviceName, displayAutoModes) && (desiredForAllDisplays || matchedDisplays.Contains(screen.DeviceName))}");
            statuses.Add(new HdrDisplayStatus
            {
                Display = screen.DeviceName,
                FriendlyName = screen.Primary ? $"{screen.DeviceName} (Primary)" : screen.DeviceName,
                IsPrimary = IsPrimaryDisplay(screen.DeviceName, primaryDisplayName),
                IsHdrSupported = false,
                IsHdrEnabled = false,
                DesiredHdrEnabled = ResolveDisplayAutoMode(screen.DeviceName, displayAutoModes) &&
                    (desiredForAllDisplays ||
                     matchedDisplays.Contains(screen.DeviceName)),
                LastAction = "Display detected; HDR state unavailable"
            });
        }

        statuses.Sort(static (a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));
        AppLogger.Info($"HDR evaluation completed. statusCount={statuses.Count}");
        return statuses;
    }

    private static bool ResolveDisplayAutoMode(
        string displayName,
        IReadOnlyDictionary<string, bool> displayAutoModes)
    {
        return !displayAutoModes.TryGetValue(displayName, out var autoMode) || autoMode;
    }

    private string? GetPrimaryDisplayName()
    {
        if (_displayResolver.TryGetPrimaryDisplay(out var primaryDisplay) &&
            !string.IsNullOrWhiteSpace(primaryDisplay))
        {
            return primaryDisplay;
        }

        var primaryScreenDeviceName = Screen.PrimaryScreen?.DeviceName;
        return string.IsNullOrWhiteSpace(primaryScreenDeviceName)
            ? null
            : primaryScreenDeviceName;
    }

    private static bool IsPrimaryDisplay(string displayName, string? primaryDisplayName)
    {
        return !string.IsNullOrWhiteSpace(primaryDisplayName) &&
            string.Equals(displayName, primaryDisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetExecutablePath(Process process, out string executablePath)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                executablePath = string.Empty;
                return false;
            }

            executablePath = path;
            return true;
        }
        catch (InvalidOperationException)
        {
            executablePath = string.Empty;
            return false;
        }
        catch (Win32Exception)
        {
            executablePath = string.Empty;
            return false;
        }
    }

    private static bool TryGetExecutablePathWithStats(
        Process process,
        ref int attempts,
        ref int succeeded,
        out string executablePath)
    {
        attempts++;
        var ok = TryGetExecutablePath(process, out executablePath);
        if (ok)
        {
            succeeded++;
        }

        return ok;
    }

    private static bool TryGetExecutablePathByProcessIdWithStats(
        int processId,
        ref int attempts,
        ref int succeeded,
        out string executablePath)
    {
        executablePath = string.Empty;
        try
        {
            using var process = Process.GetProcessById(processId);
            return TryGetExecutablePathWithStats(process, ref attempts, ref succeeded, out executablePath);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool RulesRequirePathCandidates(IReadOnlyList<ProcessWatchRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }

            if (rule.RegexMode)
            {
                return true;
            }

            var pattern = rule.Pattern;
            if (pattern.Contains('\\') || pattern.Contains('/') || pattern.Contains(':'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPathTargetOverrides(IReadOnlyDictionary<string, string> processTargetDisplayOverrides)
    {
        foreach (var key in processTargetDisplayOverrides.Keys)
        {
            if (key.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ResolveIgnoreState(
        string processName,
        string? executablePath,
        IReadOnlyDictionary<string, bool> ignoreMap,
        out string ignoreKey,
        out bool isIgnored,
        out bool isDefaultIgnoreApplied)
    {
        var normalizedName = processName.Trim();
        var nameKey = BuildNameIgnoreKey(normalizedName);
        var pathKey = string.IsNullOrWhiteSpace(executablePath)
            ? string.Empty
            : BuildPathIgnoreKey(executablePath);

        if (!string.IsNullOrEmpty(pathKey) && ignoreMap.TryGetValue(pathKey, out var pathIgnored))
        {
            ignoreKey = pathKey;
            isIgnored = pathIgnored;
            isDefaultIgnoreApplied = false;
            return;
        }

        if (TryResolvePathPrefixIgnore(executablePath, ignoreMap, out var pathPrefixKey, out var pathPrefixIgnored))
        {
            ignoreKey = pathPrefixKey;
            isIgnored = pathPrefixIgnored;
            isDefaultIgnoreApplied = false;
            return;
        }

        if (ignoreMap.TryGetValue(nameKey, out var nameIgnored))
        {
            ignoreKey = nameKey;
            isIgnored = nameIgnored;
            isDefaultIgnoreApplied = false;
            return;
        }

        if (IsDefaultIgnoredPath(executablePath))
        {
            ignoreKey = DefaultWindowsPathPrefixIgnoreKey;
            isIgnored = true;
            isDefaultIgnoreApplied = true;
            return;
        }

        var defaultIgnored = IsDefaultIgnoredProcessName(normalizedName);
        ignoreKey = !string.IsNullOrEmpty(pathKey) ? pathKey : nameKey;
        isIgnored = defaultIgnored;
        isDefaultIgnoreApplied = defaultIgnored;
    }

    private static void ResolveProcessTargetDisplayOverride(
        string processName,
        string? executablePath,
        IReadOnlyDictionary<string, string> processTargetDisplayOverrides,
        out string key,
        out string? targetDisplay,
        out bool hasOverride)
    {
        var pathKey = string.IsNullOrWhiteSpace(executablePath)
            ? string.Empty
            : BuildProcessTargetPathKey(executablePath);
        if (!string.IsNullOrWhiteSpace(pathKey) &&
            processTargetDisplayOverrides.TryGetValue(pathKey, out var pathValue) &&
            !string.IsNullOrWhiteSpace(pathValue))
        {
            key = pathKey;
            targetDisplay = pathValue.Trim();
            hasOverride = true;
            return;
        }

        var nameKey = IsValidProcessNameForTargetKey(processName)
            ? BuildProcessTargetNameKey(processName)
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(nameKey) &&
            processTargetDisplayOverrides.TryGetValue(nameKey, out var nameValue) &&
            !string.IsNullOrWhiteSpace(nameValue))
        {
            key = nameKey;
            targetDisplay = nameValue.Trim();
            hasOverride = true;
            return;
        }

        key = !string.IsNullOrWhiteSpace(pathKey)
            ? pathKey
            : nameKey;
        targetDisplay = null;
        hasOverride = false;
    }

    private static bool IsValidProcessNameForTargetKey(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var normalized = processName.Trim();
        return !normalized.StartsWith("<", StringComparison.Ordinal) &&
            !normalized.StartsWith("PID-", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildProcessTargetPathKey(string executablePath)
    {
        return $"path:{NormalizePath(executablePath)}";
    }

    public static string BuildProcessTargetNameKey(string processName)
    {
        return $"name:{processName.Trim()}";
    }

    public static string BuildPathIgnoreKey(string executablePath)
    {
        return $"path:{NormalizePath(executablePath)}";
    }

    public static string BuildPathPrefixIgnoreKey(string executablePathPrefix)
    {
        return $"pathprefix:{NormalizePath(executablePathPrefix)}";
    }

    public static string BuildNameIgnoreKey(string processName)
    {
        return $"name:{processName.Trim()}";
    }

    private static IReadOnlyList<string> BuildDefaultIgnoreKeys()
    {
        var keys = new List<string> { DefaultWindowsPathPrefixIgnoreKey };
        keys.AddRange(DefaultIgnoredProcessNames.Select(BuildNameIgnoreKey));
        return keys;
    }

    private static HashSet<string> BuildDefaultIgnoredProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in DefaultIgnoredProcessNameSeeds)
        {
            var normalized = NormalizeProcessNameForIgnore(seed);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            names.Add(normalized);
        }

        return names;
    }

    private static bool TryResolvePathPrefixIgnore(
        string? executablePath,
        IReadOnlyDictionary<string, bool> ignoreMap,
        out string ignoreKey,
        out bool isIgnored)
    {
        ignoreKey = string.Empty;
        isIgnored = false;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var normalizedPath = NormalizePath(executablePath);
        var bestMatchLength = -1;
        foreach (var entry in ignoreMap)
        {
            if (!entry.Key.StartsWith("pathprefix:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var prefix = entry.Key["pathprefix:".Length..];
            if (string.IsNullOrWhiteSpace(prefix))
            {
                continue;
            }

            if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (prefix.Length <= bestMatchLength)
            {
                continue;
            }

            ignoreKey = entry.Key;
            isIgnored = entry.Value;
            bestMatchLength = prefix.Length;
        }

        return bestMatchLength >= 0;
    }

    private static bool IsDefaultIgnoredPath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        return NormalizePath(executablePath).StartsWith(NormalizedDefaultWindowsPathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessNameForIgnore(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        while (normalized.EndsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }

        return normalized;
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('/', '\\');
    }

    private sealed class DisplayTargetResolution
    {
        public string? DesiredDisplay { get; init; }

        public string? ConfiguredTargetDisplay { get; init; }

        public required string DisplayLabel { get; init; }

        public required bool IsFullscreenLike { get; init; }

        public required bool RequestAllDisplays { get; init; }
    }
}
