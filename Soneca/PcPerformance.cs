using System.Diagnostics;
using System.IO;
using System.Net;
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
internal readonly record struct NetworkCounters(string InterfaceId, long Received, long Sent)
{
    public NetworkRate? RateSince(NetworkCounters before, double seconds) =>
        InterfaceId == before.InterfaceId && seconds > 0 && Received >= before.Received && Sent >= before.Sent
            ? new((Received - before.Received) / seconds, (Sent - before.Sent) / seconds)
            : null;
}

internal sealed class PcPerformanceSampler
{
    // Look up the outbound interface in the local routing table without sending a request.
    private static readonly uint RouteLookupAddress = BitConverter.ToUInt32(IPAddress.Parse("192.0.2.1").GetAddressBytes());
    private static readonly Lazy<string?> ProcessorName = new(ReadProcessorName);
    private static readonly Lazy<int> ProcessorGroups = new(() => GetActiveProcessorGroupCount());
    private CpuTimes? lastCpu;
    private NetworkCounters? lastNetwork;
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
        var (network, traffic) = ReadNetwork();
        double? usage = consecutive && lastCpu is { } before ? cpu?.UsageSince(before) : null;
        NetworkRate? rates = consecutive && lastNetwork is { } previousTraffic
            ? traffic?.RateSince(previousTraffic, seconds) : null;
        if (lastDiskSample == 0 || Stopwatch.GetElapsedTime(lastDiskSample, now) >= TimeSpan.FromSeconds(30))
        {
            disk = ReadSystemDisk();
            lastDiskSample = now;
        }
        lastCpu = cpu;
        lastNetwork = traffic;
        lastSample = now;
        return new(computer, ProcessorName.Value, usage, ProcessorGroups.Value, disk, network, rates);
    }

    private static (string Label, NetworkCounters? Traffic) ReadNetwork()
    {
        try
        {
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(adapter => (Adapter: adapter, Properties: adapter.GetIPProperties())).ToArray();
            NetworkInterface? primary = GetBestInterface(RouteLookupAddress, out int index) == 0
                ? active.FirstOrDefault(item => item.Adapter.Supports(NetworkInterfaceComponent.IPv4) &&
                    item.Properties.GetIPv4Properties()?.Index == index).Adapter : null;
            primary ??= active.FirstOrDefault(item => item.Properties.GatewayAddresses.Count > 0).Adapter;
            primary ??= active.Length == 1 ? active[0].Adapter : null;
            if (primary is null) return ("Sem interface principal", null);
            var stats = primary.GetIPStatistics();
            return (primary.Name, new(primary.Id, stats.BytesReceived, stats.BytesSent));
        }
        catch (NetworkInformationException) { return ("Tráfego indisponível", null); }
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
    [DllImport("iphlpapi.dll")]
    private static extern uint GetBestInterface(uint destination, out int interfaceIndex);
}
