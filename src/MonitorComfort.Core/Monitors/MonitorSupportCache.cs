namespace MonitorComfort.Core.Monitors;

/// <summary>
/// What is known about each monitor so DDC/CI isn't asked again needlessly: a monitor that answered is
/// classified once per display layout, one that didn't is asked again at growing intervals, and whether
/// it is already in the user colour preset is remembered.
/// </summary>
public sealed class MonitorSupportCache
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last answer, which survives <see cref="TopologyChanged"/> so the settings window still has something to show.</summary>
    public MonitorSupport SupportOf(string monitorId)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        return _entries.TryGetValue(monitorId, out var entry) ? entry.Support : MonitorSupport.Unknown;
    }

    /// <summary>
    /// Whether to ask the monitor now. <paramref name="scheduled"/> is a periodic plan, as opposed to one
    /// caused by a settings change; <paramref name="settling"/> is the retry window after a display change.
    /// </summary>
    public bool NeedsCheck(string monitorId, DateTimeOffset now, bool scheduled, bool settling)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        if (!_entries.TryGetValue(monitorId, out var entry))
        {
            return true;
        }

        if (entry.Support != MonitorSupport.None)
        {
            return !entry.Verified;
        }

        return scheduled && (settling || now >= entry.NextCheck);
    }

    /// <summary>Stores an answer; returns true for the first answer and for a change.</summary>
    public bool Record(string monitorId, MonitorSupport support, DateTimeOffset now, bool settling)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        ArgumentOutOfRangeException.ThrowIfEqual(support, MonitorSupport.Unknown);
        var isNew = !_entries.TryGetValue(monitorId, out var entry);
        entry ??= new Entry();
        _entries[monitorId] = entry;
        var changed = isNew || entry.Support != support;
        entry.Support = support;
        if (support == MonitorSupport.None)
        {
            entry.Verified = false;
            entry.InUserPreset = false;

            // Failures in the retry window are expected while a monitor starts up, so they don't count.
            if (!settling)
            {
                entry.FailedChecks++;
                entry.NextCheck = now + ReplanSchedule.UnansweredDelay(entry.FailedChecks);
            }
        }
        else
        {
            entry.Verified = true;
            entry.FailedChecks = 0;
        }

        return changed;
    }

    /// <summary>A read or write failed: classify the monitor again and re-verify its colour preset.</summary>
    public void Invalidate(string monitorId)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        if (_entries.TryGetValue(monitorId, out var entry))
        {
            entry.Verified = false;
            entry.InUserPreset = false;
        }
    }

    /// <summary>The display layout changed: everything is asked again, and unanswered monitors start their backoff over.</summary>
    public void TopologyChanged()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Verified = false;
            entry.InUserPreset = false;
            entry.FailedChecks = 0;
            entry.NextCheck = DateTimeOffset.MinValue;
        }
    }

    public bool InUserPreset(string monitorId)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        return _entries.TryGetValue(monitorId, out var entry) && entry.InUserPreset;
    }

    public void SetInUserPreset(string monitorId, bool inUserPreset)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        if (_entries.TryGetValue(monitorId, out var entry))
        {
            entry.InUserPreset = inUserPreset;
        }
        else if (inUserPreset)
        {
            _entries[monitorId] = new Entry { InUserPreset = true };
        }
    }

    public void ForgetUserPresets()
    {
        foreach (var entry in _entries.Values)
        {
            entry.InUserPreset = false;
        }
    }

    private sealed class Entry
    {
        public MonitorSupport Support { get; set; } = MonitorSupport.Unknown;

        public bool Verified { get; set; }

        public bool InUserPreset { get; set; }

        public int FailedChecks { get; set; }

        public DateTimeOffset NextCheck { get; set; } = DateTimeOffset.MinValue;
    }
}
