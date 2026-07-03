using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;

// Второй OCR-движок (PaddleOCR / PP-OCRv5) — заметно точнее Windows OCR на стилизованных
// и игровых шрифтах, но в разы медленнее на CPU. Поэтому он опциональный (настройки),
// а для перевода под курсором распознаётся только область вокруг курсора, не весь экран.
static class PaddleOcr
{
    // PaddleOcrAll не потокобезопасен, а создание движка стоит ~1 секунду —
    // держим один экземпляр под замком и пересоздаём только при смене языка.
    private static readonly object EngineLock = new();
    private static PaddleOcrAll? _engine;
    private static string? _engineLanguageCode;

    // Область вокруг курсора для Alt+Q / Alt+E: полный экран PaddleOCR на CPU
    // обрабатывает ~10 секунд, такой кроп — ~3 секунды, а предложение-контекст
    // в него всё равно помещается.
    private const int FocusCropWidth = 1100;
    private const int FocusCropHeight = 700;

    public static List<OcrLineResult> RecognizeLines(string imagePath, StudyLanguage language, (int X, int Y)? focusImagePoint)
    {
        lock (EngineLock)
        {
            if (_engine is null || _engineLanguageCode != language.Code)
            {
                _engine?.Dispose();
                _engine = null;
                _engineLanguageCode = null;

                _engine = new PaddleOcrAll(ModelFor(language.Code), PaddleDevice.Mkldnn())
                {
                    AllowRotateDetection = false,
                    Enable180Classification = false,
                };
                _engineLanguageCode = language.Code;
            }

            using var fullImage = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (fullImage.Empty())
            {
                return new List<OcrLineResult>();
            }

            var roi = BuildRoi(fullImage, focusImagePoint);

            PaddleOcrResult result;
            if (roi is { } cropRect)
            {
                using var cropped = new Mat(fullImage, cropRect);
                result = _engine.Run(cropped);
            }
            else
            {
                result = _engine.Run(fullImage);
            }

            var offsetX = roi?.X ?? 0;
            var offsetY = roi?.Y ?? 0;

            return result.Regions
                .Select(region => ToLine(region, offsetX, offsetY))
                .OrderBy(line => line.Words.Count > 0 ? line.Words[0].Rect.Y : 0)
                .ThenBy(line => line.Words.Count > 0 ? line.Words[0].Rect.X : 0)
                .ToList();
        }
    }

    private static FullOcrModel ModelFor(string languageCode) => languageCode switch
    {
        "de" or "fr" or "es" => LocalFullModels.LatinV5,
        "ko" => LocalFullModels.KoreanV5,
        // Основная модель PP-OCRv5 обучена сразу на китайском, японском и английском.
        "ja" or "zh" => LocalFullModels.ChineseV5,
        _ => LocalFullModels.EnglishV5,
    };

    private static Rect? BuildRoi(Mat image, (int X, int Y)? focusImagePoint)
    {
        if (focusImagePoint is not { } focus)
        {
            return null;
        }

        var x = Math.Max(0, focus.X - FocusCropWidth / 2);
        var y = Math.Max(0, focus.Y - FocusCropHeight / 2);
        var width = Math.Min(FocusCropWidth, image.Width - x);
        var height = Math.Min(FocusCropHeight, image.Height - y);

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new Rect(x, y, width, height);
    }

    // Paddle возвращает повёрнутый прямоугольник всего фрагмента строки, а приложению
    // нужны рамки отдельных слов (клик по слову в оверлее, поиск слова под курсором).
    // Берём осевой охватывающий прямоугольник и режем его по словам пропорционально
    // числу символов — для горизонтального текста этого достаточно.
    private static OcrLineResult ToLine(PaddleOcrResultRegion region, int offsetX, int offsetY)
    {
        var corners = region.Rect.Points();
        var minX = corners.Min(p => p.X) + offsetX;
        var maxX = corners.Max(p => p.X) + offsetX;
        var minY = corners.Min(p => p.Y) + offsetY;
        var maxY = corners.Max(p => p.Y) + offsetY;

        var text = region.Text;
        var words = SplitIntoWords(text, minX, minY, maxX - minX, maxY - minY);
        return new OcrLineResult(text, words);
    }

    private static List<OcrWordResult> SplitIntoWords(string text, double x, double y, double width, double height)
    {
        var words = new List<OcrWordResult>();
        if (string.IsNullOrWhiteSpace(text) || width <= 0)
        {
            return words;
        }

        // Иероглифический текст без пробелов режем посимвольно — так же ведёт себя
        // Windows OCR для китайского/японского, и клик по одному знаку остаётся возможным.
        if (!text.Contains(' ') && text.Length > 1 && text.Any(IsCjkChar))
        {
            var charWidth = width / text.Length;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i].ToString();
                if (!string.IsNullOrWhiteSpace(ch))
                {
                    words.Add(new OcrWordResult(ch, new Windows.Foundation.Rect(x + i * charWidth, y, charWidth, height)));
                }
            }

            return words;
        }

        var perCharWidth = width / text.Length;
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == ' ')
            {
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && text[index] != ' ')
            {
                index++;
            }

            var token = text[start..index];
            words.Add(new OcrWordResult(
                token,
                new Windows.Foundation.Rect(x + start * perCharWidth, y, (index - start) * perCharWidth, height)));
        }

        return words;
    }

    private static bool IsCjkChar(char c) =>
        (c >= 0x2E80 && c <= 0x9FFF)   // радикалы CJK, кана, иероглифы
        || (c >= 0xAC00 && c <= 0xD7AF) // хангыль
        || (c >= 0xF900 && c <= 0xFAFF); // CJK Compatibility
}
