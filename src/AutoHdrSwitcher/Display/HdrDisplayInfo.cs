namespace AutoHdrSwitcher.Display;

internal sealed class HdrDisplayInfo
{
    public required string DisplayKey { get; init; }

    public required string GdiDeviceName { get; init; }

    public required string FriendlyName { get; init; }

    public required bool IsHdrSupported { get; init; }

    public required bool IsHdrEnabled { get; init; }

    public required uint TargetId { get; init; }

    public required NativeDisplay.Luid AdapterId { get; init; }
}
