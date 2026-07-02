using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

static class ModelWindow
{
    public static void Show()
    {
        var thread = new Thread(RunWindowThread) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void RunWindowThread()
    {
        var listBox = new ListBox
        {
            Margin = new Thickness(16, 16, 16, 8),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ItemContainerStyle = AppTheme.CreateListBoxItemStyle(),
        };

        void RefreshList()
        {
            var selectedIndex = listBox.SelectedIndex;
            listBox.Items.Clear();

            for (var i = 0; i < ModelManager.Models.Count; i++)
            {
                var model = ModelManager.Models[i];
                var marker = i == ModelManager.HomeIndex ? " [ОСНОВНАЯ]" : "";
                var exhausted = ModelManager.IsExhaustedToday(model.Id) ? " [лимит на сегодня исчерпан]" : "";

                var itemText = new TextBlock { TextWrapping = TextWrapping.Wrap };
                itemText.Inlines.Add(new Run(model.DisplayName) { FontWeight = FontWeights.Bold, Foreground = AppTheme.HeadingBrush });
                itemText.Inlines.Add(new Run($" — до {model.DailyLimit}/день{marker}{exhausted}") { Foreground = AppTheme.TextBrush });

                listBox.Items.Add(new ListBoxItem { Content = itemText });
            }

            if (selectedIndex >= 0 && selectedIndex < listBox.Items.Count)
            {
                listBox.SelectedIndex = selectedIndex;
            }
        }

        void MakeSelectedHome()
        {
            if (listBox.SelectedIndex >= 0)
            {
                ModelManager.SetHomeModel(listBox.SelectedIndex);
                RefreshList();
            }
        }

        listBox.MouseDoubleClick += (_, _) => MakeSelectedHome();

        var makeHomeButton = new Button
        {
            Content = "Сделать основной",
            Style = AppTheme.CreateButtonStyle(),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
        };
        makeHomeButton.Click += (_, _) => MakeSelectedHome();

        RefreshList();

        var root = new DockPanel();
        DockPanel.SetDock(makeHomeButton, Dock.Bottom);
        root.Children.Add(makeHomeButton);
        root.Children.Add(listBox);

        var window = new Window
        {
            Title = "Модели",
            Width = 480,
            Height = 360,
            Content = root,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        AppTheme.ApplyWindow(window);

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
}
