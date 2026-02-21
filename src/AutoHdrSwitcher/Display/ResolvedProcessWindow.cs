namespace AutoHdrSwitcher.Display;

public sealed class ResolvedProcessWindow
{
    public required int ProcessId { get; init; }

    public required string GdiDeviceName { get; init; }

    public required bool IsFullscreenLike { get; init; }

    public required int Area { get; init; }

    public required bool IsForeground { get; init; }
}
