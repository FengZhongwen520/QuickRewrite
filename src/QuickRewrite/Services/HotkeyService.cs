using System.Windows.Interop;
using QuickRewrite.Interop;

namespace QuickRewrite.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x5152;
    private readonly HwndSource _messageWindow;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("QuickRewriteHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0
        };

        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
    }

    public bool Register(HotkeyGesture gesture, out string error)
    {
        error = "";
        Unregister();

        var modifiers = gesture.Modifiers | NativeMethods.MOD_NOREPEAT;
        if (!NativeMethods.RegisterHotKey(_messageWindow.Handle, HotkeyId, modifiers, gesture.VirtualKey))
        {
            var code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            error = $"快捷键 {gesture.DisplayName} 注册失败，可能已被其他程序占用。（错误码 {code}）";
            return false;
        }

        _registered = true;
        return true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_messageWindow.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _messageWindow.RemoveHook(WndProc);
        _messageWindow.Dispose();
    }
}