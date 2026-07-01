using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

static class NewTranslationWindow
{
    public static void Show(Func<string, string, bool, Task> onSubmit)
    {
        var thread = new Thread(() => RunWindowThread(onSubmit)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void RunWindowThread(Func<string, string, bool, Task> onSubmit)
    {
        var sentenceBox = new TextBox
        {
            Height = 60,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var wordBox = new TextBox { Margin = new Thickness(0, 0, 0, 12) };

        var saveCheckBox = new CheckBox { Content = "Сохранить в словарь", Margin = new Thickness(0, 0, 0, 12) };

        var translateButton = new Button
        {
            Content = "Перевести",
            IsEnabled = false,
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        void UpdateButtonState() =>
            translateButton.IsEnabled = !string.IsNullOrWhiteSpace(sentenceBox.Text) && !string.IsNullOrWhiteSpace(wordBox.Text);

        sentenceBox.TextChanged += (_, _) => UpdateButtonState();
        wordBox.TextChanged += (_, _) => UpdateButtonState();

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Предложение:", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(sentenceBox);
        panel.Children.Add(new TextBlock { Text = "Слово из предложения:", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(wordBox);
        panel.Children.Add(saveCheckBox);
        panel.Children.Add(translateButton);

        var window = new Window
        {
            Title = "Новый перевод",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = panel,
        };

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };

        translateButton.Click += (_, _) =>
        {
            var sentence = sentenceBox.Text.Trim();
            var word = wordBox.Text.Trim();
            var save = saveCheckBox.IsChecked == true;

            window.Close();

            // Закрываем окно сразу и переводим в фоне (как и клики по оверлею) — чтобы
            // перевод не зависел от Dispatcher этого уже закрытого окна.
            Task.Run(async () =>
            {
                try
                {
                    await onSubmit(sentence, word, save);
                }
                catch (Exception ex)
                {
                    NativeMethods.MessageBox(IntPtr.Zero, $"Ошибка при переводе: {ex.Message}", "Экранный переводчик", 0x30);
                }
            });
        };

        window.Closed += (_, _) => Dispatcher.ExitAllFrames();

        window.Show();
        Dispatcher.Run();
    }
}
