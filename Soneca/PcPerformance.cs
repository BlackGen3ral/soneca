using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Soneca;

internal sealed record PcPerformance(LocalComputerSnapshot Computer, string? ProcessorName, double? ProcessorLoad,
    int ProcessorGroups, DiskUsage? Disk, string Network, NetworkRate? Traffic);

internal sealed record DiskUsage(string Volume, long Total, long Free)
{
    public long Used => Total - Free;
}

internal readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User)
{
    public double? UsageSince(CpuTimes before)
    {
        if (Idle < before.Idle || Kernel < before.Kernel || User < before.User) return null;
        double total = (Kernel - before.Kernel) + (double)(User - before.User);
        double idle = Idle - before.Idle;
        return total > 0 && idle <= total ? 100 * (total - idle) / total : null;
    }
}

internal readonly record struct NetworkRate(double Received, double Sent);
internal readonly record struct NetworkBytes(long Received, long Sent)
{
    public NetworkRate? RateSince(NetworkBytes before, double seconds) =>
        seconds > 0 && Received >= before.Received && Sent >= before.Sent
            ? new((Received - before.Received) / seconds, (Sent - before.Sent) / seconds)
            : null;
}

internal sealed class PcPerformanceSampler
{
    private static readonly Lazy<string?> ProcessorName = new(ReadProcessorName);
    private static readonly Lazy<int> ProcessorGroups = new(() => GetActiveProcessorGroupCount());
    private CpuTimes? lastCpu;
    private Dictionary<string, NetworkBytes> lastNetwork = new();
    private long lastSample;
    private long lastDiskSample;
    private DiskUsage? disk;

    public PcPerformance Read()
    {
        long now = Stopwatch.GetTimestamp();
        double seconds = lastSample == 0 ? 0 : Stopwatch.GetElapsedTime(lastSample, now).TotalSeconds;
        bool consecutive = seconds is > 0 and <= 10;
        CpuTimes? cpu = GetSystemTimes(out var idle, out var kernel, out var user) ? new(idle, kernel, user) : null;
        var computer = LocalComputerAgent.Read();
        var (network, interfaces) = ReadNetwork();
        double? usage = consecutive && lastCpu is { } before ? cpu?.UsageSince(before) : null;
        NetworkRate? rates = consecutive ? CompareNetwork(lastNetwork, interfaces, seconds) : null;
        if (lastDiskSample == 0 || Stopwatch.GetElapsedTime(lastDiskSample, now) >= TimeSpan.FromSeconds(30))
        {
            disk = ReadSystemDisk();
            lastDiskSample = now;
        }
        lastCpu = cpu;
        lastNetwork = interfaces;
        lastSample = now;
        return new(computer, ProcessorName.Value, usage, ProcessorGroups.Value, disk, network, rates);
    }

    internal static NetworkRate? CompareNetwork(IReadOnlyDictionary<string, NetworkBytes> before,
        IReadOnlyDictionary<string, NetworkBytes> current, double seconds)
    {
        if (current.Count == 0 || current.Count != before.Count) return null;
        double received = 0, sent = 0;
        foreach (var (id, counters) in current)
        {
            if (!before.TryGetValue(id, out var old) || counters.RateSince(old, seconds) is not { } rate)
                return null;
            received += rate.Received;
            sent += rate.Sent;
        }
        return new(received, sent);
    }

    private static (string Label, Dictionary<string, NetworkBytes> Counters) ReadNetwork()
    {
        var counters = new Dictionary<string, NetworkBytes>(StringComparer.Ordinal);
        try
        {
            var active = NetworkInterface.GetAllNetworkInterfaces().Where(adapter =>
                adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType is
                NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT).ToArray();
            if (active.Length == 0) return ("Ethernet e Wi-Fi desconectados", counters);
            foreach (var adapter in active)
            {
                var stats = adapter.GetIPStatistics();
                counters.Add(adapter.Id, new(stats.BytesReceived, stats.BytesSent));
            }
            string label = active.Length == 1
                ? (active[0].NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet")
                : $"{active.Length} interfaces Ethernet e Wi-Fi";
            return (label, counters);
        }
        catch (NetworkInformationException) { return ("Tráfego indisponível", new()); }
    }

    private static DiskUsage? ReadSystemDisk()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            long total = drive.TotalSize;
            long free = drive.TotalFreeSpace;
            return total > 0 && free >= 0 && free <= total ? new(drive.Name.TrimEnd('\\'), total, free) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string? ReadProcessorName()
    {
        try
        {
            using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (cpu?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return null; }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")]
    private static extern ushort GetActiveProcessorGroupCount();
}
