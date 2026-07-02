using System.Globalization;
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
        var window = entries.Count == 0 ? BuildEmptyWindow() : BuildWindowWithEntries(entries);

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

    private static Window BuildEmptyWindow()
    {
        return new Window
        {
            Title = "Словарь",
            Width = 480,
            Height = 500,
            Content = new TextBlock
            {
                Text = "Словарь пока пуст.",
                Margin = new Thickness(16),
            },
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
    }

    private static Window BuildWindowWithEntries(List<DictionaryEntry> entries)
    {
        var checkBoxes = new List<(CheckBox CheckBox, DictionaryEntry Entry)>();
        var panel = new StackPanel { Margin = new Thickness(16) };
        int? lastClickedIndex = null;
        var number = 0;

        foreach (var group in GroupByDate(entries))
        {
            panel.Children.Add(new TextBlock
            {
                Text = group.Label,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Margin = new Thickness(0, number == 0 ? 0 : 12, 0, 8),
            });

            foreach (var e in group.Entries)
            {
                var index = number;
                number++;

                var entryPanel = new StackPanel();
                entryPanel.Children.Add(new TextBlock
                {
                    Text = $"{index + 1}. {e.Word} — {e.Translation} ({e.PartOfSpeech})",
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                });
                AddLine(entryPanel, "Транскрипция", e.Transcription);
                AddLine(entryPanel, "Подсказка", e.Hint);
                AddLine(entryPanel, "Пример", e.Sentence);
                AddLine(entryPanel, "Перевод примера", e.ContextTranslation);
                AddLine(entryPanel, "Пояснение", e.Explanation);

                var checkBox = new CheckBox
                {
                    Content = entryPanel,
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 0, 16),
                };

                checkBox.Click += (_, _) =>
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && lastClickedIndex.HasValue)
                    {
                        var from = Math.Min(lastClickedIndex.Value, index);
                        var to = Math.Max(lastClickedIndex.Value, index);
                        var isChecked = checkBox.IsChecked == true;

                        for (var j = from; j <= to; j++)
                        {
                            checkBoxes[j].CheckBox.IsChecked = isChecked;
                        }
                    }

                    lastClickedIndex = index;
                };

                checkBoxes.Add((checkBox, e));
                panel.Children.Add(checkBox);
            }
        }

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };

        var buttonsPanel = BuildButtonsPanel(checkBoxes);

        var root = new DockPanel();
        DockPanel.SetDock(buttonsPanel, Dock.Top);
        root.Children.Add(buttonsPanel);
        root.Children.Add(scrollViewer);

        return new Window
        {
            Title = "Словарь",
            Width = 480,
            Height = 500,
            Content = root,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
    }

    private static StackPanel BuildButtonsPanel(List<(CheckBox CheckBox, DictionaryEntry Entry)> checkBoxes)
    {
        var selectAllButton = new Button { Content = "Выделить все", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
        var selectNoneButton = new Button { Content = "Снять все", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
        var exportButton = new Button { Content = "Экспортировать в Anki", Padding = new Thickness(8, 4, 8, 4) };

        selectAllButton.Click += (_, _) => SetAllChecked(checkBoxes, true);
        selectNoneButton.Click += (_, _) => SetAllChecked(checkBoxes, false);

        exportButton.Click += (_, _) =>
        {
            var selected = checkBoxes.Where(x => x.CheckBox.IsChecked == true).Select(x => x.Entry).ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show("Выбери хотя бы одно слово.", "Экранный переводчик");
                return;
            }

            AnkiExporter.Export(selected);
        };

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 16, 16, 0),
        };
        buttonsPanel.Children.Add(selectAllButton);
        buttonsPanel.Children.Add(selectNoneButton);
        buttonsPanel.Children.Add(exportButton);

        return buttonsPanel;
    }

    private static void SetAllChecked(List<(CheckBox CheckBox, DictionaryEntry Entry)> checkBoxes, bool isChecked)
    {
        foreach (var (checkBox, _) in checkBoxes)
        {
            checkBox.IsChecked = isChecked;
        }
    }

    private static IEnumerable<(string Label, List<DictionaryEntry> Entries)> GroupByDate(List<DictionaryEntry> entries)
    {
        var today = DateTime.Today;

        var dated = entries
            .Where(e => e.AddedAt.HasValue)
            .GroupBy(e => e.AddedAt!.Value.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => (Label: FormatDateLabel(g.Key, today), Entries: g.ToList()));

        foreach (var group in dated)
        {
            yield return group;
        }

        var undated = entries.Where(e => !e.AddedAt.HasValue).ToList();
        if (undated.Count > 0)
        {
            yield return ("Без даты", undated);
        }
    }

    private static string FormatDateLabel(DateTime date, DateTime today)
    {
        if (date == today)
        {
            return "Сегодня";
        }

        if (date == today.AddDays(-1))
        {
            return "Вчера";
        }

        return date.ToString("d MMMM yyyy", new CultureInfo("ru-RU"));
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
