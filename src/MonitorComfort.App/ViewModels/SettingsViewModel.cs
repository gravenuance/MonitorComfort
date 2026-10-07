using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorComfort.App.Services;
using MonitorComfort.Core.Planning;
using MonitorComfort.Core.Settings;
using MonitorComfort.Core.Solar;

namespace MonitorComfort.App.ViewModels;

/// <summary>
/// The settings window's state. Every edit is pushed to <see cref="SettingsService"/> at once,
/// so the monitors preview the change; the service coalesces the file write.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PauseLength = TimeSpan.FromHours(1);

    private readonly SettingsService _settings;
    private readonly ComfortController _controller;
    private readonly Autostart _autostart;
    private readonly TimeProvider _time;
    private bool _loading;

    internal SettingsViewModel(SettingsService settings, ComfortController controller, Autostart autostart, TimeProvider time)
    {
        _settings = settings;
        _controller = controller;
        _autostart = autostart;
        _time = time;

        var current = settings.Current;
        Day = new ProfileViewModel("Day", current.Day);
        Evening = new ProfileViewModel("Evening", current.Evening);
        Night = new ProfileViewModel("Night", current.Night);
        foreach (var profile in Profiles)
        {
            profile.PropertyChanged += OnChildChanged;
        }

        StatusText = "Starting…";
        Load(current);
        ApplyStatus(controller.Status);
        _controller.StatusChanged += OnStatusChanged;
    }

    public ProfileViewModel Day { get; }

    public ProfileViewModel Evening { get; }

    public ProfileViewModel Night { get; }

    public IReadOnlyList<ProfileViewModel> Profiles => [Day, Evening, Night];

    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial bool NeedsAttention { get; set; }

    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial decimal? Latitude { get; set; }

    [ObservableProperty]
    public partial decimal? Longitude { get; set; }

    [ObservableProperty]
    public partial string? LocationMessage { get; set; }

    [ObservableProperty]
    public partial string SunTimes { get; set; } = "";

    [ObservableProperty]
    public partial TimeSpan? NightStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? NightEnd { get; set; }

    [ObservableProperty]
    public partial bool RoomLightEnabled { get; set; }

    [ObservableProperty]
    public partial decimal? LightsOnOffset { get; set; }

    /// <summary>Index into "At sunrise", "At a set time".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LightsOffAtTime))]
    public partial int LightsOffIndex { get; set; }

    public bool LightsOffAtTime => LightsOffIndex == (int)LightsOff.AtTime;

    [ObservableProperty]
    public partial TimeSpan? LightsOffTime { get; set; }

    /// <summary>Index into "Warm", "Neutral", "Daylight".</summary>
    [ObservableProperty]
    public partial int LightColourIndex { get; set; }

    /// <summary>Index into "Dim", "Normal", "Bright".</summary>
    [ObservableProperty]
    public partial int LightLevelIndex { get; set; }

    [ObservableProperty]
    public partial bool DimOtherMonitors { get; set; }

    [ObservableProperty]
    public partial double OtherMonitorPercent { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    public void Dispose()
    {
        _controller.StatusChanged -= OnStatusChanged;
        foreach (var profile in Profiles)
        {
            profile.PropertyChanged -= OnChildChanged;
        }

        foreach (var monitor in Monitors)
        {
            monitor.PropertyChanged -= OnChildChanged;
        }
    }

    [RelayCommand]
    private async Task UseMyLocationAsync(CancellationToken cancellationToken)
    {
        LocationMessage = "Finding your location…";
        var result = await WindowsLocation.GetAsync(cancellationToken);
        if (result.Location is { } location)
        {
            Latitude = (decimal)location.Latitude;
            Longitude = (decimal)location.Longitude;
            LocationMessage = null;
        }
        else
        {
            LocationMessage = result.Error;
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused)
        {
            _controller.Resume();
        }
        else
        {
            _controller.PauseUntil(_time.GetUtcNow() + PauseLength);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is nameof(StatusText) or nameof(NeedsAttention) or nameof(IsPaused)
            or nameof(LocationMessage) or nameof(SunTimes) or nameof(LightsOffAtTime))
        {
            return;
        }

        if (e.PropertyName is nameof(Latitude) or nameof(Longitude))
        {
            UpdateSunTimes();
        }

        if (e.PropertyName is nameof(StartWithWindows))
        {
            _autostart.Apply(StartWithWindows);
        }

        Push();
    }

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_loading)
        {
            Push();
        }
    }

    private void Load(ComfortSettings settings)
    {
        _loading = true;
        try
        {
            Latitude = settings.Location is { } location ? (decimal)location.Latitude : null;
            Longitude = settings.Location is { } loc ? (decimal)loc.Longitude : null;
            NightStart = settings.NightStart.ToTimeSpan();
            NightEnd = settings.NightEnd.ToTimeSpan();
            RoomLightEnabled = settings.RoomLight.Enabled;
            LightsOnOffset = settings.RoomLight.OnMinutesAfterSunset;
            LightsOffIndex = (int)settings.RoomLight.OffAt;
            LightsOffTime = settings.RoomLight.OffTime.ToTimeSpan();
            LightColourIndex = (int)settings.RoomLight.Colour;
            LightLevelIndex = (int)settings.RoomLight.Level;
            DimOtherMonitors = settings.Fullscreen.DimOtherMonitors;
            OtherMonitorPercent = settings.Fullscreen.OtherMonitorBrightnessPercent;
            StartWithWindows = settings.StartWithWindows;
        }
        finally
        {
            _loading = false;
        }

        UpdateSunTimes();
    }

    private void Push()
    {
        var location = Latitude is { } lat && Longitude is { } lon && GeoLocation.IsValid((double)lat, (double)lon)
            ? new GeoLocation((double)lat, (double)lon)
            : (GeoLocation?)null;

        _settings.Update(current =>
        {
            var monitors = Monitors.Aggregate(current, (s, m) => s.WithMonitor(m.Id, m.ApplyTo(s.ForMonitor(m.Id))));
            return monitors with
            {
                Location = location ?? current.Location,
                NightStart = NightStart is { } start ? TimeOnly.FromTimeSpan(start) : current.NightStart,
                NightEnd = NightEnd is { } end ? TimeOnly.FromTimeSpan(end) : current.NightEnd,
                Day = Day.ToProfile(),
                Evening = Evening.ToProfile(),
                Night = Night.ToProfile(),
                RoomLight = current.RoomLight with
                {
                    Enabled = RoomLightEnabled,
                    OnMinutesAfterSunset = (int)(LightsOnOffset ?? 0),
                    OffAt = (LightsOff)LightsOffIndex,
                    OffTime = LightsOffTime is { } off ? TimeOnly.FromTimeSpan(off) : current.RoomLight.OffTime,
                    Colour = (LightColour)LightColourIndex,
                    Level = (LightLevel)LightLevelIndex,
                },
                Fullscreen = new FullscreenSettings
                {
                    DimOtherMonitors = DimOtherMonitors,
                    OtherMonitorBrightnessPercent = (int)Math.Round(OtherMonitorPercent),
                },
                StartWithWindows = StartWithWindows,
            };
        });
    }

    private void UpdateSunTimes()
    {
        if (Latitude is not { } lat || Longitude is not { } lon || !GeoLocation.IsValid((double)lat, (double)lon))
        {
            SunTimes = "";
            return;
        }

        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var events = SolarCalculator.Events(today, new GeoLocation((double)lat, (double)lon));
        SunTimes = events.Kind switch
        {
            DayKind.Normal => string.Create(
                CultureInfo.CurrentCulture,
                $"Sunrise {TimeZoneInfo.ConvertTime(events.Sunrise!.Value, TimeZoneInfo.Local):t} · Sunset {TimeZoneInfo.ConvertTime(events.Sunset!.Value, TimeZoneInfo.Local):t}"),
            DayKind.PolarDay => "The sun stays up today",
            DayKind.PolarNight => "The sun stays down today",
            _ => throw new InvalidOperationException($"Unhandled day kind {events.Kind}"),
        };
    }

    private void OnStatusChanged(object? sender, ComfortStatus status) => Dispatcher.UIThread.Post(() => ApplyStatus(status));

    private void ApplyStatus(ComfortStatus status)
    {
        StatusText = Describe(status);
        IsPaused = status.State == ControllerState.Paused;
        NeedsAttention = status.State is ControllerState.NeedsLocation or ControllerState.NoMonitors;
        SyncMonitors(status.Monitors);
    }

    internal static string Describe(ComfortStatus status) => status.State switch
    {
        ControllerState.Starting => "Starting…",
        ControllerState.NeedsLocation => "Set your location to start",
        ControllerState.NoMonitors => "No adjustable monitors found",
        ControllerState.Paused when status.PausedUntil is { } until && until != DateTimeOffset.MaxValue =>
            string.Create(CultureInfo.CurrentCulture, $"Paused until {TimeZoneInfo.ConvertTime(until, TimeZoneInfo.Local):t}"),
        ControllerState.Paused => "Paused",
        ControllerState.Active when status.Plan is { } plan => string.Create(
            CultureInfo.CurrentCulture,
            $"{PhaseName(plan.Phase)} · {plan.Brightness}% · {plan.Kelvin}K{(plan.RoomLit ? " · room lit" : "")}"),
        ControllerState.Active => "Active",
        _ => throw new InvalidOperationException($"Unhandled state {status.State}"),
    };

    private static string PhaseName(Phase phase) => phase switch
    {
        Phase.Day => "Day",
        Phase.Evening => "Evening",
        Phase.Night => "Night",
        _ => throw new InvalidOperationException($"Unhandled phase {phase}"),
    };

    // Updates the list in place so the view keeps its existing rows.
    private void SyncMonitors(IReadOnlyList<DetectedMonitor> detected)
    {
        for (var i = Monitors.Count - 1; i >= 0; i--)
        {
            if (!detected.Any(d => string.Equals(d.Id, Monitors[i].Id, StringComparison.OrdinalIgnoreCase)))
            {
                Monitors[i].PropertyChanged -= OnChildChanged;
                Monitors.RemoveAt(i);
            }
        }

        foreach (var monitor in detected)
        {
            var existing = Monitors.FirstOrDefault(m => string.Equals(m.Id, monitor.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Name = monitor.Name;
                continue;
            }

            var row = new MonitorViewModel(monitor.Id, monitor.Name, _settings.Current.ForMonitor(monitor.Id));
            row.PropertyChanged += OnChildChanged;
            Monitors.Add(row);
        }
    }
}
