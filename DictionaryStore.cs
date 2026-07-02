using System.IO;
using System.Text.Json;

static class DictionaryStore
{
    private static readonly string DictionaryFile = AppPaths.Resolve("dictionary.json");

    public static List<DictionaryEntry> Load()
    {
        if (!File.Exists(DictionaryFile))
        {
            return new List<DictionaryEntry>();
        }

        var json = File.ReadAllText(DictionaryFile);
        return JsonSerializer.Deserialize<List<DictionaryEntry>>(json) ?? new List<DictionaryEntry>();
    }

    public static void Save(List<DictionaryEntry> entries)
    {
        File.WriteAllText(DictionaryFile, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }
}
