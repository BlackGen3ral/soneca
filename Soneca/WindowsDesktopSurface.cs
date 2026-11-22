using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Soneca;

internal sealed class WindowsDesktopSurface : IDesktopSurface
{
    public bool IsAvailable()
    {
        IntPtr input = OpenInputDesktop(0, false, 1);
        if (input == IntPtr.Zero) return false;
        try
        {
            var name = new StringBuilder(128);
            return GetUserObjectInformationW(input, 2, name, name.Capacity * sizeof(char), out _) &&
                name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseDesktop(input); }
    }

    public DesktopFrame Capture()
    {
        IntPtr awareness = SetThreadDpiAwarenessContext(new IntPtr(-4));
        IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
            if (width <= 0 || height <= 0 || !IsAvailable()) throw new IOException("Tela indisponível.");
            double scale = Math.Min(1d, 1280d / Math.Max(width, height));
            int resizedWidth = Math.Max(1, (int)(width * scale));
            int resizedHeight = Math.Max(1, (int)(height * scale));
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new IOException("Tela indisponível.");
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, resizedWidth, resizedHeight);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new IOException("Imagem indisponível.");
            previous = SelectObject(memory, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new IOException("Imagem indisponível.");
            SetStretchBltMode(memory, 4);
            if (!StretchBlt(memory, 0, 0, resizedWidth, resizedHeight, screen, 0, 0, width, height, 0x40CC0020))
                throw new IOException("Não foi possível ler a tela.");
            SelectObject(memory, previous);
            previous = IntPtr.Zero;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            var encoder = new JpegBitmapEncoder { QualityLevel = 63 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = new MemoryStream();
            encoder.Save(output);
            if (!IsAvailable()) throw new IOException("A sessão do Windows está bloqueada.");
            return GetCursorPos(out PointNative cursor) && cursor.X >= 0 && cursor.Y >= 0 && cursor.X < width && cursor.Y < height
                ? new DesktopFrame(output.ToArray(), Normalize(cursor.X, width), Normalize(cursor.Y, height))
                : new DesktopFrame(output.ToArray(), -1, -1);
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            if (awareness != IntPtr.Zero) SetThreadDpiAwarenessContext(awareness);
        }
    }

    public void Apply(DesktopInput input)
    {
        if (!IsAvailable()) throw new IOException("A sessão está bloqueada.");
        if (input.Action == "scroll")
        {
            Send([Mouse(0x0800, unchecked((uint)(input.Delta * 120)))]);
            return;
        }
        if (input.Action == "text")
        {
            Send(input.Value!.SelectMany(character => new[]
            {
                Key(0, character, 4), Key(0, character, 6)
            }).ToArray());
            return;
        }
        if (input.Action == "key")
        {
            ushort key = input.Value switch
            {
                "Enter" => 0x0D, "Backspace" => 0x08, "Escape" => 0x1B, "Tab" => 0x09,
                "Left" => 0x25, "Up" => 0x26, "Right" => 0x27, "Down" => 0x28,
                _ => throw new IOException("Tecla inválida.")
            };
            uint extended = key is >= 0x25 and <= 0x28 ? 1u : 0u;
            Send([Key(key, 0, extended), Key(key, 0, extended | 2)]);
            return;
        }

        IntPtr awareness = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            if (!SetCursorPos(Coordinate(input.X, GetSystemMetrics(0)), Coordinate(input.Y, GetSystemMetrics(1))))
                throw new IOException("Não foi possível mover o ponteiro.");
        }
        finally { if (awareness != IntPtr.Zero) SetThreadDpiAwarenessContext(awareness); }
        if (input.Action == "move") return;
        Send(input.Action switch
        {
            "click" => [Mouse(0x0002), Mouse(0x0004)],
            "double" => [Mouse(0x0002), Mouse(0x0004), Mouse(0x0002), Mouse(0x0004)],
            "right" => [Mouse(0x0008), Mouse(0x0010)],
            "down" => [Mouse(0x0002)],
            "up" => [Mouse(0x0004)],
            _ => throw new IOException("Comando inválido.")
        });
    }

    public void Release() => SendInput(1, [Mouse(0x0004)], Marshal.SizeOf<NativeInput>());

    internal static int Coordinate(int normalized, int dimension) =>
        (int)((long)normalized * Math.Max(0, dimension - 1) / 65535);
    private static int Normalize(int pixels, int dimension) => (int)(pixels * 65535L / Math.Max(1, dimension - 1));
    private static NativeInput Mouse(uint flags, uint mouseData = 0) => new()
    {
        Data = new NativeUnion { Mouse = new NativeMouse { Flags = flags, MouseData = mouseData } }
    };
    private static NativeInput Key(ushort vk, ushort scan, uint flags) => new()
    {
        Type = 1, Data = new NativeUnion { Keyboard = new NativeKeyboard { Vk = vk, Scan = scan, Flags = flags } }
    };
    private static void Send(NativeInput[] commands)
    {
        if (SendInput((uint)commands.Length, commands, Marshal.SizeOf<NativeInput>()) != commands.Length)
            throw new IOException("O Windows não aceitou o comando de entrada.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public NativeUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct NativeUnion
    {
        [FieldOffset(0)] public NativeMouse Mouse;
        [FieldOffset(0)] public NativeKeyboard Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMouse
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeKeyboard
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder name, int bytes, out int needed);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int dx, int dy, int dw, int dh, IntPtr source, int sx, int sy, int sw, int sh, uint operation);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, NativeInput[] commands, int size);
}
