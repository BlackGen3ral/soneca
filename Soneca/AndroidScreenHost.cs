using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Soneca;

internal sealed class AndroidScreenHost : HwndHost
{
    private const int WindowStyle = -16, ExtendedStyle = -20;
    private const int Child = 0x40000000, Visible = 0x10000000, ClipChildren = 0x02000000, ClipSiblings = 0x04000000;
    private const uint ResizeFlags = 0x0004 | 0x0010 | 0x4000;
    private IntPtr container;
    private AndroidViewWindow? screen;
    private readonly DispatcherTimer fitTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public AndroidScreenHost() => fitTimer.Tick += (_, _) => ResizeScreen();

    public bool IsAttached(AndroidViewWindow window) => screen == window;

    public void Attach(AndroidViewWindow window)
    {
        Clear();
        if (container == IntPtr.Zero || !OwnsWindow(window))
            throw new IOException("A tela do Android deixou de responder antes de abrir no painel.");

        int style = GetWindowLongW(window.Handle, WindowStyle);
        int extra = GetWindowLongW(window.Handle, ExtendedStyle);
        SetWindowLongW(window.Handle, WindowStyle, (style & ~unchecked((int)0x80CF0000)) | Child | Visible);
        SetWindowLongW(window.Handle, ExtendedStyle, extra & ~0x00040000); // WS_EX_APPWINDOW
        SetParent(window.Handle, container);
        if (GetParent(window.Handle) != container)
            throw new IOException("O Windows não conseguiu encaixar a tela do Android no painel.");
        screen = window;
        ResizeScreen(frameChanged: true);
        fitTimer.Start();
    }

    public void Clear()
    {
        fitTimer.Stop();
        if (screen is { } current && OwnsWindow(current) && GetParent(current.Handle) == container)
        {
            ShowWindowAsync(current.Handle, 0);
        }
        screen = null;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        container = CreateWindowExW(0, "Static", "", Child | Visible | ClipChildren | ClipSiblings | 4,
            0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (container == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        return new HandleRef(this, container);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        fitTimer.Stop();
        if (screen is { } current && OwnsWindow(current) && GetParent(current.Handle) == hwnd.Handle)
        {
            ShowWindowAsync(current.Handle, 0);
            SetParent(current.Handle, IntPtr.Zero);
        }
        screen = null;
        container = IntPtr.Zero;
        DestroyWindow(hwnd.Handle);
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        ResizeScreen();
    }

    private void ResizeScreen(bool frameChanged = false)
    {
        if (screen is not { } current || !OwnsWindow(current) || GetParent(current.Handle) != container ||
            !GetClientRect(container, out var bounds)) return;
        if (!frameChanged && GetWindowRect(container, out var parentRect) && GetWindowRect(current.Handle, out var childRect) &&
            parentRect.Left == childRect.Left && parentRect.Top == childRect.Top &&
            parentRect.Right == childRect.Right && parentRect.Bottom == childRect.Bottom) return;
        SetWindowPos(current.Handle, IntPtr.Zero, 0, 0, Math.Max(1, bounds.Right), Math.Max(1, bounds.Bottom),
            ResizeFlags | (frameChanged ? 0x0020u : 0u));
    }

    private static bool OwnsWindow(AndroidViewWindow window) =>
        GetWindowThreadProcessId(window.Handle, out var processId) != 0 && processId == window.ProcessId;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(int extendedStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int SetWindowLongW(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y,
        int width, int height, uint flags);
}
