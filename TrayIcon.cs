using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

static class TrayIcon
{
    private static NotifyIcon? _icon;

    public static event Action? NewTranslationRequested;
    public static event Action? ShowDictionaryRequested;
    public static event Action? ChooseModelRequested;

    public static void Start()
    {
        var thread = new Thread(RunTrayThread);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void RunTrayThread()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Новый перевод", null, (_, _) => NewTranslationRequested?.Invoke());
        menu.Items.Add("Посмотреть словарь", null, (_, _) => ShowDictionaryRequested?.Invoke());
        menu.Items.Add("Выбрать модель / статистика", null, (_, _) => ChooseModelRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit());

        _icon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Экранный переводчик",
            Visible = true,
            ContextMenuStrip = menu
        };

        Application.Run();
    }

    private static void Exit()
    {
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
        }

        Environment.Exit(0);
    }

    // Тёмный бейдж с буквой "A" и голубой полоской-подсветкой под ней — как подсветка
    // слова в оверлее. Рисуется в памяти через GDI+, без отдельного файла .ico.
    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var backgroundColor = Color.FromArgb(255, 0x1E, 0x1E, 0x2E);
            var borderColor = Color.FromArgb(255, 0x33, 0x33, 0x4A);
            var accentColor = Color.FromArgb(255, 0x89, 0xB4, 0xFA);

            using (var backgroundPath = RoundedRect(new RectangleF(1, 1, 30, 30), 8))
            {
                using var backgroundBrush = new SolidBrush(backgroundColor);
                g.FillPath(backgroundBrush, backgroundPath);

                using var borderPen = new Pen(borderColor, 1);
                g.DrawPath(borderPen, backgroundPath);
            }

            using (var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(Color.White))
            {
                const string text = "A";
                var textSize = g.MeasureString(text, font);
                g.DrawString(text, font, textBrush, (32 - textSize.Width) / 2f, 3f);
            }

            using (var barPath = RoundedRect(new RectangleF(9, 23, 14, 4), 2))
            {
                using var barBrush = new SolidBrush(accentColor);
                g.FillPath(barBrush, barPath);
            }
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            using var handleIcon = Icon.FromHandle(hIcon);
            return (Icon)handleIcon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
