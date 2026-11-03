using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace MonitorComfort.Core.Settings;

/// <summary>
/// Per-monitor settings keyed case-insensitively and compared by content, so a settings record
/// rebuilt with the same values equals the original and the update is skipped.
/// </summary>
internal sealed class MonitorSettingsMap : IReadOnlyDictionary<string, MonitorSettings>, IEquatable<MonitorSettingsMap>
{
    public static readonly MonitorSettingsMap Empty = new(FrozenDictionary<string, MonitorSettings>.Empty);

    private readonly FrozenDictionary<string, MonitorSettings> _items;

    private MonitorSettingsMap(FrozenDictionary<string, MonitorSettings> items) => _items = items;

    public static MonitorSettingsMap From(IReadOnlyDictionary<string, MonitorSettings>? items) => items switch
    {
        null => Empty,
        MonitorSettingsMap map => map,
        { Count: 0 } => Empty,
        _ => new MonitorSettingsMap(items.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)),
    };

    public int Count => _items.Count;

    public IEnumerable<string> Keys => _items.Keys;

    public IEnumerable<MonitorSettings> Values => _items.Values;

    public MonitorSettings this[string key] => _items[key];

    public bool ContainsKey(string key) => _items.ContainsKey(key);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out MonitorSettings value) => _items.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, MonitorSettings>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, MonitorSettings>>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(MonitorSettingsMap? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || other.Count != Count)
        {
            return false;
        }

        foreach (var (id, settings) in _items)
        {
            if (!other.TryGetValue(id, out var theirs) || !settings.Equals(theirs))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as MonitorSettingsMap);

    // Order-independent, and case-insensitive on the id like the lookup.
    public override int GetHashCode()
    {
        var hash = Count;
        foreach (var (id, settings) in _items)
        {
            hash ^= HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(id), settings);
        }

        return hash;
    }
}
