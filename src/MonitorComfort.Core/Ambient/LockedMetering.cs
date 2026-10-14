namespace MonitorComfort.Core.Ambient;

/// <summary>A manual camera setting: exposure in log2 seconds and a gain value.</summary>
public readonly record struct CameraSetting(int ExposureLog2, int Gain);

/// <summary>The camera's exposure and gain ranges.</summary>
public sealed record CameraLimits(int MinExposureLog2, int MaxExposureLog2, int MinGain, int MaxGain)
{
    /// <summary>The gain the response's exposure sweep is measured at, so readings there depend least on the gain curve.</summary>
    public int MeteringGain => MinGain + ((MaxGain - MinGain) / 4);

    public int GainStep => Math.Max(1, (MaxGain - MinGain) / 4);
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

        var setting = new CameraSetting(
            Math.Clamp(reported.ExposureLog2, limits.MinExposureLog2, limits.MaxExposureLog2),
            limits.MeteringGain);
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
        if (meanY > BrightestY)
        {
            // A darker setting than the metering gain allows only when exposure can't shorten further.
            return setting.ExposureLog2 > limits.MinExposureLog2
                ? setting with { ExposureLog2 = setting.ExposureLog2 - 1 }
                : null;
        }

        if (meanY >= DarkestY)
        {
            return null;
        }

        if (setting.ExposureLog2 < limits.MaxExposureLog2)
        {
            return setting with { ExposureLog2 = setting.ExposureLog2 + 1 };
        }

        return setting.Gain < limits.MaxGain
            ? setting with { Gain = Math.Min(limits.MaxGain, setting.Gain + limits.GainStep) }
            : null;
    }
}
