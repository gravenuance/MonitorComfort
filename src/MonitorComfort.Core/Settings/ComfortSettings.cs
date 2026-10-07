using System.Collections.Frozen;
using MonitorComfort.Core.Colour;
using MonitorComfort.Core.Solar;

namespace MonitorComfort.Core.Settings;

/// <summary>Brightness (0–100) and white point (kelvin) for one part of the day.</summary>
public sealed record DisplayProfile(int Brightness, int Kelvin);

public enum LightsOff
{
    AtSunrise,
    AtTime,
}

public enum LightColour
{
    Warm,
    Neutral,
    Daylight,
}

public enum LightLevel
{
    Dim,
    Normal,
    Bright,
}

/// <summary>Artificial room light that switches on around sunset.</summary>
public sealed record RoomLightSettings
{
    public bool Enabled { get; init; } = true;

    public int OnMinutesAfterSunset { get; init; }

    public LightsOff OffAt { get; init; } = LightsOff.AtSunrise;

    public TimeOnly OffTime { get; init; } = new(23, 0);

    public LightColour Colour { get; init; } = LightColour.Neutral;

    public LightLevel Level { get; init; } = LightLevel.Normal;
}

public sealed record FullscreenSettings
{
    public bool DimOtherMonitors { get; init; } = true;

    /// <summary>Other monitors' brightness as a percentage of their scheduled brightness.</summary>
    public int OtherMonitorBrightnessPercent { get; init; } = 20;
}

public sealed record MonitorSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>Added to the scheduled brightness so differently bright panels look alike.</summary>
    public int BrightnessOffset { get; init; }

    /// <summary>The colour gain each channel uses at 6500K.</summary>
    public int NeutralGain { get; init; } = 50;

    public static MonitorSettings Default { get; } = new();
}

public sealed record ComfortSettings
{
    public const int CurrentSchemaVersion = 1;

    private readonly FrozenDictionary<string, MonitorSettings> _monitors =
        FrozenDictionary<string, MonitorSettings>.Empty;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Stockholm unless the user sets another; nothing is adjusted while it is null.</summary>
    public GeoLocation? Location { get; init; } = new GeoLocation(59.33, 18.07);

    public TimeOnly NightStart { get; init; } = new(22, 30);

    public TimeOnly NightEnd { get; init; } = new(7, 0);

    public DisplayProfile Day { get; init; } = new(60, 6500);

    public DisplayProfile Evening { get; init; } = new(35, 4500);

    public DisplayProfile Night { get; init; } = new(15, 3400);

    public RoomLightSettings RoomLight { get; init; } = new();

    public FullscreenSettings Fullscreen { get; init; } = new();

    /// <summary>Keyed by PnP hardware ID, matched case-insensitively however the value was built.</summary>
    public IReadOnlyDictionary<string, MonitorSettings> Monitors
    {
        get => _monitors;
        init => _monitors = (value ?? FrozenDictionary<string, MonitorSettings>.Empty)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public bool StartWithWindows { get; init; } = true;

    public MonitorSettings ForMonitor(string monitorId)
        => Monitors.TryGetValue(monitorId, out var settings) ? settings : MonitorSettings.Default;

    public ComfortSettings WithMonitor(string monitorId, MonitorSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);
        ArgumentNullException.ThrowIfNull(settings);
        var copy = new Dictionary<string, MonitorSettings>(Monitors, StringComparer.OrdinalIgnoreCase)
        {
            [monitorId] = settings,
        };
        return this with { Monitors = copy };
    }

    /// <summary>Clamps every value into its valid range and lists what had to change.</summary>
    public (ComfortSettings Settings, IReadOnlyList<string> Corrections) Normalize()
    {
        var corrections = new List<string>();

        int Clamp(string name, int value, int min, int max)
        {
            var clamped = Math.Clamp(value, min, max);
            if (clamped != value)
            {
                corrections.Add($"{name} {value} is outside {min}–{max}; using {clamped}");
            }

            return clamped;
        }

        DisplayProfile Profile(string name, DisplayProfile? profile, DisplayProfile fallback)
        {
            if (profile is null)
            {
                corrections.Add($"{name} profile missing; using defaults");
                return fallback;
            }

            return new DisplayProfile(
                Clamp($"{name} brightness", profile.Brightness, 0, 100),
                Clamp($"{name} colour temperature", profile.Kelvin, ColourTemperature.Minimum, ColourTemperature.Neutral));
        }

        var defaults = new ComfortSettings();
        var roomLight = RoomLight ?? defaults.RoomLight;
        var fullscreen = Fullscreen ?? defaults.Fullscreen;
        var monitors = Monitors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value is null
                ? MonitorSettings.Default
                : pair.Value with
                {
                    BrightnessOffset = Clamp($"{pair.Key} brightness offset", pair.Value.BrightnessOffset, -50, 50),
                    NeutralGain = Clamp($"{pair.Key} neutral gain", pair.Value.NeutralGain, 1, 100),
                },
            StringComparer.OrdinalIgnoreCase);

        var normalized = this with
        {
            SchemaVersion = CurrentSchemaVersion,
            Day = Profile("Day", Day, defaults.Day),
            Evening = Profile("Evening", Evening, defaults.Evening),
            Night = Profile("Night", Night, defaults.Night),
            RoomLight = roomLight with
            {
                OnMinutesAfterSunset = Clamp("Lights-on offset", roomLight.OnMinutesAfterSunset, -180, 180),
            },
            Fullscreen = fullscreen with
            {
                OtherMonitorBrightnessPercent = Clamp(
                    "Full-screen dim level", fullscreen.OtherMonitorBrightnessPercent, 0, 100),
            },
            Monitors = monitors,
        };
        return (normalized, corrections);
    }
}
