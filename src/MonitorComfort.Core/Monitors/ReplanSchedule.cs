namespace MonitorComfort.Core.Monitors;

/// <summary>
/// When to plan next: every minute normally, but in quick retries after the displays change,
/// because a monitor that was just connected or woken often ignores DDC/CI for several seconds.
/// </summary>
public sealed class ReplanSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // Retries land about 2, 5, 10 and 20 seconds after the change.
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
    ];

    private int _retry = RetryDelays.Length;

    public void DisplaysChanged() => _retry = 0;

    /// <summary>The delay after a plan; retries end early once every monitor has answered.</summary>
    public TimeSpan NextDelay(bool allMonitorsAnswered)
    {
        if (allMonitorsAnswered || _retry >= RetryDelays.Length)
        {
            _retry = RetryDelays.Length;
            return Interval;
        }

        return RetryDelays[_retry++];
    }
}
