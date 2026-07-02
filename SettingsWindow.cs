using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Globalization;
using Windows.Media.Ocr;

static class SettingsWindow
{
    private const string ApiKeyHelpUrl = "https://aistudio.google.com/apikey";

    public static void Show() => ShowInternal(null, null);

    // Блокирует вызывающий поток до закрытия окна. Возвращает введённый и сохранённый ключ,
    // либо null, если окно закрыли без сохранения — используется, когда ключа нет нигде
    // и нужно сразу же продолжить текущий запрос перевода уже введённым ключом.
    public static string? ShowAndWaitForKey(string introMessage)
    {
        string? result = null;
        using var done = new ManualResetEventSlim(false);

        ShowInternal(introMessage, key =>
        {
            result = key;
            done.Set();
        });

        done.Wait();
        return result;
    }

    private static void ShowInternal(string? introMessage, Action<string?>? onClosed)
    {
        var thread = new Thread(() => RunWindowThread(introMessage, onClosed)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void RunWindowThread(string? introMessage, Action<string?>? onClosed)
    {
        string? savedKey = null;

        var window = new Window
        {
            Title = "Настройки",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        AppTheme.ApplyWindow(window);

        var panel = new StackPanel { Margin = new Thickness(20) };

        if (!string.IsNullOrWhiteSpace(introMessage))
        {
            panel.Children.Add(new TextBlock
            {
                Text = introMessage,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.Bold,
                Foreground = AppTheme.HeadingBrush,
                Margin = new Thickness(0, 0, 0, 14),
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Gemini API-ключ",
            Foreground = AppTheme.TextBrush,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var settings = SettingsStore.Load();

        var keyBox = new TextBox
        {
            Text = settings.ApiKey ?? "",
            Background = AppTheme.ButtonBackground,
            Foreground = AppTheme.HeadingBrush,
            CaretBrush = AppTheme.HeadingBrush,
            BorderBrush = AppTheme.DividerBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 14),
        };
        panel.Children.Add(keyBox);

        panel.Children.Add(AppTheme.CreateDivider());

        panel.Children.Add(new TextBlock
        {
            Text = "Изучаемый язык",
            Foreground = AppTheme.TextBrush,
            Margin = new Thickness(0, 10, 0, 4),
        });

        var currentLanguage = StudyLanguages.FromCode(settings.StudyLanguageCode);

        var languageList = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemContainerStyle = AppTheme.CreateListBoxItemStyle(),
            Margin = new Thickness(0, 0, 0, 2),
        };

        foreach (var language in StudyLanguages.All)
        {
            var hasOcrPack = OcrEngine.IsLanguageSupported(new Language(language.OcrTag));

            var itemText = new TextBlock { TextWrapping = TextWrapping.Wrap };
            itemText.Inlines.Add(new Run(language.DisplayName) { Foreground = AppTheme.HeadingBrush });
            if (!hasOcrPack)
            {
                itemText.Inlines.Add(new Run("  — нет OCR-пакета Windows") { Foreground = AppTheme.TextBrush, FontStyle = FontStyles.Italic });
            }

            languageList.Items.Add(new ListBoxItem { Content = itemText, Tag = language });
        }

        languageList.SelectedIndex = StudyLanguages.All.FindIndex(l => l.Code == currentLanguage.Code);
        panel.Children.Add(languageList);

        var ocrHint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.TextBrush,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(0, 2, 0, 10),
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(ocrHint);

        StudyLanguage? SelectedLanguage() => (languageList.SelectedItem as ListBoxItem)?.Tag as StudyLanguage;

        void RefreshOcrHint()
        {
            var selected = SelectedLanguage();
            if (selected is null || OcrEngine.IsLanguageSupported(new Language(selected.OcrTag)))
            {
                ocrHint.Visibility = Visibility.Collapsed;
                return;
            }

            ocrHint.Text =
                $"Для языка «{selected.DisplayName}» не установлен OCR-пакет Windows — распознавание слов с экрана не заработает, пока его не добавить:\n" +
                "Параметры Windows -> Время и язык -> Язык и регион -> Добавить язык -> " +
                $"{selected.DisplayName} ({selected.NativeName}), при установке отметь «Распознавание текста (OCR)», затем перезапусти программу.";
            ocrHint.Visibility = Visibility.Visible;
        }

        languageList.SelectionChanged += (_, _) => RefreshOcrHint();
        RefreshOcrHint();

        panel.Children.Add(new TextBlock
        {
            Text = "Как получить бесплатный ключ:",
            FontWeight = FontWeights.Bold,
            Foreground = AppTheme.HeadingBrush,
            Margin = new Thickness(0, 10, 0, 4),
        });

        var instructions = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.TextBrush,
            Margin = new Thickness(0, 0, 0, 14),
        };
        instructions.Inlines.Add(new Run("1. Открой "));
        var link = new Hyperlink(new Run(ApiKeyHelpUrl)) { Foreground = AppTheme.AccentBrush };
        link.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        instructions.Inlines.Add(link);
        instructions.Inlines.Add(new Run("\n2. Войди со своим Google-аккаунтом\n3. Нажми «Create API key»\n4. Скопируй ключ и вставь его в поле выше"));
        panel.Children.Add(instructions);

        var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var saveButton = new Button { Content = "Сохранить", Style = AppTheme.CreateButtonStyle() };
        saveButton.Click += (_, _) =>
        {
            var value = keyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show("Вставь ключ перед сохранением.", "Экранный переводчик");
                return;
            }

            var selectedLanguage = SelectedLanguage() ?? currentLanguage;
            SettingsStore.Save(new AppSettings(value, selectedLanguage.Code));
            savedKey = value;
            window.Close();
        };
        buttonsPanel.Children.Add(saveButton);
        panel.Children.Add(buttonsPanel);

        window.Content = panel;

        window.Closed += (_, _) =>
        {
            onClosed?.Invoke(savedKey);
            Dispatcher.ExitAllFrames();
        };

        window.Show();
        Dispatcher.Run();
    }
}
