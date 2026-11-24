using System;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.TimeIsUp.Usage;

/// <summary>
/// Keeps the current day's usage in a JSON file, so restarts don't reset it.
/// </summary>
internal sealed class UsageStore
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    private readonly string _path;

    public UsageStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "usage.json");
    }

    public UsageDay? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        using var stream = File.OpenRead(_path);
        return JsonSerializer.Deserialize<UsageDay>(stream, _options);
    }

    public void Save(UsageDay day)
    {
        var temp = _path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, day, _options);
        }

        File.Move(temp, _path, true);
    }
}
