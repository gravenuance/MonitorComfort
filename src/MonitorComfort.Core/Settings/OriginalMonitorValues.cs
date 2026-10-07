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

    /// <summary>Whether enough was read to put the monitor back; a monitor still waking answers only some reads.</summary>
    public bool IsComplete
        => Brightness is not null
            && (ColourPreset, RedGain, GreenGain, BlueGain) is (not null, not null, not null, not null) or (null, null, null, null);
}

public sealed record OriginalMonitorValues
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<MonitorSnapshot> Monitors { get; init; } = [];

    public MonitorSnapshot? Find(string monitorId)
        => Monitors.FirstOrDefault(m => string.Equals(m.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds a complete snapshot for a monitor not captured before, so the first values are never
    /// overwritten and a partial read is retried rather than kept.
    /// </summary>
    public OriginalMonitorValues WithFirstCapture(MonitorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return !snapshot.IsComplete || Find(snapshot.MonitorId) is not null
            ? this
            : this with { Monitors = [.. Monitors, snapshot] };
    }
}
