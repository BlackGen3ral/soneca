using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;

namespace Soneca;

internal sealed record PcCard(Guid Id, string Name, string Address, bool Online, DateTimeOffset? LastContact,
    WeeklyRule? Wake, WeeklyRule? Shutdown, string? LastRoutine, string PowerStatus = "checking");

internal enum PcResponse { Authenticated, AddressOnly, Unreachable, OutsideNetwork }

internal static class PcDeviceService
{
    private static readonly SemaphoreSlim PollGate = new(1, 1);
    private static readonly Dictionary<Guid, DateTimeOffset> Current = [];
    private sealed record Observation(string Token, string State, int Misses);
    private static readonly Dictionary<Guid, Observation> Observations = [];

    public static async Task<PairStatus> PairAsync(string code)
    {
        var invite = PcAgent.ParseInvite(code);
        EnsureSlot(PcStore.Read(), invite.Id, invite.Address);
        var pc = await PcAgent.PairAsync(invite).ConfigureAwait(false);
        PcStore.Update(state =>
        {
            EnsureSlot(state, pc.Id, pc.Address);
            int i = state.Computers.FindIndex(entry => entry.Id == pc.Id);
            if (i < 0) state.Computers.Add(pc);
            else
            {
                var old = state.Computers[i];
                state.Computers[i] = pc with
                {
                    Wake = old.Wake, Shutdown = old.Shutdown, LastRoutine = old.LastRoutine,
                    LastWakeSlot = old.LastWakeSlot, LastShutdownSlot = old.LastShutdownSlot
                };
            }
            return true;
        });
        return new PairStatus(pc.Id, pc.Name);
    }

    private static void EnsureSlot(PcDeviceState state, Guid id, string address)
    {
        if (state.Computers.Any(pc => pc.Address == address && pc.Id != id))
            throw new InvalidOperationException("Outro PC já está salvo neste endereço. Remova o pareamento antigo primeiro.");
        if (state.Computers.Count >= RemoteComputerConfiguration.MaximumComputers && state.Computers.All(pc => pc.Id != id))
            throw new InvalidOperationException("Você já pareou três PCs.");
    }

    public static async Task<PcCard[]> ListAsync(bool refresh = true)
    {
        await PollGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var pcs = PcStore.Read().Computers;
            var now = DateTimeOffset.Now;
            if (refresh && pcs.Count > 0)
            {
                var results = await Task.WhenAll(pcs.Select(Probe)).ConfigureAwait(false);
                for (int i = 0; i < pcs.Count; i++)
                {
                    var pc = pcs[i];
                    var response = results[i];
                    if (response == PcResponse.Authenticated) Current[pc.Id] = DateTimeOffset.Now;
                    else Current.Remove(pc.Id);
                    var prior = Observations.GetValueOrDefault(pc.Id);
                    int misses = prior?.Token == pc.Token ? prior.Misses : 0;
                    var next = PowerStatus(response, misses);
                    Observations[pc.Id] = new Observation(pc.Token, next.State, next.Misses);
                }
                pcs = PcStore.Update(state =>
                {
                    for (int i = 0; i < state.Computers.Count; i++)
                    {
                        var pc = state.Computers[i];
                        if (Current.GetValueOrDefault(pc.Id) is var contact && contact >= now &&
                            pcs.Any(sample => sample.Id == pc.Id && sample.Token == pc.Token) &&
                            (pc.LastContact is null || contact > pc.LastContact))
                            state.Computers[i] = pc with { LastContact = contact };
                    }
                    return state.Computers;
                });
            }
            foreach (Guid id in Current.Keys.Where(id => pcs.All(pc => pc.Id != id)).ToArray()) Current.Remove(id);
            foreach (Guid id in Observations.Keys.Where(id => pcs.All(pc => pc.Id != id)).ToArray()) Observations.Remove(id);
            return pcs.Select(pc => {
                var sample = Observations.GetValueOrDefault(pc.Id);
                string state = sample?.Token == pc.Token ? sample.State : "checking";
                return new PcCard(pc.Id, pc.Name, pc.Address, state == "on", pc.LastContact, pc.Wake, pc.Shutdown, pc.LastRoutine, state);
            }).ToArray();
        }
        finally { PollGate.Release(); }
    }

    internal static (string State, int Misses) PowerStatus(PcResponse response, int misses) => response switch
    {
        PcResponse.Authenticated => ("on", 0),
        PcResponse.AddressOnly => ("address_only", 0),
        PcResponse.OutsideNetwork => ("outside_network", 0),
        _ => (misses >= 1 ? "unreachable" : "checking", Math.Min(2, misses + 1))
    };

    private static async Task<PcResponse> Probe(PairedPc pc)
    {
        var address = IPAddress.Parse(pc.Address);
        try { PcLan.For(address); }
        catch (InvalidOperationException) { return PcResponse.OutsideNetwork; }
        try { await PcAgent.CheckAsync(pc).ConfigureAwait(false); return PcResponse.Authenticated; }
        catch (Exception) { }
        try
        {
            using var ping = new Ping();
            if ((await ping.SendPingAsync(address, 700).ConfigureAwait(false)).Status == IPStatus.Success)
                return PcResponse.AddressOnly;
        }
        catch (PingException) { }
        return PcResponse.Unreachable;
    }

    public static Task WakeAsync(Guid id) => PcLan.WakeAsync(PcStore.Read().Computers.FirstOrDefault(pc => pc.Id == id)
        ?? throw new ArgumentException("PC não pareado."));

    public static void Save(Guid id, WeeklyRule? wake, WeeklyRule? shutdown)
    {
        if (wake is not null && shutdown is not null && wake.Time == shutdown.Time && (wake.Days & shutdown.Days) != 0)
            throw new ArgumentException("Escolha horários diferentes para ligar e desligar nos mesmos dias.");
        PcStore.Update(state =>
        {
            int index = state.Computers.FindIndex(pc => pc.Id == id);
            if (index < 0) throw new ArgumentException("PC não pareado.");
            state.Computers[index] = state.Computers[index] with { Wake = wake, Shutdown = shutdown };
            return true;
        }, syncSchedules: true);
    }

    public static void Remove(Guid id) => PcStore.Update(state => state.Computers.RemoveAll(pc => pc.Id == id), syncSchedules: true);

    public static async Task RunAsync(DateTime local)
    {
        string slot = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var due = PcStore.Update(state =>
        {
            var commands = new List<(PairedPc, bool)>();
            for (int i = 0; i < state.Computers.Count; i++)
            {
                var pc = state.Computers[i];
                if (pc.Wake?.IsDue(local) == true && pc.LastWakeSlot != slot)
                {
                    commands.Add((pc, true));
                    pc = pc with { LastWakeSlot = slot };
                }
                if (pc.Shutdown?.IsDue(local) == true && pc.LastShutdownSlot != slot)
                {
                    commands.Add((pc, false));
                    pc = pc with { LastShutdownSlot = slot };
                }
                state.Computers[i] = pc;
            }
            WeeklyRule? localRule = state.LocalShutdown;
            bool localDue = localRule?.IsDue(local) == true && state.LastLocalShutdownSlot != slot;
            if (localDue) state.LastLocalShutdownSlot = slot;
            return (commands, localDue ? localRule : null);
        });

        await Task.WhenAll(due.commands.Select(async command =>
        {
            var (pc, wake) = command;
            var current = PcStore.Read().Computers.FirstOrDefault(entry => entry.Id == pc.Id);
            if (current is null || current.Token != pc.Token || (wake ? current.Wake != pc.Wake : current.Shutdown != pc.Shutdown))
                return;
            string result;
            try
            {
                if (wake) await PcLan.WakeAsync(pc).ConfigureAwait(false);
                else await PcAgent.ShutdownAsync(pc).ConfigureAwait(false);
                result = wake ? "Sinal para ligar enviado" : "Desligamento aceito pelo PC";
            }
            catch (Exception) { result = wake ? "Falha ao enviar sinal para ligar" : "PC não aceitou o desligamento"; }
            PcStore.Update(state =>
            {
                int index = state.Computers.FindIndex(entry => entry.Id == pc.Id && entry.Token == pc.Token);
                if (index >= 0) state.Computers[index] = state.Computers[index] with
                    { LastRoutine = $"{local:dd/MM HH:mm} · {result}" };
                return true;
            });
        })).ConfigureAwait(false);
        if (due.Item2 is { } localShutdown && PcStore.Read().LocalShutdown == localShutdown &&
            DateTime.Now - local < TimeSpan.FromMinutes(1))
            LocalPower.Run(PowerCommand.Shutdown);
    }

    internal sealed record PairStatus(Guid Id, string Name);
}
