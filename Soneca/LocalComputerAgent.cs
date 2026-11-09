using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Soneca;

public sealed record LocalComputerSnapshot(string Name, string WindowsVersion, int ProcessorCount,
    double MemoryInUseGb, double TotalMemoryGb, TimeSpan Uptime, bool NetworkAvailable);

public sealed class LocalComputerAgent : ILocalWindowsAgent
{
    public LocalComputerSnapshot ReadSnapshot()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool hasMemory = NativeMemory.GlobalMemoryStatusEx(ref memory);
        double divisor = 1024d * 1024 * 1024;
        return new LocalComputerSnapshot(Environment.MachineName, Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            hasMemory ? (memory.TotalPhysical - memory.AvailablePhysical) / divisor : 0,
            hasMemory ? memory.TotalPhysical / divisor : 0,
            TimeSpan.FromMilliseconds(Environment.TickCount64), NetworkInterface.GetIsNetworkAvailable());
    }

    public static LocalComputerSnapshot Read() => new LocalComputerAgent().ReadSnapshot();
}

internal struct MemoryStatus
{
    public uint Length, MemoryLoad;
    public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
}

internal static class NativeMemory
{
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GlobalMemoryStatusEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
