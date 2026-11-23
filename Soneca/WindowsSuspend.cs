using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Soneca;

internal static class WindowsSuspend
{
    internal const string Argument = "--suspend-now";

    public static bool Run()
    {
        const uint TokenAdjustPrivileges = 0x20;
        const uint TokenQuery = 0x08;
        const uint PrivilegeEnabled = 0x02;
        if (!OpenProcessToken(new IntPtr(-1), TokenAdjustPrivileges | TokenQuery, out var token)) return false;
        using (token)
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid)) return false;
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = PrivilegeEnabled };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
                || Marshal.GetLastPInvokeError() != 0) return false;
            return SetSuspendState(0, 0, 0) != 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disableAll,
        ref TokenPrivileges privileges, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern byte SetSuspendState(byte hibernate, byte force, byte disableWakeEvents);
}
