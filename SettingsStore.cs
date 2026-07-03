using System.IO;
using System.Text.Json;

// OcrEngineCode: "windows" (по умолчанию) или "paddle" (PaddleOCR — точнее на игровых шрифтах, но медленнее).
record AppSettings(string? ApiKey = null, string? StudyLanguageCode = null, string? OcrEngineCode = null);

static class SettingsStore
{
    private static readonly string SettingsFile = AppPaths.Resolve("settings.json");

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
