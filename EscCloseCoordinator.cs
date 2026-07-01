static class EscCloseCoordinator
{
    private const uint WmEscWatcherRequest = 0x8000 + 1; // WM_APP + 1, не пересекается с WM_HOTKEY (0x0312)

    private static uint _hotkeyThreadId;
    private static int _hotkeyId;
    private static uint _virtualKeyEscape;
    private static int _watcherCount;
    private static bool _hotkeyRegistered;

    public static void Init(uint hotkeyThreadId, int hotkeyId, uint virtualKeyEscape)
    {
        _hotkeyThreadId = hotkeyThreadId;
        _hotkeyId = hotkeyId;
        _virtualKeyEscape = virtualKeyEscape;
    }

    // Просим поток-слушатель хоткеев зарегистрировать/снять Esc. Регистрация Escape
    // обязана происходить на том же потоке, что вызывает GetMessage — иначе система
    // не доставит WM_HOTKEY по назначению. Другие потоки (клик в оверлее, ручной ввод)
    // не могут зарегистрировать хоткей сами, поэтому просят об этом через сообщение.
    public static void RequestWatcher()
    {
        if (_hotkeyThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hotkeyThreadId, WmEscWatcherRequest, (IntPtr)1, IntPtr.Zero);
        }
    }

    public static void ReleaseWatcher()
    {
        if (_hotkeyThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hotkeyThreadId, WmEscWatcherRequest, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public static bool IsWatcherRequestMessage(uint message) => message == WmEscWatcherRequest;

    // Вызывать только из потока, который слушает хоткеи (тот же, где вызывается GetMessage).
    public static void HandleWatcherRequestMessage(IntPtr wParam)
    {
        if (wParam != IntPtr.Zero)
        {
            _watcherCount++;
            if (_watcherCount == 1 && !_hotkeyRegistered)
            {
                _hotkeyRegistered = NativeMethods.RegisterHotKey(IntPtr.Zero, _hotkeyId, 0, _virtualKeyEscape);
            }
        }
        else
        {
            _watcherCount = Math.Max(0, _watcherCount - 1);
            if (_watcherCount == 0 && _hotkeyRegistered)
            {
                NativeMethods.UnregisterHotKey(IntPtr.Zero, _hotkeyId);
                _hotkeyRegistered = false;
            }
        }
    }

    public static void Cleanup()
    {
        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, _hotkeyId);
            _hotkeyRegistered = false;
        }

        _watcherCount = 0;
    }
}
