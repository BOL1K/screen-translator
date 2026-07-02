using Microsoft.Win32;

// Автозапуск через HKCU\...\Run — работает без прав администратора,
// действует только для текущего пользователя.
static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenTranslator";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string existing
            && existing.Equals(GetQuotedExecutablePath(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(ValueName, GetQuotedExecutablePath());
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string GetQuotedExecutablePath() => $"\"{Environment.ProcessPath}\"";
}
