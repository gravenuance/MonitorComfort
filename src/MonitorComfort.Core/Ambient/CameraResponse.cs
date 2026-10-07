namespace MonitorComfort.Core.Ambient;

/// <summary>A gain setting and how many stops (doublings of light) it adds over the lowest gain.</summary>
public sealed record GainPoint(int Gain, double Stops);

/// <summary>
/// How a webcam's picture brightness responds to exposure and gain, measured once per camera
/// so readings taken at different automatic settings can be compared on one light scale.
/// </summary>
public sealed record CameraResponse(double Gamma, IReadOnlyList<GainPoint> GainCurve)
{
    // Below this the picture is sensor noise; above it highlights start to clip.
    private const double DarkestUsable = 0.002;
    private const double BrightestUsable = 0.6;
    private const int MinimumExposurePoints = 3;
    private const double MinimumGamma = 0.5;
    private const double MaximumGamma = 3.0;

    /// <summary>
    /// The scene's light in stops on this camera's scale: each +1 is twice the light. Null when
    /// the picture is black, which carries no measurement.
    /// </summary>
    public double? LightStops(double meanLinearY, int exposureLog2, int gain)
        => meanLinearY > 0 ? (Math.Log2(meanLinearY) / Gamma) - exposureLog2 - GainStops(gain) : null;

    /// <summary>Stops added by a gain setting, interpolated between measured points and held at the ends.</summary>
    public double GainStops(int gain)
    {
        if (GainCurve.Count == 0)
        {
            return 0;
        }

        if (gain <= GainCurve[0].Gain)
        {
            return GainCurve[0].Stops;
        }

        for (var i = 1; i < GainCurve.Count; i++)
        {
            var upper = GainCurve[i];
            if (gain <= upper.Gain)
            {
                var lower = GainCurve[i - 1];
                var fraction = (double)(gain - lower.Gain) / (upper.Gain - lower.Gain);
                return lower.Stops + ((upper.Stops - lower.Stops) * fraction);
            }
        }

        return GainCurve[^1].Stops;
    }

    /// <summary>
    /// Fits the response from an exposure sweep at fixed gain and a gain sweep at fixed exposure,
    /// both of an unchanging scene. Null when the sweeps don't give a usable fit.
    /// </summary>
    public static CameraResponse? Fit(
        IReadOnlyList<(int ExposureLog2, double MeanY)> exposureSweep,
        IReadOnlyList<(int Gain, double MeanY)> gainSweep)
    {
        ArgumentNullException.ThrowIfNull(exposureSweep);
        ArgumentNullException.ThrowIfNull(gainSweep);

        var usable = exposureSweep.Where(p => p.MeanY is >= DarkestUsable and <= BrightestUsable).ToList();
        if (usable.Count < MinimumExposurePoints)
        {
            return null;
        }

        // Least-squares slope of log2(brightness) against exposure stops.
        var meanX = usable.Average(p => (double)p.ExposureLog2);
        var meanLog = usable.Average(p => Math.Log2(p.MeanY));
        var covariance = usable.Sum(p => (p.ExposureLog2 - meanX) * (Math.Log2(p.MeanY) - meanLog));
        var variance = usable.Sum(p => (p.ExposureLog2 - meanX) * (p.ExposureLog2 - meanX));
        var gamma = covariance / variance;
        if (gamma is < MinimumGamma or > MaximumGamma || double.IsNaN(gamma))
        {
            return null;
        }

        var gains = gainSweep.Where(p => p.MeanY > 0).OrderBy(p => p.Gain).ToList();
        if (gains.Count < 2)
        {
            return null;
        }

        var baseLog = Math.Log2(gains[0].MeanY);
        var curve = new List<GainPoint>(gains.Count);
        var highest = 0.0;
        foreach (var (gain, meanY) in gains)
        {
            // More gain never means less light; noise can make a step look like a dip.
            highest = Math.Max(highest, (Math.Log2(meanY) - baseLog) / gamma);
            curve.Add(new GainPoint(gain, highest));
        }

        return new CameraResponse(gamma, curve);
    }
}
