using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Soneca;

internal sealed record WeeklyRule(string Time, int Days)
{
    public TimeOnly Clock => TimeOnly.ParseExact(Time, "HH:mm", CultureInfo.InvariantCulture);

    public static WeeklyRule? Create(string? time, int days)
    {
        if (time is null or "") return null;
        if (days is < 1 or > 127 ||
            !TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Informe um horário de 24 horas (HH:mm) e marque pelo menos um dia.");
        return new WeeklyRule(time, days);
    }

    public bool Includes(DayOfWeek day) => (Days & (1 << (int)day)) != 0;
    public bool IsDue(DateTime local) => Includes(local.DayOfWeek) && Clock.Hour == local.Hour && Clock.Minute == local.Minute;
}

internal sealed record PairedPc(Guid Id, string Name, string Address, string Mac, string Pin, string Token,
    DateTimeOffset? LastContact = null, WeeklyRule? Wake = null, WeeklyRule? Shutdown = null,
    string? LastRoutine = null, string? LastWakeSlot = null, string? LastShutdownSlot = null);

internal sealed record PcAgentSettings(Guid Id, string Address, byte[] Certificate, string? ControllerToken, bool Enabled);
internal sealed record PcInvite(Guid Id, string Address, string Pin, string Token);
internal sealed record PcAccepted(Guid Id, string Name, string Mac, string Token);

internal sealed class PcDeviceState
{
    public List<PairedPc> Computers { get; init; } = [];
    public PcAgentSettings? Agent { get; set; }
    public MobileProfile? Mobile { get; set; }
    public WeeklyRule? LocalShutdown { get; set; }
    public string? LastLocalShutdownSlot { get; set; }
}

internal static class PcStore
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArthoAI", "pc-devices.dat");

    public static PcDeviceState Read() => Locked(Load);

    public static T Update<T>(Func<PcDeviceState, T> edit, bool syncSchedules = false) => Locked(() =>
    {
        var state = Load();
        var previous = state.Computers.ToArray();
        var previousLocal = state.LocalShutdown;
        T result = edit(state);
        if (syncSchedules) PcRoutineSchedule.Register(state.Computers, localShutdown: state.LocalShutdown);
        try { Save(state); }
        catch
        {
            if (syncSchedules) PcRoutineSchedule.Register(previous, localShutdown: previousLocal);
            throw;
        }
        return result;
    });

    private static T Locked<T>(Func<T> action)
    {
        using var gate = new Mutex(false, "Local\\ArthoAI-PcDevices-v1");
        try
        {
            try
            {
                if (!gate.WaitOne(TimeSpan.FromSeconds(8))) throw new IOException("As configurações estão em uso. Tente novamente.");
            }
            catch (AbandonedMutexException) { }
            try { return action(); }
            finally { gate.ReleaseMutex(); }
        }
        catch (CryptographicException ex) { throw new IOException("Não foi possível abrir as configurações de PCs desta conta.", ex); }
    }

    private static PcDeviceState Load()
    {
        if (!File.Exists(PathName)) return new PcDeviceState();
        var raw = ProtectedData.Unprotect(File.ReadAllBytes(PathName), null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<PcDeviceState>(raw) ?? throw new IOException("Configurações inválidas."); }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    private static void Save(PcDeviceState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var raw = JsonSerializer.SerializeToUtf8Bytes(state);
        string temporary = PathName + ".new";
        try
        {
            File.WriteAllBytes(temporary, ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser));
            File.Move(temporary, PathName, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal sealed record LanAdapter(IPAddress Address, string Mac, int Prefix);

internal static class PcLan
{
    public static LanAdapter[] Adapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
            .Where(entry => IsPrivate(entry.Address) && entry.PrefixLength is >= 1 and <= 32)
            .Select(entry => new LanAdapter(entry.Address, adapter.GetPhysicalAddress().ToString(), entry.PrefixLength)))
        .DistinctBy(adapter => adapter.Address).ToArray();

    public static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168));
    }

    public static LanAdapter For(IPAddress target)
    {
        if (!IsPrivate(target)) throw new ArgumentException("Selecione um PC da rede local.");
        return Adapters().Where(adapter => adapter.Prefix <= 30 && !target.Equals(adapter.Address)
            && Network(adapter.Address, adapter.Prefix) == Network(target, adapter.Prefix))
            .OrderByDescending(adapter => adapter.Prefix).FirstOrDefault()
            ?? throw new InvalidOperationException("Conecte o computador de controle à mesma rede do PC pareado.");
    }

    private static uint Number(IPAddress address) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
    private static uint Mask(int prefix) => uint.MaxValue << (32 - prefix);
    private static uint Network(IPAddress address, int prefix) => Number(address) & Mask(prefix);
    public static bool SameSubnet(LanAdapter local, IPAddress other) =>
        IsPrivate(other) && Network(local.Address, local.Prefix) == Network(other, local.Prefix);
    internal static IPAddress Broadcast(IPAddress address, int prefix)
    {
        var bytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, Number(address) | ~Mask(prefix));
        return new IPAddress(bytes);
    }

    public static byte[] MagicPacket(string mac)
    {
        if (mac.Length != 12 || !mac.All(Uri.IsHexDigit)) throw new ArgumentException("Endereço MAC inválido.");
        var address = Convert.FromHexString(mac);
        if ((address[0] & 1) != 0 || address.All(octet => octet == 0))
            throw new ArgumentException("Endereço MAC inválido.");
        var packet = new byte[102];
        packet.AsSpan(0, 6).Fill(0xff);
        for (int repetition = 0; repetition < 16; repetition++) address.CopyTo(packet, 6 + repetition * 6);
        return packet;
    }

    public static async Task WakeAsync(PairedPc pc)
    {
        var route = For(IPAddress.Parse(pc.Address));
        using var socket = new UdpClient(new IPEndPoint(route.Address, 0)) { EnableBroadcast = true };
        await socket.SendAsync(MagicPacket(pc.Mac), new IPEndPoint(Broadcast(route.Address, route.Prefix), 9));
    }
}
