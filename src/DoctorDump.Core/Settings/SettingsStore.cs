using System.Text.Json;
using DoctorDump.Core.Json;
using DoctorDump.Core.Models;

namespace DoctorDump.Core.Settings;

public static class SettingsStore
{
    public static string SettingsPath => Path.Combine(DumpDoctorPaths.ConfigRoot, "settings.json");

    public static async Task<DumpSettings> LoadAsync()
    {
        if (!File.Exists(SettingsPath))
        {
            return new DumpSettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<DumpSettings>(stream, JsonDefaults.Options)
                ?? new DumpSettings();
        }
        catch
        {
            return new DumpSettings();
        }
    }

    public static async Task SaveAsync(DumpSettings settings)
    {
        Directory.CreateDirectory(DumpDoctorPaths.ConfigRoot);
        await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(settings, JsonDefaults.Options));
    }
}
