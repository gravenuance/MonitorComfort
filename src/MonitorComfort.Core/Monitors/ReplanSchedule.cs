namespace MonitorComfort.Core.Monitors;

/// <summary>
/// When to plan next: every minute normally, but in quick retries after the displays change,
/// because a monitor that was just connected or woken often ignores DDC/CI for several seconds,
/// or takes writes without applying them while still reporting the written values.
/// </summary>
public sealed class ReplanSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan MaxUnansweredDelay = TimeSpan.FromMinutes(30);

    // Plans land at the change and about 2, 5, 10, 20 and 40 seconds after it.
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
    ];

    private int _settlingPlansLeft;

    /// <summary>
    /// True for the plans of the retry window, whose readback can't be trusted,
    /// so every value is written again.
    /// </summary>
    public bool IsSettling => _settlingPlansLeft > 0;

    public void DisplaysChanged() => _settlingPlansLeft = RetryDelays.Length + 1;

    /// <summary>How long to leave a monitor that didn't answer before asking again, after this many failed checks.</summary>
    public static TimeSpan UnansweredDelay(int failedChecks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedChecks, 1);

        // The shift is capped well below overflow; the delay itself is capped at MaxUnansweredDelay.
        var delay = Interval * (1 << Math.Min(failedChecks - 1, 10));
        return delay < MaxUnansweredDelay ? delay : MaxUnansweredDelay;
    }

    /// <summary>The delay after a plan.</summary>
    public TimeSpan NextDelay()
    {
        if (_settlingPlansLeft == 0)
        {
            return Interval;
        }

        _settlingPlansLeft--;
        return _settlingPlansLeft == 0 ? Interval : RetryDelays[^_settlingPlansLeft];
    }
}
