using System.Collections.Generic;
using AutoHdrSwitcher.Matching;

namespace AutoHdrSwitcher.Config;

public sealed class WatchConfiguration
{
    public int PollIntervalSeconds { get; init; } = 2;

    public bool MonitorAllFullscreenProcesses { get; init; }

    public List<ProcessWatchRule> ProcessRules { get; init; } = new();
}
