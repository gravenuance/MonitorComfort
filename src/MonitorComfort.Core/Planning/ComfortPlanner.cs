using MonitorComfort.Core.Ambient;
using MonitorComfort.Core.Colour;
using MonitorComfort.Core.Settings;
using MonitorComfort.Core.Solar;

namespace MonitorComfort.Core.Planning;

public enum Phase
{
    Day,
    Evening,
    Night,
}

/// <summary>What fitted the schedule to the room: nothing, the modelled room lights, or a webcam reading.</summary>
public enum AmbientSource
{
    Schedule,
    RoomModel,
    Camera,
}

/// <summary>What one monitor should show now.</summary>
public sealed record MonitorTarget(string MonitorId, int Brightness, int Kelvin);

/// <summary>The schedule's result before per-monitor adjustments, plus each monitor's target.</summary>
public sealed record ComfortPlan(Phase Phase, AmbientSource Source, int Brightness, int Kelvin, IReadOnlyList<MonitorTarget> Targets);

/// <summary>
/// Blends the day, evening and night profiles by sun elevation and bedtime, then fits the
/// result to the room's light (measured by webcam, else modelled), each monitor's offset, and
/// whether something is full-screen.
/// </summary>
public static class ComfortPlanner
{
    /// <summary>How long the evening-to-night and night-to-morning blends take.</summary>
    public static readonly TimeSpan NightRamp = TimeSpan.FromMinutes(45);

    // Daylight blends in across civil twilight: fully dark below -6°, fully day above +6°.
    private const double DarkElevation = -6.0;
    private const double DayElevation = 6.0;

    public static ComfortPlan Plan(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        ComfortSettings settings,
        GeoLocation location,
        IReadOnlyList<string> monitorIds,
        string? fullscreenMonitorId,
        AmbientReading? ambient = null,
        int? previewBrightness = null)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitorIds);

        var daylight = Smoothstep(DarkElevation, DayElevation, SolarCalculator.Elevation(now, location));
        var localTime = LocalTime(now, timeZone);
        var nightWeight = NightWeight(localTime, settings.NightStart, settings.NightEnd);

        var darkBrightness = Lerp(settings.Evening.Brightness, settings.Night.Brightness, nightWeight);
        var darkKelvin = Lerp(settings.Evening.Kelvin, settings.Night.Kelvin, nightWeight);
        var brightness = (int)Math.Round(Lerp(darkBrightness, settings.Day.Brightness, daylight));
        var kelvin = (int)Math.Round(Lerp(darkKelvin, settings.Day.Kelvin, daylight));

        var camera = settings.CameraLight;
        AmbientSource source;
        if (camera.IsReady && ambient is not null && AmbientMapping.IsFresh(ambient, now))
        {
            // The measurement replaces the schedule's brightness; its colour is blended with the schedule's.
            source = AmbientSource.Camera;
            brightness = AmbientMapping.Brightness(camera.Calibration!, ambient.LightStops, camera.BrightnessPerStop);
            if (AmbientMapping.ReliableKelvin(camera.Calibration!, ambient) is { } lightKelvin)
            {
                kelvin = RoomWarmth.Fit(kelvin, ColourTemperature.Clamp(lightKelvin), daylight);
            }
        }
        else if (settings.RoomLight.Enabled && LitFraction(localTime, settings.RoomLight) is { } litFraction)
        {
            source = AmbientSource.RoomModel;
            var floor = MinimumBrightness(settings.RoomLight.Level);
            if (settings.RoomLight.Curve)
            {
                floor = (int)Math.Round(floor * RoomLightCurve.Factor(litFraction));
            }

            brightness = Math.Max(brightness, floor);
            kelvin = RoomWarmth.Fit(kelvin, LightKelvin(settings.RoomLight.Colour), daylight);
        }
        else
        {
            source = AmbientSource.Schedule;
        }

        // While the user drags the calibration slider, show that brightness; offsets and full-screen still apply.
        if (previewBrightness is { } preview)
        {
            brightness = Math.Clamp(preview, 0, 100);
        }

        kelvin = ColourTemperature.Clamp(kelvin);
        var phase = daylight >= 0.5 ? Phase.Day : nightWeight >= 0.5 ? Phase.Night : Phase.Evening;
        var dimOthers = fullscreenMonitorId is not null && settings.Fullscreen.DimOtherMonitors;

        var targets = new List<MonitorTarget>(monitorIds.Count);
        foreach (var id in monitorIds)
        {
            var monitor = settings.ForMonitor(id);
            if (!monitor.Enabled)
            {
                continue;
            }

            var monitorBrightness = Math.Clamp(brightness + monitor.BrightnessOffset, 0, 100);
            if (dimOthers && !string.Equals(id, fullscreenMonitorId, StringComparison.OrdinalIgnoreCase))
            {
                monitorBrightness = (int)Math.Round(monitorBrightness * settings.Fullscreen.OtherMonitorBrightnessPercent / 100.0);
            }

            targets.Add(new MonitorTarget(id, monitorBrightness, kelvin));
        }

        return new ComfortPlan(phase, source, brightness, kelvin, targets);
    }

    /// <summary>
    /// 0 outside the night window, 1 inside it, ramping over <see cref="NightRamp"/> after each
    /// boundary. The window may cross midnight.
    /// </summary>
    public static double NightWeight(TimeOnly now, TimeOnly nightStart, TimeOnly nightEnd)
    {
        const double minutesPerDay = 24 * 60;
        var ramp = NightRamp.TotalMinutes;
        var sinceStart = Wrap((now - nightStart).TotalMinutes, minutesPerDay);
        var nightLength = Wrap((nightEnd - nightStart).TotalMinutes, minutesPerDay);

        if (sinceStart < nightLength)
        {
            return Math.Min(1.0, sinceStart / ramp);
        }

        var sinceEnd = sinceStart - nightLength;
        var weightAtEnd = Math.Min(1.0, nightLength / ramp);
        return sinceEnd < ramp ? weightAtEnd * (1.0 - (sinceEnd / ramp)) : 0.0;
    }

    /// <summary>Whether modelled room lights are on: from the on time up to, not including, the off time, in local time.</summary>
    public static bool IsRoomLit(DateTimeOffset now, TimeZoneInfo timeZone, RoomLightSettings roomLight)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return LitFraction(LocalTime(now, timeZone), roomLight) is not null;
    }

    /// <summary>
    /// How far through the lit window <paramref name="localTime"/> is, 0 at the on time up to just under 1 at
    /// the off time; null while the lights are off. The window may cross midnight.
    /// </summary>
    public static double? LitFraction(TimeOnly localTime, RoomLightSettings roomLight)
    {
        ArgumentNullException.ThrowIfNull(roomLight);

        const double minutesPerDay = 24 * 60;
        var sinceOn = Wrap((localTime - roomLight.OnTime).TotalMinutes, minutesPerDay);
        var length = Wrap((roomLight.OffTime - roomLight.OnTime).TotalMinutes, minutesPerDay);
        return sinceOn < length ? sinceOn / length : null;
    }

    public static int MinimumBrightness(LightLevel level) => level switch
    {
        LightLevel.Dim => 25,
        LightLevel.Normal => 40,
        LightLevel.Bright => 55,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unhandled light level"),
    };

    public static int LightKelvin(LightColour colour) => colour switch
    {
        LightColour.Warm => 2700,
        LightColour.Neutral => 4000,
        LightColour.Daylight => 6500,
        _ => throw new ArgumentOutOfRangeException(nameof(colour), colour, "Unhandled light colour"),
    };

    private static TimeOnly LocalTime(DateTimeOffset now, TimeZoneInfo timeZone)
        => TimeOnly.FromTimeSpan(TimeZoneInfo.ConvertTime(now, timeZone).TimeOfDay);

    private static double Smoothstep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    private static double Lerp(double from, double to, double weight) => from + ((to - from) * weight);

    private static double Wrap(double value, double period)
    {
        var result = value % period;
        return result < 0 ? result + period : result;
    }
}
