record ModelInfo(string Id, string DisplayName, int DailyLimit);

static class ModelManager
{
    public static readonly List<ModelInfo> Models = new()
    {
        new ModelInfo("gemini-3.1-flash-lite", "Gemini 3.1 Flash Lite", 500),
        new ModelInfo("gemma-4-31b-it", "Gemma 4 31B", 1500),
        new ModelInfo("gemma-4-26b-a4b-it", "Gemma 4 26B", 1500),
        new ModelInfo("gemini-3.5-flash", "Gemini 3.5 Flash", 20),
        new ModelInfo("gemini-3-flash-preview", "Gemini 3 Flash Preview", 20),
        new ModelInfo("gemini-2.5-flash-lite", "Gemini 2.5 Flash-Lite", 20),
    };

    private static int _homeIndex;
    private static readonly HashSet<string> ExhaustedToday = new();

    public static IEnumerable<ModelInfo> GetTryOrder()
    {
        yield return Models[_homeIndex];

        for (var i = 0; i < Models.Count; i++)
        {
            if (i != _homeIndex)
            {
                yield return Models[i];
            }
        }
    }

    public static int HomeIndex => _homeIndex;

    public static void SetHomeModel(int index) => _homeIndex = index;

    public static bool IsExhaustedToday(string modelId) => ExhaustedToday.Contains(modelId);

    public static void MarkExhaustedToday(string modelId) => ExhaustedToday.Add(modelId);
}
