using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AutoHdrSwitcher.Display;
using AutoHdrSwitcher.Matching;
using System.Windows.Forms;

namespace AutoHdrSwitcher.Monitoring;

public sealed class ProcessMonitorService
{
    private readonly ProcessDisplayResolver _displayResolver = new();
    private readonly HdrController _hdrController = new();

    public ProcessMonitorSnapshot Evaluate(
        IReadOnlyCollection<ProcessWatchRule> rules,
        bool monitorAllFullscreenProcesses)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var processArray = Process.GetProcesses();
        var resolvedWindows = _displayResolver.CaptureProcessWindows();
        var matches = new List<ProcessMatchResult>();
        var fullscreenProcesses = new List<FullscreenProcessInfo>();
        var matchedPids = new HashSet<int>();
        var matchedDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processNames = new Dictionary<int, string>();
        _ = _displayResolver.TryGetPrimaryDisplay(out var primaryDisplay);

        foreach (var process in processArray)
        {
            using (process)
            {
                try
                {
                    var processId = process.Id;
                    var safeName = SafeProcessName(process);
                    processNames[processId] = safeName;

                    foreach (var candidate in BuildCandidates(process))
                    {
                        if (!ProcessWatchMatcher.TryFindFirstMatch(candidate, rules, out var matchedRule))
                        {
                            continue;
                        }

                        if (!matchedPids.Add(processId))
                        {
                            break;
                        }

                        var display = ResolveDisplay(
                            processId,
                            resolvedWindows,
                            matchedDisplays,
                            out var isFullscreenLike,
                            out var hasResolvedDisplay);
                        if (!hasResolvedDisplay && !string.IsNullOrWhiteSpace(primaryDisplay))
                        {
                            matchedDisplays.Add(primaryDisplay);
                            display = $"(pending -> primary {primaryDisplay})";
                        }

                        matches.Add(new ProcessMatchResult
                        {
                            ProcessId = processId,
                            ProcessName = safeName,
                            MatchInput = candidate,
                            RulePattern = matchedRule!.Pattern,
                            Mode = DescribeMode(matchedRule),
                            Display = display,
                            IsFullscreenLike = isFullscreenLike
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
            var name = processNames.TryGetValue(window.ProcessId, out var safeName)
                ? safeName
                : $"PID-{window.ProcessId}";
            var matchedByRule = matchedPids.Contains(window.ProcessId);
            fullscreenProcesses.Add(new FullscreenProcessInfo
            {
                ProcessId = window.ProcessId,
                ProcessName = name,
                Display = window.GdiDeviceName,
                MatchedByRule = matchedByRule
            });

            if (monitorAllFullscreenProcesses)
            {
                matchedDisplays.Add(window.GdiDeviceName);
            }
        }

        var displayStatuses = EvaluateDisplayHdrStates(matchedDisplays);
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

    private static IEnumerable<string> BuildCandidates(Process process)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processName = SafeProcessName(process);

        AddCandidate(unique, processName);
        AddCandidate(unique, processName + ".exe");

        try
        {
            var modulePath = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(modulePath))
            {
                AddCandidate(unique, modulePath);
                AddCandidate(unique, Path.GetFileName(modulePath));
            }
        }
        catch (InvalidOperationException)
        {
            // Process exited during inspection.
        }
        catch (Win32Exception)
        {
            // Access denied for MainModule.
        }

        return unique;
    }

    private static void AddCandidate(HashSet<string> target, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(value);
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

    private string ResolveDisplay(
        int processId,
        IReadOnlyDictionary<int, ResolvedProcessWindow> resolvedWindows,
        HashSet<string> matchedDisplays,
        out bool isFullscreenLike,
        out bool hasResolvedDisplay)
    {
        if (!resolvedWindows.TryGetValue(processId, out var resolved))
        {
            hasResolvedDisplay = false;
            isFullscreenLike = false;
            return "(window not found)";
        }

        var gdiName = resolved.GdiDeviceName;
        isFullscreenLike = resolved.IsFullscreenLike;
        hasResolvedDisplay = true;
        matchedDisplays.Add(gdiName);
        return gdiName;
    }

    private List<HdrDisplayStatus> EvaluateDisplayHdrStates(HashSet<string> matchedDisplays)
    {
        var statuses = new List<HdrDisplayStatus>();
        var displays = _hdrController.GetDisplays();
        var coveredDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in displays)
        {
            var desired = display.IsHdrSupported && matchedDisplays.Contains(display.GdiDeviceName);
            var hdrEnabled = display.IsHdrEnabled;
            var action = "No change";

            if (!display.IsHdrSupported)
            {
                action = "HDR unsupported";
            }
            else if (display.IsHdrEnabled != desired)
            {
                if (_hdrController.TrySetHdr(display, desired, out var actionText))
                {
                    hdrEnabled = desired;
                    action = actionText;
                }
                else
                {
                    action = actionText;
                }
            }

            statuses.Add(new HdrDisplayStatus
            {
                Display = display.GdiDeviceName,
                FriendlyName = display.FriendlyName,
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

            statuses.Add(new HdrDisplayStatus
            {
                Display = screen.DeviceName,
                FriendlyName = screen.Primary ? $"{screen.DeviceName} (Primary)" : screen.DeviceName,
                IsHdrSupported = false,
                IsHdrEnabled = false,
                DesiredHdrEnabled = matchedDisplays.Contains(screen.DeviceName),
                LastAction = "Display detected; HDR state unavailable"
            });
        }

        statuses.Sort(static (a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));
        return statuses;
    }
}
