using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        var window = new Window
        {
            Title = "Словарь",
            Width = 480,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        AppTheme.ApplyWindow(window);

        var checkBoxes = new List<(CheckBox CheckBox, DictionaryEntry Entry)>();

        void Refresh()
        {
            checkBoxes.Clear();
            window.Content = entries.Count == 0
                ? BuildEmptyContent()
                : BuildEntriesContent(entries, checkBoxes, DeleteChecked, DeleteAll);
        }

        void DeleteChecked()
        {
            var toDelete = checkBoxes.Where(x => x.CheckBox.IsChecked == true).Select(x => x.Entry).ToList();
            if (toDelete.Count == 0)
            {
                return;
            }

            var result = MessageBox.Show($"Удалить {toDelete.Count} слов?", "Экранный переводчик", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var toDeleteSet = new HashSet<DictionaryEntry>(toDelete, ReferenceEqualityComparer.Instance);
            entries.RemoveAll(e => toDeleteSet.Contains(e));
            DictionaryStore.Save(entries);
            Refresh();
        }

        void DeleteAll()
        {
            if (entries.Count == 0)
            {
                return;
            }

            var result = MessageBox.Show("Удалить ВСЕ слова? Это нельзя отменить", "Экранный переводчик", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            entries.Clear();
            DictionaryStore.Save(entries);
            Refresh();
        }

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
            else if (e.Key == Key.Delete)
            {
                DeleteChecked();
            }
        };

        window.Closed += (_, _) => Dispatcher.ExitAllFrames();

        Refresh();
        window.Show();
        Dispatcher.Run();
    }

    private static TextBlock BuildEmptyContent()
    {
        return new TextBlock
        {
            Text = "Словарь пока пуст.",
            Foreground = AppTheme.TextBrush,
            Margin = new Thickness(16),
        };
    }

    private static DockPanel BuildEntriesContent(
        List<DictionaryEntry> entries,
        List<(CheckBox CheckBox, DictionaryEntry Entry)> checkBoxes,
        Action deleteChecked,
        Action deleteAll)
    {
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
                Foreground = AppTheme.HeadingBrush,
                Margin = new Thickness(0, number == 0 ? 0 : 12, 0, 8),
            });

            foreach (var e in group.Entries)
            {
                var index = number;
                number++;

                var titleBlock = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                };
                titleBlock.Inlines.Add(new Run($"{index + 1}. {e.Word}")
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = AppTheme.AccentBrush,
                });
                titleBlock.Inlines.Add(new Run($" — {e.Translation} ({e.PartOfSpeech})")
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = AppTheme.HeadingBrush,
                });

                var entryPanel = new StackPanel();
                entryPanel.Children.Add(titleBlock);
                AddLine(entryPanel, "Транскрипция", e.Transcription);
                AddLine(entryPanel, "Подсказка", e.Hint);
                AddLine(entryPanel, "Пример", e.Sentence);
                AddLine(entryPanel, "Перевод примера", e.ContextTranslation);
                AddLine(entryPanel, "Пояснение", e.Explanation);

                var checkBox = new CheckBox
                {
                    Style = AppTheme.CreateCheckBoxStyle(),
                    Content = entryPanel,
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 0, 10),
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
                panel.Children.Add(AppTheme.CreateDivider(new Thickness(0, 0, 0, 10)));
            }
        }

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };

        var buttonsPanel = BuildButtonsPanel(checkBoxes, deleteAll);

        var root = new DockPanel();
        DockPanel.SetDock(buttonsPanel, Dock.Top);
        root.Children.Add(buttonsPanel);
        root.Children.Add(scrollViewer);

        return root;
    }

    private static StackPanel BuildButtonsPanel(List<(CheckBox CheckBox, DictionaryEntry Entry)> checkBoxes, Action deleteAll)
    {
        var buttonStyle = AppTheme.CreateButtonStyle();
        var selectAllButton = new Button { Content = "Выделить все", Style = buttonStyle, Margin = new Thickness(0, 0, 8, 0) };
        var selectNoneButton = new Button { Content = "Снять все", Style = buttonStyle, Margin = new Thickness(0, 0, 8, 0) };
        var exportButton = new Button { Content = "Экспортировать в Anki", Style = buttonStyle, Margin = new Thickness(0, 0, 8, 0) };
        var deleteAllButton = new Button { Content = "Удалить все", Style = buttonStyle };

        selectAllButton.Click += (_, _) => SetAllChecked(checkBoxes, true);
        selectNoneButton.Click += (_, _) => SetAllChecked(checkBoxes, false);
        deleteAllButton.Click += (_, _) => deleteAll();

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
        buttonsPanel.Children.Add(deleteAllButton);

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
            .Select(g => (Label: FormatDateLabel(g.Key, today), Entries: g.OrderByDescending(e => e.AddedAt!.Value).ToList()));

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
