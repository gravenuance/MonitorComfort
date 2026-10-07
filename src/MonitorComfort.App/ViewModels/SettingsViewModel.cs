using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using MonitorComfort.App.Ambient;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonitorComfort.App.Services;
using MonitorComfort.Core.Ambient;
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
    private readonly AmbientLightService _ambient;
    private readonly TimeProvider _time;
    private bool _loading;
    private bool _previewing;

    internal SettingsViewModel(
        SettingsService settings, ComfortController controller, Autostart autostart, AmbientLightService ambient, TimeProvider time)
    {
        _settings = settings;
        _controller = controller;
        _autostart = autostart;
        _ambient = ambient;
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
        _ambient.ReadingChanged += OnReadingChanged;
        CameraStatus = DescribeCamera();
        CalibrationSummary = DescribeCalibration();
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

    [ObservableProperty]
    public partial bool CameraEnabled { get; set; }

    [ObservableProperty]
    public partial string CameraStatus { get; set; } = "";

    [ObservableProperty]
    public partial string CalibrationSummary { get; set; } = "";

    /// <summary>The brightness that feels right in the current light; previewed on the monitors while changed.</summary>
    [ObservableProperty]
    public partial double ComfortableBrightness { get; set; }

    /// <summary>Stops any calibration preview; called when the window hides.</summary>
    public void EndPreview()
    {
        if (_previewing)
        {
            _previewing = false;
            _controller.PreviewBrightness(null);
        }
    }

    public void Dispose()
    {
        EndPreview();
        _controller.StatusChanged -= OnStatusChanged;
        _ambient.ReadingChanged -= OnReadingChanged;
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
    private async Task CalibrateAsync(CalibrationLevel level, CancellationToken cancellationToken)
    {
        var brightness = (int)Math.Round(ComfortableBrightness);
        var progress = new Progress<string>(message => CameraStatus = message);
        var error = await _ambient.CalibrateAsync(level, brightness, progress, cancellationToken);
        CalibrationSummary = DescribeCalibration();
        EndPreview();
        _loading = true;
        try
        {
            CameraEnabled = _settings.Current.CameraLight.Enabled;
        }
        finally
        {
            _loading = false;
        }

        CameraStatus = error ?? DescribeCamera();
    }

    [RelayCommand]
    private async Task MeasureNowAsync(CancellationToken cancellationToken)
    {
        CameraStatus = "Measuring…";
        await _ambient.MeasureNowAsync(cancellationToken);
        CameraStatus = DescribeCamera();
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
            or nameof(LocationMessage) or nameof(SunTimes) or nameof(LightsOffAtTime) or nameof(CameraStatus)
            or nameof(CalibrationSummary))
        {
            return;
        }

        if (e.PropertyName is nameof(CameraEnabled))
        {
            _settings.Update(s => s with { CameraLight = s.CameraLight with { Enabled = CameraEnabled } });
            return;
        }

        if (e.PropertyName is nameof(ComfortableBrightness))
        {
            _previewing = true;
            _controller.PreviewBrightness((int)Math.Round(ComfortableBrightness));
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
        // Name and support come from the controller; saving on them would re-plan on every status.
        if (!_loading && e.PropertyName is nameof(MonitorViewModel.Enabled) or nameof(MonitorViewModel.BrightnessOffset))
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
            CameraEnabled = settings.CameraLight.Enabled;
            ComfortableBrightness = settings.CameraLight.Calibration?.Bright?.Brightness ?? settings.Day.Brightness;
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

    private void OnReadingChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!CalibrateCommand.IsRunning && !MeasureNowCommand.IsRunning)
        {
            CameraStatus = DescribeCamera();
        }
    });

    private string DescribeCalibration()
    {
        var calibration = _settings.Current.CameraLight.Calibration;
        return (calibration?.Bright, calibration?.Dim) switch
        {
            (null, null) => "Not calibrated yet",
            ({ } bright, null) => string.Create(CultureInfo.CurrentCulture, $"Bright room {bright.Brightness}% · dim room not set"),
            (null, { } dim) => string.Create(CultureInfo.CurrentCulture, $"Bright room not set · dim room {dim.Brightness}%"),
            ({ } bright, { } dim) => string.Create(CultureInfo.CurrentCulture, $"Bright room {bright.Brightness}% · dim room {dim.Brightness}%"),
        };
    }

    private string DescribeCamera()
    {
        var camera = _settings.Current.CameraLight;
        if (camera.Calibration is not { HasAnyPoint: true })
        {
            return "";
        }

        if (_ambient.LastError is { } error)
        {
            return error;
        }

        if (_ambient.Latest is not { } reading)
        {
            return camera.Enabled ? "First reading within a minute" : "";
        }

        var at = TimeZoneInfo.ConvertTime(reading.At, TimeZoneInfo.Local);
        return reading.Kelvin is { } kelvin
            ? string.Create(CultureInfo.CurrentCulture, $"Last reading {at:t} · {kelvin}K light")
            : string.Create(CultureInfo.CurrentCulture, $"Last reading {at:t}");
    }

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
            $"{PhaseName(plan.Phase)} · {plan.Brightness}% · {plan.Kelvin}K{SourceSuffix(plan.Source)}"),
        ControllerState.Active => "Active",
        _ => throw new InvalidOperationException($"Unhandled state {status.State}"),
    };

    private static string SourceSuffix(AmbientSource source) => source switch
    {
        AmbientSource.Schedule => "",
        AmbientSource.RoomModel => " · room lit",
        AmbientSource.Camera => " · measured",
        _ => throw new InvalidOperationException($"Unhandled source {source}"),
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
                existing.Support = monitor.Support;
                continue;
            }

            var row = new MonitorViewModel(monitor.Id, monitor.Name, monitor.Support, _settings.Current.ForMonitor(monitor.Id));
            row.PropertyChanged += OnChildChanged;
            Monitors.Add(row);
        }
    }
}
