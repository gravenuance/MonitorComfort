using CommunityToolkit.Mvvm.ComponentModel;
using MonitorComfort.Core.Monitors;
using MonitorComfort.Core.Settings;

namespace MonitorComfort.App.ViewModels;

/// <summary>Brightness and warmth for one part of the day.</summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    public ProfileViewModel(string name, DisplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Name = name;
        Load(profile);
    }

    public string Name { get; }

    [ObservableProperty]
    public partial double Brightness { get; set; }

    [ObservableProperty]
    public partial double Kelvin { get; set; }

    public DisplayProfile ToProfile() => new((int)Math.Round(Brightness), (int)Math.Round(Kelvin));

    public void Load(DisplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Brightness = profile.Brightness;
        Kelvin = profile.Kelvin;
    }
}

/// <summary>One detected monitor's on/off switch and brightness offset, and what it supports.</summary>
public sealed partial class MonitorViewModel : ObservableObject
{
    public MonitorViewModel(string id, string name, MonitorSupport support, MonitorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Id = id;
        Name = name;
        Support = support;
        Enabled = settings.Enabled;
        BrightnessOffset = settings.BrightnessOffset;
    }

    public string Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdjustable), nameof(CanChangeOffset), nameof(SupportText))]
    public partial MonitorSupport Support { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeOffset))]
    public partial bool Enabled { get; set; }

    public bool IsAdjustable => Support != MonitorSupport.None;

    public bool CanChangeOffset => IsAdjustable && Enabled;

    public string SupportText => Support switch
    {
        MonitorSupport.BrightnessAndColour => "Brightness and colour",
        MonitorSupport.BrightnessOnly => "Brightness only",
        MonitorSupport.None => "DDC/CI not answering; turn it on in the monitor's menu",
        MonitorSupport.Unknown => "Not checked yet",
        _ => throw new InvalidOperationException($"Unhandled support {Support}"),
    };

    [ObservableProperty]
    public partial double BrightnessOffset { get; set; }

    public MonitorSettings ApplyTo(MonitorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings with { Enabled = Enabled, BrightnessOffset = (int)Math.Round(BrightnessOffset) };
    }
}
