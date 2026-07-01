using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

const string DictionaryFile = "dictionary.json";

NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4));

var hotkeyThread = new Thread(RunScreenshotHotkeyMode) { IsBackground = true };
hotkeyThread.Start();

Console.WriteLine("Ctrl+Alt+Q — показать перевод слова под курсором.");
Console.WriteLine("Ctrl+Alt+E — перевести и сразу сохранить слово под курсором в словарь.");
Console.WriteLine("Ctrl+Alt+W — показать рамки вокруг всех слов на экране (Esc — закрыть).");

while (true)
{
    Console.WriteLine();
    Console.WriteLine("Команды:");
    Console.WriteLine("  1 - Новый перевод");
    Console.WriteLine("  2 - Посмотреть словарь");
    Console.WriteLine("  3 - Выход");
    Console.WriteLine("  4 - Выбрать модель / статистика запросов");
    Console.Write("Введите команду: ");
    var choice = Console.ReadLine();

    if (choice == "1")
    {
        await TranslateAndMaybeSave();
    }
    else if (choice == "2")
    {
        ShowDictionary();
    }
    else if (choice == "3")
    {
        break;
    }
    else if (choice == "4")
    {
        ChooseModel();
    }
    else
    {
        Console.WriteLine("Не понял команду. Введите 1, 2, 3 или 4.");
    }
}

return;

async Task TranslateAndMaybeSave()
{
    Console.Write("Введи предложение на английском: ");
    var sentence = Console.ReadLine();

    Console.Write("Введи слово из этого предложения: ");
    var word = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(sentence) || string.IsNullOrWhiteSpace(word))
    {
        Console.WriteLine("Предложение и слово не должны быть пустыми.");
        return;
    }

    var replyText = await GetTranslationReply(word, sentence);
    if (replyText is null)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine(replyText);

    Console.Write("Сохранить это слово в словарь? (д/н): ");
    var save = Console.ReadLine();

    if (save is not ("д" or "Д" or "y" or "Y"))
    {
        return;
    }

    var (translation, partOfSpeech, explanation, contextTranslation) = ParseReply(replyText);
    SaveEntry(new DictionaryEntry(word, translation, partOfSpeech, explanation, sentence, contextTranslation));
    Console.WriteLine("Сохранено.");
}

async Task<string?> GetTranslationReply(string word, string context)
{
    var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.WriteLine("Не найден ключ GEMINI_API_KEY.");
        Console.WriteLine("Задай его командой в PowerShell:");
        Console.WriteLine("  setx GEMINI_API_KEY \"твой_ключ\"");
        Console.WriteLine("После этого перезапусти терминал.");
        return null;
    }

    var prompt = $"""
    Переведи слово "{word}" на русский язык с учётом окружающего контекста: "{context}"
    Также переведи весь этот контекст целиком на русский язык.

    Ответь кратко в формате:
    Перевод: ...
    Часть речи: ...
    Пояснение: ...
    Перевод контекста: ...
    """;

    foreach (var model in ModelManager.GetTryOrder())
    {
        if (ModelManager.IsExhaustedToday(model.Id))
        {
            continue;
        }

        var result = await CallGeminiModel(model.Id, apiKey, prompt);

        if (result.IsPerMinuteRateLimited)
        {
            Console.WriteLine($"Модель {model.DisplayName} отвечает слишком часто, жду 2 секунды и пробую снова...");
            await Task.Delay(2000);
            result = await CallGeminiModel(model.Id, apiKey, prompt);
        }

        if (result.Success)
        {
            ModelManager.RecordUsage(model.Id);
            Console.WriteLine($"Модель: {model.DisplayName}");
            return result.Text;
        }

        if (result.IsDailyQuotaExceeded)
        {
            Console.WriteLine($"Модель {model.DisplayName} исчерпала дневной лимит запросов, переключаюсь на следующую...");
            ModelManager.MarkExhaustedToday(model.Id);
            continue;
        }

        if (result.IsPerMinuteRateLimited)
        {
            Console.WriteLine($"Модель {model.DisplayName} по-прежнему перегружена, пробую следующую модель...");
            continue;
        }

        Console.WriteLine(result.ErrorMessage);
        return null;
    }

    Console.WriteLine("Дневной лимит бесплатных запросов исчерпан на сегодня.");
    return null;
}

async Task<GeminiCallResult> CallGeminiModel(string modelId, string apiKey, string prompt)
{
    var requestBody = new
    {
        contents = new[]
        {
            new
            {
                parts = new[]
                {
                    new { text = prompt }
                }
            }
        }
    };

    var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";

    using var client = new HttpClient();
    using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

    string responseText;
    try
    {
        var response = await client.PostAsync(url, content);
        responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var message = ExtractErrorMessage(responseText);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var isPerMinute = message.Contains("minute", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("PerMinute", StringComparison.OrdinalIgnoreCase);

                return new GeminiCallResult(false, null, !isPerMinute, isPerMinute, message);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new GeminiCallResult(false, null, false, false,
                    $"Модель {modelId} не найдена ({message}). Список моделей: https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}");
            }

            return new GeminiCallResult(false, null, false, false, $"Ошибка от Gemini API ({(int)response.StatusCode}): {message}");
        }
    }
    catch (HttpRequestException ex)
    {
        return new GeminiCallResult(false, null, false, false, $"Не удалось связаться с Gemini API: {ex.Message}");
    }

    using var doc = JsonDocument.Parse(responseText);
    var text = doc.RootElement
        .GetProperty("candidates")[0]
        .GetProperty("content")
        .GetProperty("parts")[0]
        .GetProperty("text")
        .GetString() ?? "";

    return new GeminiCallResult(true, text, false, false, null);
}

string ExtractErrorMessage(string responseText)
{
    try
    {
        using var doc = JsonDocument.Parse(responseText);
        if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message))
        {
            return message.GetString() ?? responseText;
        }
    }
    catch (JsonException)
    {
    }

    return responseText;
}

(string Translation, string PartOfSpeech, string Explanation, string ContextTranslation) ParseReply(string reply)
{
    string translation = "", partOfSpeech = "", explanation = "", contextTranslation = "";

    foreach (var line in reply.Split('\n'))
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("Перевод контекста:", StringComparison.OrdinalIgnoreCase))
            contextTranslation = trimmed["Перевод контекста:".Length..].Trim();
        else if (trimmed.StartsWith("Перевод:", StringComparison.OrdinalIgnoreCase))
            translation = trimmed["Перевод:".Length..].Trim();
        else if (trimmed.StartsWith("Часть речи:", StringComparison.OrdinalIgnoreCase))
            partOfSpeech = trimmed["Часть речи:".Length..].Trim();
        else if (trimmed.StartsWith("Пояснение:", StringComparison.OrdinalIgnoreCase))
            explanation = trimmed["Пояснение:".Length..].Trim();
    }

    if (translation == "" && partOfSpeech == "" && explanation == "" && contextTranslation == "")
    {
        translation = reply.Trim();
    }

    return (translation, partOfSpeech, explanation, contextTranslation);
}

void SaveEntry(DictionaryEntry entry)
{
    var entries = LoadEntries();
    entries.Add(entry);
    File.WriteAllText(DictionaryFile, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
}

List<DictionaryEntry> LoadEntries()
{
    if (!File.Exists(DictionaryFile))
    {
        return new List<DictionaryEntry>();
    }

    var json = File.ReadAllText(DictionaryFile);
    return JsonSerializer.Deserialize<List<DictionaryEntry>>(json) ?? new List<DictionaryEntry>();
}

void ShowDictionary()
{
    var entries = LoadEntries();

    if (entries.Count == 0)
    {
        Console.WriteLine("Словарь пока пуст.");
        return;
    }

    for (var i = 0; i < entries.Count; i++)
    {
        var e = entries[i];
        Console.WriteLine($"{i + 1}. {e.Word} — {e.Translation} ({e.PartOfSpeech})");
        Console.WriteLine($"   Пояснение: {e.Explanation}");
        Console.WriteLine($"   Предложение: {e.Sentence}");
        Console.WriteLine($"   Перевод предложения: {e.ContextTranslation}");
        Console.WriteLine();
    }
}

void ChooseModel()
{
    Console.WriteLine();
    Console.WriteLine("Модели (тир-лист, от лучшей к запасной):");
    ModelManager.PrintModelList();
    Console.Write("Введите номер модели, чтобы сделать её основной (Enter — оставить как есть): ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        return;
    }

    if (int.TryParse(input, out var number) && number >= 1 && number <= ModelManager.Models.Count)
    {
        ModelManager.SetHomeModel(number - 1);
        Console.WriteLine($"Основная модель теперь: {ModelManager.Models[number - 1].DisplayName}");
    }
    else
    {
        Console.WriteLine("Не понял номер модели.");
    }
}

void RunScreenshotHotkeyMode()
{
    const int hotkeyIdShow = 1;
    const int hotkeyIdSave = 2;
    const int hotkeyIdOverlay = 3;
    const int hotkeyIdCloseOverlay = 4;
    const uint modControl = 0x0002;
    const uint modAlt = 0x0001;
    const uint vkQ = 0x51;
    const uint vkE = 0x45;
    const uint vkW = 0x57;
    const uint vkEscape = 0x1B;

    var escOverlayHotkeyRegistered = false;

    var showRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, hotkeyIdShow, modControl | modAlt, vkQ);
    if (!showRegistered)
    {
        var errorCode = Marshal.GetLastWin32Error();
        Console.WriteLine($"Не удалось зарегистрировать горячую клавишу Ctrl+Alt+Q (код ошибки {errorCode}).");
    }

    var saveRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, hotkeyIdSave, modControl | modAlt, vkE);
    if (!saveRegistered)
    {
        var errorCode = Marshal.GetLastWin32Error();
        Console.WriteLine($"Не удалось зарегистрировать горячую клавишу Ctrl+Alt+E (код ошибки {errorCode}).");
    }

    var overlayRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, hotkeyIdOverlay, modControl | modAlt, vkW);
    if (!overlayRegistered)
    {
        var errorCode = Marshal.GetLastWin32Error();
        Console.WriteLine($"Не удалось зарегистрировать горячую клавишу Ctrl+Alt+W (код ошибки {errorCode}).");
    }

    if (!showRegistered && !saveRegistered && !overlayRegistered)
    {
        Console.WriteLine("Скриншоты по горячим клавишам работать не будут, но остальные функции доступны.");
        return;
    }

    try
    {
        const uint wmHotkey = 0x0312;

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message != wmHotkey)
            {
                continue;
            }

            var hotkeyId = (int)msg.wParam;
            if (hotkeyId != hotkeyIdShow && hotkeyId != hotkeyIdSave && hotkeyId != hotkeyIdOverlay && hotkeyId != hotkeyIdCloseOverlay)
            {
                continue;
            }

            if (hotkeyId == hotkeyIdCloseOverlay)
            {
                Overlay.CloseCurrent();

                if (escOverlayHotkeyRegistered)
                {
                    NativeMethods.UnregisterHotKey(IntPtr.Zero, hotkeyIdCloseOverlay);
                    escOverlayHotkeyRegistered = false;
                }

                continue;
            }

            if (hotkeyId == hotkeyIdOverlay)
            {
                var (overlayScreenshotPath, overlayOriginX, overlayOriginY) = TakeScreenshot();
                var words = RecognizeAllWords(overlayScreenshotPath).GetAwaiter().GetResult();

                if (words is null)
                {
                    continue;
                }

                if (words.Count == 0)
                {
                    Console.WriteLine("Слов на экране не найдено.");
                    continue;
                }

                Overlay.Show(words, overlayOriginX, overlayOriginY, OnOverlayWordLeftClick, OnOverlayWordRightClick);

                if (!escOverlayHotkeyRegistered)
                {
                    escOverlayHotkeyRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, hotkeyIdCloseOverlay, 0, vkEscape);
                }

                continue;
            }

            NativeMethods.GetCursorPos(out var cursorPos);
            var (screenshotPath, originX, originY) = TakeScreenshot();

            if (hotkeyId == hotkeyIdShow)
            {
                ShowTranslationUnderCursor(screenshotPath, originX, originY, cursorPos.X, cursorPos.Y).GetAwaiter().GetResult();
            }
            else
            {
                SaveTranslationUnderCursor(screenshotPath, originX, originY, cursorPos.X, cursorPos.Y).GetAwaiter().GetResult();
            }
        }
    }
    finally
    {
        if (showRegistered)
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, hotkeyIdShow);
        }

        if (saveRegistered)
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, hotkeyIdSave);
        }

        if (overlayRegistered)
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, hotkeyIdOverlay);
        }

        if (escOverlayHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, hotkeyIdCloseOverlay);
        }
    }
}

(string Path, int OriginX, int OriginY) TakeScreenshot()
{
    const int smXVirtualScreen = 76;
    const int smYVirtualScreen = 77;
    const int smCXVirtualScreen = 78;
    const int smCYVirtualScreen = 79;

    var x = NativeMethods.GetSystemMetrics(smXVirtualScreen);
    var y = NativeMethods.GetSystemMetrics(smYVirtualScreen);
    var width = NativeMethods.GetSystemMetrics(smCXVirtualScreen);
    var height = NativeMethods.GetSystemMetrics(smCYVirtualScreen);

    using var bitmap = new Bitmap(width, height);
    using var g = Graphics.FromImage(bitmap);
    g.CopyFromScreen(x, y, 0, 0, new Size(width, height));

    var screenshotsDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
    Directory.CreateDirectory(screenshotsDir);

    var fileName = $"screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
    var path = Path.Combine(screenshotsDir, fileName);
    bitmap.Save(path, ImageFormat.Png);

    Console.WriteLine($"Скриншот сохранён: {path}");

    return (path, x, y);
}

async Task ShowTranslationUnderCursor(string path, int originX, int originY, int cursorScreenX, int cursorScreenY)
{
    var found = await FindWordAndContextUnderCursor(path, originX, originY, cursorScreenX, cursorScreenY);
    if (found is null)
    {
        return;
    }

    var (word, context) = found.Value;
    await ShowTranslation(word, context);
}

async Task SaveTranslationUnderCursor(string path, int originX, int originY, int cursorScreenX, int cursorScreenY)
{
    var found = await FindWordAndContextUnderCursor(path, originX, originY, cursorScreenX, cursorScreenY);
    if (found is null)
    {
        return;
    }

    var (word, context) = found.Value;
    await SaveTranslation(word, context);
}

async Task OnOverlayWordLeftClick(string word, string context)
{
    await ShowTranslation(word, context);
}

async Task OnOverlayWordRightClick(string word, string context)
{
    await SaveTranslation(word, context);
}

async Task<string?> ShowTranslation(string word, string context)
{
    Console.WriteLine();
    Console.WriteLine($"Слово: {word}");
    Console.WriteLine($"Контекст: {context}");

    var replyText = await GetTranslationReply(word, context);
    if (replyText is null)
    {
        return null;
    }

    Console.WriteLine();
    Console.WriteLine(replyText);

    return replyText;
}

async Task SaveTranslation(string word, string context)
{
    var replyText = await ShowTranslation(word, context);
    if (replyText is null)
    {
        return;
    }

    var (translation, partOfSpeech, explanation, contextTranslation) = ParseReply(replyText);
    SaveEntry(new DictionaryEntry(word, translation, partOfSpeech, explanation, context, contextTranslation));
    Console.WriteLine("Сохранено.");
}

async Task<IReadOnlyList<OcrLine>?> RecognizeLines(string path)
{
    var language = new Language("en");

    if (!OcrEngine.IsLanguageSupported(language))
    {
        PrintOcrLanguageMissingMessage();
        return null;
    }

    var engine = OcrEngine.TryCreateFromLanguage(language);
    if (engine is null)
    {
        PrintOcrLanguageMissingMessage();
        return null;
    }

    var file = await StorageFile.GetFileFromPathAsync(path);
    using var stream = await file.OpenAsync(FileAccessMode.Read);
    var decoder = await BitmapDecoder.CreateAsync(stream);
    using var sourceBitmap = await decoder.GetSoftwareBitmapAsync();

    var ocrBitmap = sourceBitmap;
    if (sourceBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || sourceBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
    {
        ocrBitmap = SoftwareBitmap.Convert(sourceBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    var result = await engine.RecognizeAsync(ocrBitmap);

    if (!ReferenceEquals(ocrBitmap, sourceBitmap))
    {
        ocrBitmap.Dispose();
    }

    return result.Lines;
}

async Task<List<(string Text, string Context, Windows.Foundation.Rect Rect)>?> RecognizeAllWords(string path)
{
    var lines = await RecognizeLines(path);
    if (lines is null)
    {
        return null;
    }

    var words = new List<(string Text, string Context, Windows.Foundation.Rect Rect)>();
    for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
    {
        var context = BuildWideContext(lines, lineIndex);

        foreach (var word in lines[lineIndex].Words)
        {
            words.Add((word.Text, context, word.BoundingRect));
        }
    }

    return words;
}

async Task<(string Word, string Context)?> FindWordAndContextUnderCursor(string path, int originX, int originY, int cursorScreenX, int cursorScreenY)
{
    var lines = await RecognizeLines(path);
    if (lines is null)
    {
        return null;
    }

    var cursorImageX = cursorScreenX - originX;
    var cursorImageY = cursorScreenY - originY;

    for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
    {
        var line = lines[lineIndex];

        foreach (var word in line.Words)
        {
            var rect = word.BoundingRect;
            var insideWord = cursorImageX >= rect.X && cursorImageX <= rect.X + rect.Width
                && cursorImageY >= rect.Y && cursorImageY <= rect.Y + rect.Height;

            if (!insideWord)
            {
                continue;
            }

            return (word.Text, BuildWideContext(lines, lineIndex));
        }
    }

    Console.WriteLine();
    Console.WriteLine("Под курсором нет слова");
    return null;
}

string BuildWideContext(IReadOnlyList<OcrLine> lines, int lineIndex)
{
    var contextLines = new List<string>();
    if (lineIndex > 0)
    {
        contextLines.Add(lines[lineIndex - 1].Text);
    }

    contextLines.Add(lines[lineIndex].Text);

    if (lineIndex < lines.Count - 1)
    {
        contextLines.Add(lines[lineIndex + 1].Text);
    }

    return string.Join(" ", contextLines);
}

void PrintOcrLanguageMissingMessage()
{
    Console.WriteLine();
    Console.WriteLine("Windows OCR для английского языка недоступен на этом компьютере.");
    Console.WriteLine("Как включить:");
    Console.WriteLine("  1. Параметры Windows -> Время и язык -> Язык и регион");
    Console.WriteLine("  2. Добавить язык -> выбери English");
    Console.WriteLine("  3. При установке отметь распознавание текста (OCR), если это предложено");
    Console.WriteLine("  4. Дождись установки и перезапусти программу");
}

[StructLayout(LayoutKind.Sequential)]
struct NativePoint
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
struct NativeMessage
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public NativePoint pt;
}

static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}

record DictionaryEntry(string Word, string Translation, string PartOfSpeech, string Explanation, string Sentence, string ContextTranslation = "");

record GeminiCallResult(bool Success, string? Text, bool IsDailyQuotaExceeded, bool IsPerMinuteRateLimited, string? ErrorMessage);
