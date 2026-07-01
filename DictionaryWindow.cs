using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

static class DictionaryWindow
{
    public static void Show(List<DictionaryEntry> entries)
    {
        var thread = new Thread(() => RunWindowThread(entries)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void RunWindowThread(List<DictionaryEntry> entries)
    {
        FrameworkElement content;

        if (entries.Count == 0)
        {
            content = new TextBlock
            {
                Text = "Словарь пока пуст.",
                Margin = new Thickness(16),
            };
        }
        else
        {
            var panel = new StackPanel { Margin = new Thickness(16) };

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];

                var entryPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
                entryPanel.Children.Add(new TextBlock
                {
                    Text = $"{i + 1}. {e.Word} — {e.Translation} ({e.PartOfSpeech})",
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                });
                AddLine(entryPanel, "Пояснение", e.Explanation);
                AddLine(entryPanel, "Предложение", e.Sentence);
                AddLine(entryPanel, "Перевод предложения", e.ContextTranslation);

                panel.Children.Add(entryPanel);
            }

            content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel,
            };
        }

        var window = new Window
        {
            Title = "Словарь",
            Width = 480,
            Height = 500,
            Content = content,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };

        window.Closed += (_, _) => Dispatcher.ExitAllFrames();

        window.Show();
        Dispatcher.Run();
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
            Margin = new Thickness(0, 0, 0, 2),
        });
    }
}
