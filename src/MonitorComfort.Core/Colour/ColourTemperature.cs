namespace MonitorComfort.Core.Colour;

/// <summary>Red, green and blue gain values as the monitor's DDC/CI controls take them (0–100).</summary>
public readonly record struct RgbGains(int Red, int Green, int Blue);

/// <summary>Maps a white-point colour temperature to per-channel gain multipliers.</summary>
public static class ColourTemperature
{
    public const int Minimum = 2000;
    public const int Neutral = 6500;

    private const int TableStep = 500;

    // Blackbody white points in sRGB relative to 6500K, from Redshift's colorramp.c table
    // sampled every 500K. Index 0 is 2000K.
    private static readonly (double Red, double Green, double Blue)[] Table =
    [
        (1.0, 0.54360078, 0.08679949),
        (1.0, 0.64373109, 0.28819679),
        (1.0, 0.71976951, 0.42860152),
        (1.0, 0.77987699, 0.54642268),
        (1.0, 0.82854786, 0.64816570),
        (1.0, 0.86860704, 0.73688797),
        (1.0, 0.90198230, 0.81465502),
        (1.0, 0.93853986, 0.88130458),
        (1.0, 0.97107439, 0.94305985),
        (1.0, 1.0, 1.0),
    ];

    /// <summary>Clamps to the supported range: this app only warms the screen, never cools it.</summary>
    public static int Clamp(int kelvin) => Math.Clamp(kelvin, Minimum, Neutral);

    /// <summary>Scales each channel of a neutral gain setting to reach <paramref name="kelvin"/>.</summary>
    public static RgbGains Gains(int kelvin, RgbGains neutral)
    {
        var (red, green, blue) = Multipliers(kelvin);
        return new RgbGains(
            (int)Math.Round(neutral.Red * red),
            (int)Math.Round(neutral.Green * green),
            (int)Math.Round(neutral.Blue * blue));
    }

    public static (double Red, double Green, double Blue) Multipliers(int kelvin)
    {
        var position = (double)(Clamp(kelvin) - Minimum) / TableStep;
        var lower = (int)Math.Floor(position);
        if (lower >= Table.Length - 1)
        {
            return Table[^1];
        }

        var fraction = position - lower;
        var a = Table[lower];
        var b = Table[lower + 1];
        return (
            a.Red + ((b.Red - a.Red) * fraction),
            a.Green + ((b.Green - a.Green) * fraction),
            a.Blue + ((b.Blue - a.Blue) * fraction));
    }
}
