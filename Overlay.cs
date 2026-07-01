using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

static class Overlay
{
    private static Window? _currentWindow;

    public static void Show(
        List<(string Text, string Context, Windows.Foundation.Rect Rect)> words,
        int originX,
        int originY,
        Func<string, string, Windows.Foundation.Rect, Task> onLeftClick,
        Func<string, string, Windows.Foundation.Rect, Task> onRightClick)
    {
        CloseCurrent();

        var thread = new Thread(() => RunOverlayThread(words, originX, originY, onLeftClick, onRightClick))
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void RunClickAction(Func<Task> action)
    {
        Task.Run(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"Ошибка при переводе слова из оверлея: {ex.Message}");
            }
        });
    }

    public static void CloseCurrent()
    {
        if (_currentWindow is { } existingWindow)
        {
            existingWindow.Dispatcher.Invoke(existingWindow.Close);
        }
    }

    private static void RunOverlayThread(
        List<(string Text, string Context, Windows.Foundation.Rect Rect)> words,
        int originX,
        int originY,
        Func<string, string, Windows.Foundation.Rect, Task> onLeftClick,
        Func<string, string, Windows.Foundation.Rect, Task> onRightClick)
    {
        EscCloseCoordinator.RequestWatcher();

        // Alpha=1 (не 0) — визуально неотличимо от прозрачного, но не даёт Windows
        // пропускать события мыши "сквозь" полностью прозрачные пиксели layered-окна.
        var almostInvisible = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = almostInvisible,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
        };

        var canvas = new Canvas { Background = almostInvisible };
        window.Content = canvas;

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            const int smCXVirtualScreen = 78;
            const int smCYVirtualScreen = 79;
            var width = NativeMethods.GetSystemMetrics(smCXVirtualScreen);
            var height = NativeMethods.GetSystemMetrics(smCYVirtualScreen);

            const uint swpNoZOrder = 0x0004;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, originX, originY, width, height, swpNoZOrder);
        };

        var highlight = new Rectangle
        {
            Stroke = Brushes.Lime,
            StrokeThickness = 2,
            Visibility = Visibility.Collapsed,
        };
        canvas.Children.Add(highlight);

        var wordRectsInDip = new List<(string Text, string Context, Rect DipRect, Windows.Foundation.Rect ScreenRect)>();

        window.Loaded += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(window);

            foreach (var (text, context, rect) in words)
            {
                var dipRect = new Rect(
                    rect.X / dpi.DpiScaleX,
                    rect.Y / dpi.DpiScaleY,
                    rect.Width / dpi.DpiScaleX,
                    rect.Height / dpi.DpiScaleY);

                var screenRect = new Windows.Foundation.Rect(rect.X + originX, rect.Y + originY, rect.Width, rect.Height);

                wordRectsInDip.Add((text, context, dipRect, screenRect));
            }

            window.Activate();
        };

        window.MouseMove += (_, e) =>
        {
            var cursor = e.GetPosition(canvas);

            foreach (var (_, _, dipRect, _) in wordRectsInDip)
            {
                if (!dipRect.Contains(cursor))
                {
                    continue;
                }

                highlight.Width = dipRect.Width;
                highlight.Height = dipRect.Height;
                Canvas.SetLeft(highlight, dipRect.X);
                Canvas.SetTop(highlight, dipRect.Y);
                highlight.Visibility = Visibility.Visible;
                return;
            }

            highlight.Visibility = Visibility.Collapsed;
        };

        window.MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)
            {
                return;
            }

            var cursor = e.GetPosition(canvas);

            foreach (var (text, context, dipRect, screenRect) in wordRectsInDip)
            {
                if (!dipRect.Contains(cursor))
                {
                    continue;
                }

                if (e.ChangedButton == MouseButton.Left)
                {
                    RunClickAction(() => onLeftClick(text, context, screenRect));
                }
                else
                {
                    RunClickAction(() => onRightClick(text, context, screenRect));
                }

                return;
            }
        };

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_currentWindow, window))
            {
                _currentWindow = null;
            }

            EscCloseCoordinator.ReleaseWatcher();
            Dispatcher.ExitAllFrames();
        };

        _currentWindow = window;

        window.Show();
        Dispatcher.Run();
    }
}
