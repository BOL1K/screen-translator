// Изучаемый язык: что распознаёт OCR и с какого языка Gemini переводит на русский.
// RussianName подставляется в промпты, OcrTag — BCP-47-тег движка Windows OCR,
// NativeName показывается в инструкции по установке языкового пакета Windows.
record StudyLanguage(string Code, string DisplayName, string RussianName, string OcrTag, string NativeName);

static class StudyLanguages
{
    public static readonly List<StudyLanguage> All = new()
    {
        new("en", "Английский", "английский", "en", "English"),
        new("de", "Немецкий", "немецкий", "de", "Deutsch"),
        new("fr", "Французский", "французский", "fr", "Français"),
        new("es", "Испанский", "испанский", "es", "Español"),
        new("ja", "Японский", "японский", "ja", "日本語"),
        new("ko", "Корейский", "корейский", "ko", "한국어"),
        new("zh", "Китайский (упрощённый)", "китайский", "zh-Hans", "中文(简体)"),
    };

    // Неизвестный или пустой код (старый settings.json) — английский, как было до этой настройки.
    public static StudyLanguage FromCode(string? code) =>
        All.FirstOrDefault(l => l.Code == code) ?? All[0];

    public static StudyLanguage Current => FromCode(SettingsStore.Load().StudyLanguageCode);
}
