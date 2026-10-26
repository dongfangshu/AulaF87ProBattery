using System.Diagnostics;
using System.Text.Json;
using AulaBattery.Core;

namespace AulaBattery.Windows;

internal static class BatteryCache
{
    private static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AulaBattery", "last-reading.json");

    internal static BatteryDisplayState Load()
    {
        try
        {
            if (!File.Exists(CachePath))
                return new();
            var saved = JsonSerializer.Deserialize<BatteryDisplayState>(File.ReadAllText(CachePath));
            if (saved is not { Reading: { }, ReadAt: { } at } || at > DateTimeOffset.Now ||
                saved.Reading is not ({ ExternalPower: true } or { Percent: >= 1 and <= 100 }))
                return new();
            return saved with { IsCached = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning($"Cannot load battery cache: {ex.Message}");
            return new();
        }
    }

    internal static void Save(BatteryDisplayState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Cannot save battery cache: {ex.Message}");
        }
    }
}
