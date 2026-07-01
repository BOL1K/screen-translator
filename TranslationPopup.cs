using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

static class TranslationPopup
{
    private static Window? _currentWindow;

    public static void Show(string word, string translation, string partOfSpeech, string explanation, string contextTranslation, Windows.Foundation.Rect? wordScreenRect = null)
    {
        CloseCurrent();

        var thread = new Thread(() => RunPopupThread(word, translation, partOfSpeech, explanation, contextTranslation, wordScreenRect))
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static void CloseCurrent()
    {
        if (_currentWindow is { } existingWindow)
        {
            existingWindow.Dispatcher.Invoke(existingWindow.Close);
        }
    }

    private static void RunPopupThread(string word, string translation, string partOfSpeech, string explanation, string contextTranslation, Windows.Foundation.Rect? wordScreenRect)
    {
        EscCloseCoordinator.RequestWatcher();

        var panel = new StackPanel { Margin = new Thickness(16), Width = 420 };

        panel.Children.Add(new TextBlock
        {
            Text = word,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        AddLine(panel, "Перевод", translation);
        AddLine(panel, "Часть речи", partOfSpeech);
        AddLine(panel, "Пояснение", explanation);
        AddLine(panel, "Перевод предложения", contextTranslation);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 30, 30, 30)),
            CornerRadius = new CornerRadius(8),
            Child = panel,
        };

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = border,
        };

        if (wordScreenRect is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.MouseDown += (_, _) => window.Close();

        window.Loaded += (_, _) =>
        {
            if (wordScreenRect is { } rect)
            {
                PositionNearWord(window, rect);
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

    // Позиция считается в физических пикселях (как SetWindowPos у оверлея) —
    // Window.Left/Top под Per-Monitor DPI до Loaded ненадёжны, а тут уже известен
    // фактический размер окна (SizeToContent уже сработал).
    private static void PositionNearWord(Window window, Windows.Foundation.Rect wordScreenRect)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dpi = VisualTreeHelper.GetDpi(window);

        var popupWidthPx = window.ActualWidth * dpi.DpiScaleX;
        var popupHeightPx = window.ActualHeight * dpi.DpiScaleY;

        const int smXVirtualScreen = 76;
        const int smYVirtualScreen = 77;
        const int smCXVirtualScreen = 78;
        const int smCYVirtualScreen = 79;
        var vsX = NativeMethods.GetSystemMetrics(smXVirtualScreen);
        var vsY = NativeMethods.GetSystemMetrics(smYVirtualScreen);
        var vsWidth = NativeMethods.GetSystemMetrics(smCXVirtualScreen);
        var vsHeight = NativeMethods.GetSystemMetrics(smCYVirtualScreen);

        const double gap = 8;
        var x = wordScreenRect.X;
        var y = wordScreenRect.Y + wordScreenRect.Height + gap;

        if (y + popupHeightPx > vsY + vsHeight)
        {
            y = wordScreenRect.Y - popupHeightPx - gap;
        }

        if (x + popupWidthPx > vsX + vsWidth)
        {
            x = vsX + vsWidth - popupWidthPx;
        }

        if (x < vsX)
        {
            x = vsX;
        }

        if (y < vsY)
        {
            y = vsY;
        }

        if (y + popupHeightPx > vsY + vsHeight)
        {
            y = vsY + vsHeight - popupHeightPx;
        }

        const uint swpNoSize = 0x0001;
        const uint swpNoZOrder = 0x0004;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, (int)x, (int)y, 0, 0, swpNoSize | swpNoZOrder);
    }

    private static void AddLine(StackPanel panel, string label, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"{label}: {text}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 6),
        });
    }
}
