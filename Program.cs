using System.Diagnostics;
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
const string DebugLogFile = "gemini_debug.log";

void LogDebug(string message)
{
    try
    {
        File.AppendAllText(DebugLogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
    }
    catch (IOException)
    {
    }
}

string DescribeResult(GeminiCallResult result) => result switch
{
    { Success: true } => "успех",
    { IsNetworkError: true } => "сетевая ошибка/таймаут",
    { IsDailyQuotaExceeded: true } => "дневной лимит исчерпан",
    { IsPerMinuteRateLimited: true } => "лимит запросов в минуту (rate limit)",
    _ => $"ошибка: {result.ErrorMessage}",
};

NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4));

var hotkeyThread = new Thread(RunScreenshotHotkeyMode) { IsBackground = true };
hotkeyThread.Start();

// Трей-поток держит процесс живым (он не фоновый) и пунктом "Выход" его завершает.
TrayIcon.NewTranslationRequested += () => NewTranslationWindow.Show(async (sentence, word, save) =>
{
    if (save)
    {
        await SaveTranslation(word, sentence);
    }
    else
    {
        await ShowTranslation(word, sentence);
    }
});

TrayIcon.ShowDictionaryRequested += () => DictionaryWindow.Show(LoadEntries());
TrayIcon.ChooseModelRequested += () => ModelWindow.Show();

TrayIcon.Start();

return;

string BuildLightPrompt(string word, string context) => $"""
Слово: "{word}"
Контекст: "{context}"

Переведи слово на русский с учётом контекста, коротко поясни и переведи весь контекст. Ответь строго в этом формате, без вступлений, каждое поле с новой строки:
Перевод: <перевод слова>
Часть речи: <часть речи>
Пояснение: <краткое пояснение значения в этом контексте>
Перевод контекста: <перевод контекста на русский>
""";

string BuildFullPrompt(string word, string context) => $"""
Слово: "{word}"
Контекст: "{context}"

Ответь строго в этом формате, без вступлений и лишних слов, каждое поле с новой строки:
Перевод: <перевод слова с учётом контекста>
Часть речи: <часть речи>
Пояснение: <краткое пояснение значения в этом контексте>
Перевод контекста: <перевод контекста на русский>
Перевод карточки: <перевод слова на русский БЕЗ самого английского слова, максимально коротко>
Подсказка: <короткое уточнение значения по-русски для отличия от других значений слова, БЕЗ английского слова, например "(событие, видеться)" или "(быть парой)">
Транскрипция: <международная фонетическая транскрипция слова в квадратных скобках, например [ˈmiːtɪŋ]>
""";

async Task<string?> GetLightTranslationReply(string word, string context) =>
    await RequestGeminiReply(BuildLightPrompt(word, context), "light");

async Task<string?> GetFullTranslationReply(string word, string context) =>
    await RequestGeminiReply(BuildFullPrompt(word, context), "full");

async Task<string?> RequestGeminiReply(string prompt, string requestLabel)
{
    var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        ShowWarning(
            "Не найден ключ GEMINI_API_KEY.\n\n" +
            "Задай его командой в PowerShell:\n" +
            "  setx GEMINI_API_KEY \"твой_ключ\"\n\n" +
            "После этого перезапусти программу.");
        return null;
    }

    var totalStopwatch = Stopwatch.StartNew();
    LogDebug($"--- запрос '{requestLabel}' начат ---");

    var attemptedCount = 0;
    var networkErrorCount = 0;

    foreach (var model in ModelManager.GetTryOrder())
    {
        if (ModelManager.IsExhaustedToday(model.Id))
        {
            LogDebug($"{model.DisplayName}: пропущена, дневной лимит уже исчерпан");
            continue;
        }

        attemptedCount++;

        var attemptStopwatch = Stopwatch.StartNew();
        var result = await CallGeminiModel(model.Id, apiKey, prompt);
        attemptStopwatch.Stop();
        LogDebug($"{model.DisplayName}: попытка заняла {attemptStopwatch.ElapsedMilliseconds}мс, результат: {DescribeResult(result)}");

        if (result.IsPerMinuteRateLimited)
        {
            Console.WriteLine($"Модель {model.DisplayName} отвечает слишком часто, жду 2 секунды и пробую снова...");
            await Task.Delay(2000);

            attemptStopwatch.Restart();
            result = await CallGeminiModel(model.Id, apiKey, prompt);
            attemptStopwatch.Stop();
            LogDebug($"{model.DisplayName}: повтор после паузы 2с занял {attemptStopwatch.ElapsedMilliseconds}мс, результат: {DescribeResult(result)}");
        }

        if (result.Success)
        {
            Console.WriteLine($"Модель: {model.DisplayName}");
            LogDebug($"--- запрос '{requestLabel}' успешно завершён моделью {model.DisplayName}, всего {totalStopwatch.ElapsedMilliseconds}мс ---");
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

        if (result.IsNetworkError)
        {
            networkErrorCount++;
            Console.WriteLine($"Модель {model.DisplayName}: сетевая ошибка/таймаут, быстро пробую следующую модель...");
            continue;
        }

        LogDebug($"--- запрос '{requestLabel}' прерван ошибкой, всего {totalStopwatch.ElapsedMilliseconds}мс ---");
        ShowWarning(result.ErrorMessage ?? "Неизвестная ошибка при обращении к Gemini API.");
        return null;
    }

    LogDebug($"--- запрос '{requestLabel}' — все модели исчерпаны/недоступны, всего {totalStopwatch.ElapsedMilliseconds}мс ---");

    if (attemptedCount > 0 && networkErrorCount == attemptedCount)
    {
        ShowWarning("Нет связи с сервисом перевода, проверь интернет и попробуй ещё раз.");
    }
    else
    {
        ShowWarning("Дневной лимит бесплатных запросов исчерпан на сегодня.");
    }

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

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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
    catch (TaskCanceledException)
    {
        return new GeminiCallResult(false, null, false, false, "Таймаут запроса к Gemini API (нет ответа за 15 секунд).", IsNetworkError: true);
    }
    catch (HttpRequestException ex)
    {
        return new GeminiCallResult(false, null, false, false, $"Не удалось связаться с Gemini API: {ex.Message}", IsNetworkError: true);
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

(string CardTranslation, string Hint, string Transcription) ParseCardFields(string reply)
{
    string cardTranslation = "", hint = "", transcription = "";

    foreach (var line in reply.Split('\n'))
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("Перевод карточки:", StringComparison.OrdinalIgnoreCase))
            cardTranslation = trimmed["Перевод карточки:".Length..].Trim();
        else if (trimmed.StartsWith("Подсказка:", StringComparison.OrdinalIgnoreCase))
            hint = trimmed["Подсказка:".Length..].Trim();
        else if (trimmed.StartsWith("Транскрипция:", StringComparison.OrdinalIgnoreCase))
            transcription = trimmed["Транскрипция:".Length..].Trim();
    }

    return (cardTranslation, hint, transcription);
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

void ShowWarning(string message)
{
    const uint mbIconWarning = 0x30;
    NativeMethods.MessageBox(IntPtr.Zero, message, "Экранный переводчик", mbIconWarning);
}

void RunScreenshotHotkeyMode()
{
    const int hotkeyIdShow = 1;
    const int hotkeyIdSave = 2;
    const int hotkeyIdOverlay = 3;
    const int hotkeyIdCloseOverlay = 4;
    const uint modAlt = 0x0001;
    const uint vkQ = 0x51;
    const uint vkE = 0x45;
    const uint vkW = 0x57;
    const uint vkEscape = 0x1B;

    EscCloseCoordinator.Init(NativeMethods.GetCurrentThreadId(), hotkeyIdCloseOverlay, vkEscape);

    var showRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, hotkeyIdShow, modAlt, vkQ);
    if (!showRegistered)
    {
        var errorCode = Marshal.GetLastWin32Error();
        ShowWarning($"Не удалось зарегистрировать горячую клавишу Alt+Q (код ошибки {errorCode}). Похоже, она уже занята другой программой. Попробуйте закрыть программу, которая могла её перехватить, либо сообщите мне — подберём другую комбинацию.");
    }

    var saveRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, hotkeyIdSave, modAlt, vkE);
    if (!saveRegistered)
    {
        var errorCode = Marshal.GetLastWin32Error();
        ShowWarning($"Не удалось зарегистрировать горячую клавишу Alt+E (код ошибки {errorCode}). Похоже, она уже занята другой программой. Попробуйте закрыть программу, которая могла её перехватить, либо сообщите мне — подберём другую комбинацию.");
    }

    var overlayRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, hotkeyIdOverlay, modAlt, vkW);
    if (!overlayRegistered)
    {
        var errorCode = Marshal.GetLastWin32Error();
        ShowWarning($"Не удалось зарегистрировать горячую клавишу Alt+W (код ошибки {errorCode}). Похоже, она уже занята другой программой. Попробуйте закрыть программу, которая могла её перехватить, либо сообщите мне — подберём другую комбинацию.");
    }

    if (!showRegistered && !saveRegistered && !overlayRegistered)
    {
        ShowWarning("Скриншоты по горячим клавишам работать не будут, но остальные функции доступны.");
        return;
    }

    try
    {
        const uint wmHotkey = 0x0312;

        while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (EscCloseCoordinator.IsWatcherRequestMessage(msg.message))
            {
                EscCloseCoordinator.HandleWatcherRequestMessage(msg.wParam);
                continue;
            }

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
                TranslationPopup.CloseCurrent();
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

        EscCloseCoordinator.Cleanup();
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

    var (word, context, screenRect) = found.Value;
    await ShowTranslation(word, context, screenRect);
}

async Task SaveTranslationUnderCursor(string path, int originX, int originY, int cursorScreenX, int cursorScreenY)
{
    var found = await FindWordAndContextUnderCursor(path, originX, originY, cursorScreenX, cursorScreenY);
    if (found is null)
    {
        return;
    }

    var (word, context, screenRect) = found.Value;
    await SaveTranslation(word, context, screenRect);
}

async Task OnOverlayWordLeftClick(string word, string context, Windows.Foundation.Rect screenRect)
{
    await ShowTranslation(word, context, screenRect);
}

async Task OnOverlayWordRightClick(string word, string context, Windows.Foundation.Rect screenRect)
{
    await SaveTranslation(word, context, screenRect);
}

async Task ShowTranslation(string word, string context, Windows.Foundation.Rect? screenRect = null)
{
    var replyText = await GetLightTranslationReply(word, context);
    if (replyText is null)
    {
        return;
    }

    var parsed = ParseReply(replyText);
    TranslationPopup.Show(word, parsed.Translation, parsed.PartOfSpeech, parsed.Explanation, parsed.ContextTranslation, screenRect);
}

async Task SaveTranslation(string word, string context, Windows.Foundation.Rect? screenRect = null)
{
    var replyText = await GetFullTranslationReply(word, context);
    if (replyText is null)
    {
        return;
    }

    var display = ParseReply(replyText);
    TranslationPopup.Show(word, display.Translation, display.PartOfSpeech, display.Explanation, display.ContextTranslation, screenRect);

    var card = ParseCardFields(replyText);
    SaveEntry(new DictionaryEntry(
        Word: word,
        Translation: card.CardTranslation,
        PartOfSpeech: display.PartOfSpeech,
        Explanation: "",
        Sentence: context,
        ContextTranslation: display.ContextTranslation,
        Hint: card.Hint,
        Transcription: card.Transcription));
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

async Task<(string Word, string Context, Windows.Foundation.Rect ScreenRect)?> FindWordAndContextUnderCursor(string path, int originX, int originY, int cursorScreenX, int cursorScreenY)
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

            var screenRect = new Windows.Foundation.Rect(rect.X + originX, rect.Y + originY, rect.Width, rect.Height);
            return (word.Text, BuildWideContext(lines, lineIndex), screenRect);
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
    ShowWarning(
        "Windows OCR для английского языка недоступен на этом компьютере.\n\n" +
        "Как включить:\n" +
        "1. Параметры Windows -> Время и язык -> Язык и регион\n" +
        "2. Добавить язык -> выбери English\n" +
        "3. При установке отметь распознавание текста (OCR), если это предложено\n" +
        "4. Дождись установки и перезапусти программу");
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

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);
}

record DictionaryEntry(
    string Word,
    string Translation,
    string PartOfSpeech,
    string Explanation,
    string Sentence,
    string ContextTranslation = "",
    string Hint = "",
    string Transcription = "");

record GeminiCallResult(bool Success, string? Text, bool IsDailyQuotaExceeded, bool IsPerMinuteRateLimited, string? ErrorMessage, bool IsNetworkError = false);
