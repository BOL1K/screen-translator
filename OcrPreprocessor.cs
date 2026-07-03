using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Препроцессинг скриншота перед подачей в Windows OCR — апскейл + ч/б + контраст
// (и опционально бинаризация), чтобы точнее распознавались мелкие/стилизованные шрифты.
// Настройки ниже можно крутить/выключать по отдельности.
static class OcrPreprocessor
{
    // static readonly, а не const — с const компилятор считает выключенные ветки
    // недостижимым кодом (CS0162), хотя это просто настройки для ручного тюнинга.
    public static readonly float UpscaleFactor = 2.5f;
    public static readonly bool EnableGrayscaleContrast = true;
    public static readonly float ContrastBoost = 1.3f;
    public static readonly bool EnableBinarization = false;
    public static readonly byte BinarizationThreshold = 140;

    // Возвращает путь к обработанной картинке и коэффициент апскейла
    // (им нужно поделить координаты слов от OCR, чтобы вернуться в систему исходного скриншота).
    public static (string Path, double Scale) Preprocess(string sourcePath)
    {
        using var source = new Bitmap(sourcePath);

        var scale = UpscaleFactor;
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));

        using var upscaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(upscaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (EnableGrayscaleContrast)
            {
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(BuildGrayscaleContrastMatrix(ContrastBoost));
                g.DrawImage(
                    source,
                    new Rectangle(0, 0, width, height),
                    0, 0, source.Width, source.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }
            else
            {
                g.DrawImage(source, 0, 0, width, height);
            }
        }

        if (EnableBinarization)
        {
            Binarize(upscaled, BinarizationThreshold);
        }

        var outputPath = Path.Combine(
            Path.GetDirectoryName(sourcePath) ?? ".",
            Path.GetFileNameWithoutExtension(sourcePath) + "_ocr.png");

        upscaled.Save(outputPath, ImageFormat.Png);

        return (outputPath, scale);
    }

    static ColorMatrix BuildGrayscaleContrastMatrix(float contrast)
    {
        const float rWeight = 0.299f;
        const float gWeight = 0.587f;
        const float bWeight = 0.114f;

        // Контраст растягивается вокруг середины серого (0.5), иначе просто светлеет/темнеет картинка.
        var translate = 0.5f * (1f - contrast);

        float[][] m =
        {
            new[] { rWeight * contrast, rWeight * contrast, rWeight * contrast, 0f, 0f },
            new[] { gWeight * contrast, gWeight * contrast, gWeight * contrast, 0f, 0f },
            new[] { bWeight * contrast, bWeight * contrast, bWeight * contrast, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { translate, translate, translate, 0f, 1f },
        };

        return new ColorMatrix(m);
    }

    static void Binarize(Bitmap bitmap, byte threshold)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);

        try
        {
            var byteCount = Math.Abs(data.Stride) * data.Height;
            var buffer = new byte[byteCount];
            Marshal.Copy(data.Scan0, buffer, 0, byteCount);

            for (var y = 0; y < data.Height; y++)
            {
                var rowStart = y * data.Stride;
                for (var x = 0; x < data.Width; x++)
                {
                    var offset = rowStart + x * 3;
                    var luminance = (buffer[offset] + buffer[offset + 1] + buffer[offset + 2]) / 3;
                    var value = (byte)(luminance >= threshold ? 255 : 0);
                    buffer[offset] = value;
                    buffer[offset + 1] = value;
                    buffer[offset + 2] = value;
                }
            }

            Marshal.Copy(buffer, 0, data.Scan0, byteCount);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
