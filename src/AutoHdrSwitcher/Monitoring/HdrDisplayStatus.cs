namespace AutoHdrSwitcher.Monitoring;

public sealed class HdrDisplayStatus
{
    public required string Display { get; init; }

    public required string FriendlyName { get; init; }

    public required bool IsPrimary { get; init; }

    public required bool IsHdrSupported { get; init; }

    public required bool IsHdrEnabled { get; init; }

    public required bool DesiredHdrEnabled { get; init; }

    public required string LastAction { get; init; }
}
