using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FinderApp.Services;

public class HotKeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_SPACE = 0x20;
    private const uint VK_F = 0x46;

    private const int HOTKEY_ID = 9000;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _isRegistered;

    public event Action? HotKeyPressed;
    public string ActiveShortcutDescription { get; private set; } = "Alt + Space";

    public bool Register(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.Handle;

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(HwndHook);

        // Try Alt + Space first
        bool success = RegisterHotKey(_hwnd, HOTKEY_ID, MOD_ALT | MOD_NOREPEAT, VK_SPACE);
        if (success)
        {
            _isRegistered = true;
            ActiveShortcutDescription = "Alt + Space";
            return true;
        }

        // Fallback 1: Ctrl + Space
        success = RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL | MOD_NOREPEAT, VK_SPACE);
        if (success)
        {
            _isRegistered = true;
            ActiveShortcutDescription = "Ctrl + Space";
            return true;
        }

        // Fallback 2: Ctrl + Shift + F
        success = RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_F);
        if (success)
        {
            _isRegistered = true;
            ActiveShortcutDescription = "Ctrl + Shift + F";
            return true;
        }

        ActiveShortcutDescription = "None (Registration Failed)";
        return false;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotKeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isRegistered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _isRegistered = false;
        }

        _source?.RemoveHook(HwndHook);
    }
}
