using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace MonitorComfort.Core.Settings;

/// <summary>The loaded value plus a short message when the file was unusable or had to be migrated.</summary>
public sealed record LoadResult<T>(T Value, string? Problem, bool Existed);

/// <summary>
/// A JSON file with a <c>schemaVersion</c> field: migrated as a raw JSON tree before binding,
/// backed up before migration, and written atomically.
/// </summary>
public sealed class VersionedJsonFile<T>
    where T : class
{
    private const string VersionField = "schemaVersion";

    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly int _currentVersion;
    private readonly Func<T> _createDefault;
    private readonly Func<JsonObject, int, JsonObject> _migrate;

    public VersionedJsonFile(
        string path,
        JsonTypeInfo<T> typeInfo,
        int currentVersion,
        Func<T> createDefault,
        Func<JsonObject, int, JsonObject>? migrate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(createDefault);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentVersion, 1);
        Path = path;
        _typeInfo = typeInfo;
        _currentVersion = currentVersion;
        _createDefault = createDefault;
        _migrate = migrate ?? ((node, _) => node);
    }

    public string Path { get; }

    public LoadResult<T> Load()
    {
        if (!File.Exists(Path))
        {
            return new LoadResult<T>(_createDefault(), null, Existed: false);
        }

        string text;
        try
        {
            text = File.ReadAllText(Path);
        }
        catch (IOException ex)
        {
            return new LoadResult<T>(_createDefault(), $"Could not read {Path}: {ex.Message}", Existed: true);
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;
        }
        catch (JsonException ex)
        {
            return Unusable($"is not valid JSON ({ex.Message})");
        }

        if (root is null)
        {
            return Unusable("is not a JSON object");
        }

        var version = root[VersionField]?.GetValueKind() == JsonValueKind.Number ? root[VersionField]!.GetValue<int>() : 0;
        if (version > _currentVersion)
        {
            return Unusable($"was written by a newer version (schema {version})");
        }

        string? problem = null;
        if (version < _currentVersion)
        {
            CopyAside($".v{version}.bak");
            root = _migrate(root, version);
            root[VersionField] = _currentVersion;
            problem = $"Upgraded {System.IO.Path.GetFileName(Path)} from schema {version}";
        }

        try
        {
            // Source-generated binding assigns every init-only property, so a field absent from
            // the file would become null or zero; laying the file over the defaults keeps them.
            var merged = JsonSerializer.SerializeToNode(_createDefault(), _typeInfo)!.AsObject();
            MergeInto(merged, root);
            var value = merged.Deserialize(_typeInfo);
            return value is null ? Unusable("is empty") : new LoadResult<T>(value, problem, Existed: true);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return Unusable($"has invalid values ({ex.Message})");
        }
    }

    /// <summary>Writes to a temporary file, then swaps it in, so a crash never leaves half a file.</summary>
    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, _typeInfo));
        File.Move(temp, Path, overwrite: true);
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (name, value) in source.ToList())
        {
            if (value is JsonObject sourceChild && target[name] is JsonObject targetChild)
            {
                MergeInto(targetChild, sourceChild);
            }
            else
            {
                target[name] = value?.DeepClone();
            }
        }
    }

    private LoadResult<T> Unusable(string reason)
    {
        CopyAside(".unreadable.bak");
        return new LoadResult<T>(
            _createDefault(),
            $"{System.IO.Path.GetFileName(Path)} {reason}; kept a copy and using defaults",
            Existed: true);
    }

    private void CopyAside(string suffix)
    {
        try
        {
            File.Copy(Path, Path + suffix, overwrite: true);
        }
        catch (IOException)
        {
            // The caller's result already reports the problem; a failed copy leaves the original untouched.
        }
    }
}
