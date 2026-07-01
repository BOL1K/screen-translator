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
            Icon = System.Drawing.SystemIcons.Application,
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
}
