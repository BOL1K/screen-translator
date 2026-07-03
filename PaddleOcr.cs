using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using Sdcb.PaddleOCR.Models.Online;

// Второй OCR-движок (PaddleOCR / PP-OCRv5) — заметно точнее Windows OCR на стилизованных
// и игровых шрифтах, но на CPU в разы медленнее. Поэтому он опциональный (настройки),
// а для перевода под курсором распознаётся только область вокруг курсора, не весь экран.
//
// Режим GPU (NVIDIA CUDA) требует: сборку с GPU-библиотеками (dotnet publish -p:PaddleGpu=true),
// видеокарту NVIDIA с свежим драйвером и библиотеки cuDNN 9 + cuBLAS 12 — либо в PATH,
// либо в папке "cuda" рядом с exe. Если что-то из этого не так, движок сам откатывается
// на CPU с одним предупреждением за сеанс.
static class PaddleOcr
{
    // Прокидываются из Program.cs при старте: показ предупреждений и debug-лог.
    public static Action<string>? Warn;
    public static Action<string>? Log;

    // PaddleOcrAll не потокобезопасен, а создание движка стоит ~1 секунду —
    // держим один экземпляр под замком и пересоздаём при смене языка или режима.
    private static readonly object EngineLock = new();
    private static PaddleOcrAll? _engine;
    private static string? _engineLanguageCode;
    private static bool _engineIsGpu;
    private static bool _gpuFailedThisSession;
    private static bool _gpuPathPrepared;

    // Область вокруг курсора для Alt+Q / Alt+E: полный экран PaddleOCR на CPU
    // обрабатывает ~10 секунд, такой кроп — ~3 секунды, а предложение-контекст
    // в него всё равно помещается. На GPU это тоже ускоряет отклик.
    private const int FocusCropWidth = 1100;
    private const int FocusCropHeight = 700;

    public static List<OcrLineResult> RecognizeLines(string imagePath, StudyLanguage language, (int X, int Y)? focusImagePoint, bool preferGpu)
    {
        lock (EngineLock)
        {
            var wantGpu = preferGpu && !_gpuFailedThisSession;
            if (_engine is null || _engineLanguageCode != language.Code || _engineIsGpu != wantGpu)
            {
                RecreateEngine(language, wantGpu);
            }

            using var fullImage = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (fullImage.Empty())
            {
                return new List<OcrLineResult>();
            }

            var roi = BuildRoi(fullImage, focusImagePoint);

            // Замерено на RTX 4050: на GPU распознавание фрагментов пачками по 64 ускоряет
            // полный экран с ~9 до ~2.6 секунд (меньше перенастроек ядер под размер входа),
            // а на CPU то же батчирование наоборот замедляет — там оставляем по одному.
            var recognizeBatchSize = _engineIsGpu ? 64 : 0;

            PaddleOcrResult result;
            if (roi is { } cropRect)
            {
                using var cropped = new Mat(fullImage, cropRect);
                result = _engine!.Run(cropped, recognizeBatchSize);
            }
            else
            {
                result = _engine!.Run(fullImage, recognizeBatchSize);
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

    private static void RecreateEngine(StudyLanguage language, bool wantGpu)
    {
        _engine?.Dispose();
        _engine = null;
        _engineLanguageCode = null;

        if (wantGpu)
        {
            try
            {
                _engine = CreateGpuEngine(language);
                _engineIsGpu = true;
                _engineLanguageCode = language.Code;
                Log?.Invoke($"PaddleOCR: GPU-движок создан для языка {language.Code}");
                return;
            }
            catch (Exception ex)
            {
                _gpuFailedThisSession = true;
                Log?.Invoke($"PaddleOCR GPU: не удалось запустить, откатываюсь на CPU: {ex}");
                Warn?.Invoke(
                    "GPU-режим PaddleOCR недоступен, переключился на CPU (до перезапуска программы).\n\n" +
                    "Возможные причины:\n" +
                    "- эта сборка программы без GPU-библиотек (нужна GPU-сборка);\n" +
                    "- нет видеокарты NVIDIA или драйвер устарел;\n" +
                    "- не найдены cuDNN 9 / cuBLAS 12 (положи их DLL в папку \"cuda\" рядом с exe).\n\n" +
                    "Подробности в gemini_debug.log.");
            }
        }

        _engine = new PaddleOcrAll(LocalModelFor(language.Code), PaddleDevice.Mkldnn())
        {
            AllowRotateDetection = false,
            Enable180Classification = false,
        };
        _engineIsGpu = false;
        _engineLanguageCode = language.Code;
    }

    private static PaddleOcrAll CreateGpuEngine(StudyLanguage language)
    {
        PrepareGpuEnvironment();

        // Отсутствующие CUDA-библиотеки Paddle обнаруживает уже во время инференции
        // и роняет весь процесс нативно (fail-fast, try/catch не спасает). Поэтому
        // проверяем их загружаемость заранее и при проблеме кидаем обычное исключение.
        foreach (var dll in new[] { "cudnn64_9.dll", "cublas64_12.dll", "cublasLt64_12.dll" })
        {
            if (!NativeLibrary.TryLoad(dll, out _))
            {
                throw new DllNotFoundException($"Не найдена библиотека {dll} (ни в PATH, ни в папке cuda рядом с exe).");
            }
        }

        // GPU-предиктор Paddle 3.3 не умеет загружать модель из памяти (падает с ошибкой
        // парсинга JSON), поэтому для GPU используются файловые модели: при первом
        // включении они один раз скачиваются (~20 МБ) в paddle_models рядом с exe.
        var model = DownloadOnlineModel(language);

        return new PaddleOcrAll(model, PaddleDevice.Gpu())
        {
            AllowRotateDetection = false,
            Enable180Classification = false,
        };
    }

    private static void PrepareGpuEnvironment()
    {
        if (_gpuPathPrepared)
        {
            return;
        }

        _gpuPathPrepared = true;

        // Папка "cuda" рядом с exe — место, куда пользователь кладёт DLL из архивов
        // cuDNN и cuBLAS, чтобы не ставить весь CUDA Toolkit и не трогать системный PATH.
        var cudaDir = AppPaths.Resolve("cuda");
        if (Directory.Exists(cudaDir))
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                cudaDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        }
    }

    private static FullOcrModel DownloadOnlineModel(StudyLanguage language)
    {
        Settings.GlobalModelDirectory = AppPaths.Resolve("paddle_models");

        var onlineModel = language.Code switch
        {
            "de" or "fr" or "es" => OnlineFullModels.LatinV5,
            "ko" => OnlineFullModels.KoreanV5,
            "ja" or "zh" => OnlineFullModels.ChineseV5,
            _ => OnlineFullModels.EnglishV5,
        };

        return onlineModel.DownloadAsync().GetAwaiter().GetResult();
    }

    private static FullOcrModel LocalModelFor(string languageCode) => languageCode switch
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
