using Avalonia.Data.Converters;
using Avalonia.Media;
using MonitorComfort.Core.Colour;

namespace MonitorComfort.App.Views;

internal static class Converters
{
    /// <summary>The white a colour temperature gives, as a swatch beside its slider.</summary>
    public static readonly IValueConverter KelvinToBrush = new FuncValueConverter<double, IBrush>(kelvin =>
    {
        var (red, green, blue) = ColourTemperature.Multipliers((int)Math.Round(kelvin));
        return new SolidColorBrush(Color.FromRgb(ToByte(red), ToByte(green), ToByte(blue)));
    });

    private static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
}
