namespace MonitorComfort.Core.Ambient;

/// <summary>A manual camera setting: exposure in log2 seconds and a gain value.</summary>
public readonly record struct CameraSetting(int ExposureLog2, int Gain);

/// <summary>A manual camera control's range as the driver reports it; values off its step grid may be rejected.</summary>
public sealed record ControlRange
{
    public ControlRange(int min, int max, int step)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        Min = min;
        Max = max;

        // Some drivers report a step of 0 for a continuous control.
        Step = Math.Max(1, step);
    }

    public int Min { get; }

    public int Max { get; }

    public int Step { get; }

    /// <summary>The nearest value on the step grid from <see cref="Min"/>, within the range.</summary>
    public int Snap(int value)
    {
        var steps = (int)Math.Round((Math.Clamp(value, Min, Max) - (double)Min) / Step, MidpointRounding.AwayFromZero);
        var snapped = Min + (steps * Step);
        return snapped > Max ? snapped - Step : snapped;
    }
}

/// <summary>The camera's manual exposure and gain controls; a camera without a manual gain has none.</summary>
public sealed record CameraLimits(ControlRange Exposure, ControlRange? Gain)
{
    /// <summary>The gain the response's exposure sweep is measured at, so readings there depend least on the gain curve.</summary>
    public int MeteringGain => Gain is null ? 0 : Gain.Snap(Gain.Min + ((Gain.Max - Gain.Min) / 4));

    /// <summary>How far metering raises the gain at a time: a quarter of the range, or one step if that is more.</summary>
    public int GainIncrement => Gain is null ? 0 : Spacing(Gain, 4);

    /// <summary>
    /// Gains from the lowest to the highest, about a <paramref name="divisions"/>th of the range apart and on the
    /// step grid; empty without a gain control.
    /// </summary>
    public IReadOnlyList<int> GainLadder(int divisions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(divisions, 1);
        if (Gain is not { } gain)
        {
            return [];
        }

        var spacing = Spacing(gain, divisions);
        var gains = new List<int>();
        for (var value = gain.Min; ; value = Math.Min(value + spacing, gain.Max))
        {
            var snapped = gain.Snap(value);
            if (gains.Count == 0 || snapped > gains[^1])
            {
                gains.Add(snapped);
            }

            if (value == gain.Max)
            {
                return gains;
            }
        }
    }

    private static int Spacing(ControlRange gain, int divisions) => Math.Max(gain.Step, (gain.Max - gain.Min) / divisions);
}

public static class LockedMetering
{
    /// <summary>Below this the picture is close to the sensor's black level and reads too dark.</summary>
    public const double DarkestY = 0.02;

    /// <summary>Above this the brightest parts of the room start to clip.</summary>
    public const double BrightestY = 0.15;

    private const int MaximumSteps = 16;

    /// <summary>
    /// Locks the metering gain and steps exposure, then gain only once exposure is at its longest, until
    /// the picture is in the well-measured range or the limits are reached. Only the exposure automatic
    /// exposure reported is used, as a starting point: some cameras (the C920) report a stale gain.
    /// </summary>
    public static (CameraSetting Setting, double MeanY) Measure(
        CameraLimits limits, CameraSetting reported, Func<CameraSetting, double> lockAndRead)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(lockAndRead);

        var setting = new CameraSetting(limits.Exposure.Snap(reported.ExposureLog2), limits.MeteringGain);
        var meanY = lockAndRead(setting);
        for (var step = 1; step < MaximumSteps; step++)
        {
            if (Next(limits, setting, meanY) is not { } next)
            {
                break;
            }

            setting = next;
            meanY = lockAndRead(setting);
        }

        return (setting, meanY);
    }

    private static CameraSetting? Next(CameraLimits limits, CameraSetting setting, double meanY)
    {
        var exposure = limits.Exposure;
        if (meanY > BrightestY)
        {
            // A darker setting than the metering gain allows only when exposure can't shorten further.
            var shorter = exposure.Snap(setting.ExposureLog2 - exposure.Step);
            return shorter < setting.ExposureLog2 ? setting with { ExposureLog2 = shorter } : null;
        }

        if (meanY >= DarkestY)
        {
            return null;
        }

        var longer = exposure.Snap(setting.ExposureLog2 + exposure.Step);
        if (longer > setting.ExposureLog2)
        {
            return setting with { ExposureLog2 = longer };
        }

        if (limits.Gain is not { } gain)
        {
            return null;
        }

        var higher = gain.Snap(Math.Min(gain.Max, setting.Gain + limits.GainIncrement));
        return higher > setting.Gain ? setting with { Gain = higher } : null;
    }
}
