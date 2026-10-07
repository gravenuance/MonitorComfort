using System.Text.Json.Serialization;

namespace MonitorComfort.Core.Settings;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ComfortSettings))]
[JsonSerializable(typeof(OriginalMonitorValues))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
