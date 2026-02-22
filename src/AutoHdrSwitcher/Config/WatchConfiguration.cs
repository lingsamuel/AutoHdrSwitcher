using System.Collections.Generic;
using AutoHdrSwitcher.Matching;

namespace AutoHdrSwitcher.Config;

public sealed class WatchConfiguration
{
    public int PollIntervalSeconds { get; init; } = 2;

    public bool PollingEnabled { get; init; }

    public bool MinimizeToTray { get; init; } = true;

    public bool EnableLogging { get; init; } = true;

    public bool AutoRequestAdminForTrace { get; init; }

    public bool MonitorAllFullscreenProcesses { get; init; }

    public bool SwitchAllDisplaysTogether { get; init; }

    public int? MainSplitterDistance { get; init; }

    public int? RuntimeTopSplitterDistance { get; init; }

    public int? RuntimeBottomSplitterDistance { get; init; }

    public Dictionary<string, bool> FullscreenIgnoreMap { get; init; } = new();

    public Dictionary<string, bool> DisplayAutoModes { get; init; } = new();

    public Dictionary<string, string> ProcessTargetDisplayOverrides { get; init; } = new();

    public List<ProcessWatchRule> ProcessRules { get; init; } = new();
}
