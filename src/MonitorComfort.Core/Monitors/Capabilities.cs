using System.Collections.Frozen;
using System.Globalization;

namespace MonitorComfort.Core.Monitors;

/// <summary>The VCP codes a monitor reports in its MCCS capabilities string, with any listed values.</summary>
public sealed class Capabilities
{
    private readonly FrozenDictionary<int, FrozenSet<int>> _codes;

    private Capabilities(FrozenDictionary<int, FrozenSet<int>> codes) => _codes = codes;

    public static Capabilities None { get; } = new(FrozenDictionary<int, FrozenSet<int>>.Empty);

    public bool Supports(VcpCode code) => _codes.ContainsKey((int)code);

    public bool SupportsValue(VcpCode code, int value)
        => _codes.TryGetValue((int)code, out var values) && values.Contains(value);

    /// <summary>Parses the <c>vcp(…)</c> section, e.g. <c>vcp(10 12 14(05 06 0B) 16)</c>.</summary>
    public static Capabilities Parse(string? capabilities)
    {
        if (string.IsNullOrEmpty(capabilities))
        {
            return None;
        }

        var start = capabilities.IndexOf("vcp(", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return None;
        }

        var codes = new Dictionary<int, HashSet<int>>();
        var depth = 1;
        int? lastCode = null;
        var token = new System.Text.StringBuilder();

        for (var i = start + 4; i < capabilities.Length && depth > 0; i++)
        {
            var c = capabilities[i];
            if (Uri.IsHexDigit(c))
            {
                token.Append(c);
                continue;
            }

            if (token.Length > 0)
            {
                var value = int.Parse(token.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                token.Clear();
                if (depth == 1)
                {
                    lastCode = value;
                    codes.TryAdd(value, []);
                }
                else if (depth == 2 && lastCode is { } code)
                {
                    codes[code].Add(value);
                }
            }

            depth += c switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };
        }

        return new Capabilities(codes.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet()));
    }
}
