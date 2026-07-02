using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
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

        // 520 вместо 400 — длинный перевод предложения при узкой карточке рвётся
        // на много коротких строк и читается плохо; ширина растёт только когда текст длинный.
        var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 280, MaxWidth = 520 };

        panel.Children.Add(new TextBlock
        {
            Text = word,
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        if (!string.IsNullOrWhiteSpace(translation) || !string.IsNullOrWhiteSpace(partOfSpeech))
        {
            var translationRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

            if (!string.IsNullOrWhiteSpace(translation))
            {
                translationRow.Children.Add(new TextBlock
                {
                    Text = translation,
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                });
            }

            if (!string.IsNullOrWhiteSpace(partOfSpeech))
            {
                translationRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3C)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = partOfSpeech.ToUpperInvariant(),
                        FontSize = 11,
                        FontStyle = FontStyles.Italic,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    },
                });
            }

            panel.Children.Add(translationRow);
        }

        if (!string.IsNullOrWhiteSpace(explanation) || !string.IsNullOrWhiteSpace(contextTranslation))
        {
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        if (!string.IsNullOrWhiteSpace(explanation))
        {
            panel.Children.Add(new TextBlock
            {
                Text = explanation,
                FontSize = 14,
                LineHeight = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }

        if (!string.IsNullOrWhiteSpace(contextTranslation))
        {
            var contextText = new TextBlock
            {
                Text = $"❝ {contextTranslation}",
                FontSize = 14,
                LineHeight = 21,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                TextWrapping = TextWrapping.Wrap,
            };

            // Очень длинный перевод предложения не раздувает попап на весь экран:
            // выше этой высоты появляется прокрутка (колесо мыши + тонкий скроллбар).
            var contextScroll = new ScrollViewer
            {
                Content = contextText,
                MaxHeight = 280,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 8, 0, 0),
            };
            contextScroll.Resources.Add(typeof(ScrollBar), CreateSlimScrollBarStyle());

            panel.Children.Add(contextScroll);
        }

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 30, 30, 46)),
            CornerRadius = new CornerRadius(12),
            Child = panel,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.5,
                Direction = 270,
            },
        };

        var shadowSpacer = new Border
        {
            Margin = new Thickness(16),
            Child = card,
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
            Content = shadowSpacer,
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

    // Тонкий тёмный скроллбар под стиль карточки: стандартный светлый системный
    // выглядит чужеродно на тёмном попапе. Шаблон со вложенным Track.Thumb гораздо
    // проще описать XAML-строкой, чем городить из FrameworkElementFactory.
    private static Style CreateSlimScrollBarStyle()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="ScrollBar">
              <Setter Property="Width" Value="8"/>
              <Setter Property="Background" Value="Transparent"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="Transparent">
                      <Track x:Name="PART_Track" IsDirectionReversed="True">
                        <Track.Thumb>
                          <Thumb>
                            <Thumb.Template>
                              <ControlTemplate TargetType="Thumb">
                                <Border Background="#697098" Opacity="0.55" CornerRadius="4" Margin="2,0,0,0"/>
                              </ControlTemplate>
                            </Thumb.Template>
                          </Thumb>
                        </Track.Thumb>
                      </Track>
                    </Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """;

        return (Style)XamlReader.Parse(xaml);
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
}
