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
    private static readonly Dictionary<string, int> RequestCounts = new();

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

    public static void SetHomeModel(int index) => _homeIndex = index;

    public static bool IsExhaustedToday(string modelId) => ExhaustedToday.Contains(modelId);

    public static void MarkExhaustedToday(string modelId) => ExhaustedToday.Add(modelId);

    public static void RecordUsage(string modelId)
    {
        RequestCounts[modelId] = RequestCounts.GetValueOrDefault(modelId) + 1;
    }

    public static void PrintModelList()
    {
        for (var i = 0; i < Models.Count; i++)
        {
            var model = Models[i];
            var marker = i == _homeIndex ? " [ОСНОВНАЯ]" : "";
            var exhausted = ExhaustedToday.Contains(model.Id) ? " [лимит на сегодня исчерпан]" : "";
            var count = RequestCounts.GetValueOrDefault(model.Id);

            Console.WriteLine($"  {i + 1}. {model.DisplayName} ({model.Id}) — до {model.DailyLimit}/день — использовано в сессии: {count}{marker}{exhausted}");
        }
    }
}
