namespace MonitorComfort.Core.Settings;

/// <summary>A monitor's own settings captured before this app first changed them.</summary>
public sealed record MonitorSnapshot
{
    public required string MonitorId { get; init; }

    public required string Name { get; init; }

    public DateTimeOffset CapturedAt { get; init; }

    public int? Brightness { get; init; }

    public int? ColourPreset { get; init; }

    public int? RedGain { get; init; }

    public int? GreenGain { get; init; }

    public int? BlueGain { get; init; }
}

public sealed record OriginalMonitorValues
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<MonitorSnapshot> Monitors { get; init; } = [];

    public MonitorSnapshot? Find(string monitorId)
        => Monitors.FirstOrDefault(m => string.Equals(m.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a snapshot only for a monitor not captured before, so the first values are never overwritten.</summary>
    public OriginalMonitorValues WithFirstCapture(MonitorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Find(snapshot.MonitorId) is not null ? this : this with { Monitors = [.. Monitors, snapshot] };
    }
}
