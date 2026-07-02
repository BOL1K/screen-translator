using System.IO;
using System.Text.Json;

record AppSettings(string? ApiKey = null);

static class SettingsStore
{
    private const string SettingsFile = "settings.json";

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsFile))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(SettingsFile);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
