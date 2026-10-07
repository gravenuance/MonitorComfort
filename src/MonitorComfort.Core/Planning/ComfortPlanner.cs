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

/// <summary>What one monitor should show now.</summary>
public sealed record MonitorTarget(string MonitorId, int Brightness, int Kelvin);

/// <summary>The schedule's result before per-monitor adjustments, plus each monitor's target.</summary>
public sealed record ComfortPlan(Phase Phase, bool RoomLit, int Brightness, int Kelvin, IReadOnlyList<MonitorTarget> Targets);

/// <summary>
/// Blends the day, evening and night profiles by sun elevation and bedtime, then fits the
/// result to the room's light, each monitor's offset, and whether something is full-screen.
/// </summary>
public static class ComfortPlanner
{
    /// <summary>How long the evening-to-night and night-to-morning blends take.</summary>
    public static readonly TimeSpan NightRamp = TimeSpan.FromMinutes(45);

    // Daylight blends in across civil twilight: fully dark below -6°, fully day above +6°.
    private const double DarkElevation = -6.0;
    private const double DayElevation = 6.0;

    // A screen much warmer than the room's light reads as orange; stay within this of it.
    private const int RoomLightKelvinMargin = 500;

    public static ComfortPlan Plan(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        ComfortSettings settings,
        GeoLocation location,
        IReadOnlyList<string> monitorIds,
        string? fullscreenMonitorId)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitorIds);

        var daylight = Smoothstep(DarkElevation, DayElevation, SolarCalculator.Elevation(now, location));
        var localTime = TimeOnly.FromTimeSpan(TimeZoneInfo.ConvertTime(now, timeZone).TimeOfDay);
        var nightWeight = NightWeight(localTime, settings.NightStart, settings.NightEnd);

        var darkBrightness = Lerp(settings.Evening.Brightness, settings.Night.Brightness, nightWeight);
        var darkKelvin = Lerp(settings.Evening.Kelvin, settings.Night.Kelvin, nightWeight);
        var brightness = (int)Math.Round(Lerp(darkBrightness, settings.Day.Brightness, daylight));
        var kelvin = (int)Math.Round(Lerp(darkKelvin, settings.Day.Kelvin, daylight));

        var roomLit = settings.RoomLight.Enabled && IsRoomLit(now, timeZone, settings.RoomLight, location);
        if (roomLit)
        {
            brightness = Math.Max(brightness, MinimumBrightness(settings.RoomLight.Level));
            kelvin = Math.Max(kelvin, LightKelvin(settings.RoomLight.Colour) - RoomLightKelvinMargin);
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

        return new ComfortPlan(phase, roomLit, brightness, kelvin, targets);
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

    /// <summary>Whether modelled room lights are on: from sunset (plus offset) until sunrise or the set off time.</summary>
    public static bool IsRoomLit(DateTimeOffset now, TimeZoneInfo timeZone, RoomLightSettings roomLight, GeoLocation location)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(roomLight);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        foreach (var day in (ReadOnlySpan<DateOnly>)[today.AddDays(-1), today])
        {
            var events = SolarCalculator.Events(day, location);
            switch (events.Kind)
            {
                case DayKind.PolarNight when day == today:
                    return true;
                case DayKind.PolarNight or DayKind.PolarDay:
                    continue;
                case DayKind.Normal:
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled day kind {events.Kind}");
            }

            var on = events.Sunset!.Value.AddMinutes(roomLight.OnMinutesAfterSunset);
            var off = roomLight.OffAt switch
            {
                LightsOff.AtSunrise => SolarCalculator.Events(day.AddDays(1), location).Sunrise ?? on.AddDays(1),
                LightsOff.AtTime => NextLocal(events.Sunset.Value, roomLight.OffTime, timeZone),
                _ => throw new InvalidOperationException($"Unhandled lights-off rule {roomLight.OffAt}"),
            };

            if (now >= on && now < off)
            {
                return true;
            }
        }

        return false;
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

    private static DateTimeOffset NextLocal(DateTimeOffset after, TimeOnly time, TimeZoneInfo timeZone)
    {
        var localAfter = TimeZoneInfo.ConvertTime(after, timeZone);
        var candidate = DateOnly.FromDateTime(localAfter.DateTime).ToDateTime(time);
        if (candidate <= localAfter.DateTime)
        {
            candidate = candidate.AddDays(1);
        }

        return new DateTimeOffset(candidate, timeZone.GetUtcOffset(candidate));
    }

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
