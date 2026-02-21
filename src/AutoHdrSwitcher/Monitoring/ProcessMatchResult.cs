namespace AutoHdrSwitcher.Monitoring;

public sealed class ProcessMatchResult
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public required string MatchInput { get; init; }

    public required string RulePattern { get; init; }

    public required string Mode { get; init; }

    public string Display { get; init; } = "(window not found)";

    public bool IsFullscreenLike { get; init; }
}
