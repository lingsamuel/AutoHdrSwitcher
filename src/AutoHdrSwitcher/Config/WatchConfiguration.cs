using System.Collections.Generic;
using AutoHdrSwitcher.Matching;

namespace AutoHdrSwitcher.Config;

public sealed class WatchConfiguration
{
    public int PollIntervalSeconds { get; init; } = 2;

    public bool PollingEnabled { get; init; }

    public bool MinimizeToTray { get; init; } = true;

    public bool MonitorAllFullscreenProcesses { get; init; }

    public int? MainSplitterDistance { get; init; }

    public int? RuntimeTopSplitterDistance { get; init; }

    public int? RuntimeBottomSplitterDistance { get; init; }

    public Dictionary<string, bool> FullscreenIgnoreMap { get; init; } = new();

    public List<ProcessWatchRule> ProcessRules { get; init; } = new();
}
