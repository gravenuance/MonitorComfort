using System.Collections.Frozen;

namespace MonitorComfort.Core.Context;

/// <summary>A screen rectangle in physical pixels; right and bottom are exclusive.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);

public static class FullscreenRule
{
    // The desktop and taskbar span a whole monitor but are not full-screen content.
    private static readonly FrozenSet<string> ShellWindowClasses = FrozenSet.Create(
        StringComparer.Ordinal, "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd");

    public static bool Covers(PixelRect window, PixelRect monitor, string windowClass)
        => !ShellWindowClasses.Contains(windowClass)
            && window.Left <= monitor.Left
            && window.Top <= monitor.Top
            && window.Right >= monitor.Right
            && window.Bottom >= monitor.Bottom;
}

/// <summary>
/// Reports a new value only once it has been observed unchanged for the settle time, so a
/// quick alt-tab doesn't swing a monitor's brightness back and forth.
/// </summary>
public sealed class SettledValue<T>
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _settle;
    private T _candidate;
    private DateTimeOffset _candidateSince;

    public SettledValue(T initial, TimeSpan settle, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(settle, TimeSpan.Zero);
        _time = time;
        _settle = settle;
        Current = initial;
        _candidate = initial;
        _candidateSince = time.GetUtcNow();
    }

    public T Current { get; private set; }

    /// <summary>Records an observation; returns true when <see cref="Current"/> changed.</summary>
    public bool Observe(T value)
    {
        var now = _time.GetUtcNow();
        if (!EqualityComparer<T>.Default.Equals(value, _candidate))
        {
            _candidate = value;
            _candidateSince = now;
        }

        if (EqualityComparer<T>.Default.Equals(_candidate, Current) || now - _candidateSince < _settle)
        {
            return false;
        }

        Current = _candidate;
        return true;
    }
}
