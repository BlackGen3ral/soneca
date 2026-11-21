using System.Net;

namespace Soneca;

internal static class PcDeviceTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("Falha no teste de rotina de PC " + checks);
        }

        var wake = WeeklyRule.Create("08:15", 126)!;
        var stop = WeeklyRule.Create("18:30", 62)!;
        var sunday = new DateTime(2030, 1, 6, 8, 15, 0);
        Check(sunday.DayOfWeek == DayOfWeek.Sunday);
        Check(!wake.IsDue(sunday));
        Check(wake.IsDue(sunday.AddDays(1).AddSeconds(30)));
        Check(!wake.IsDue(sunday.AddDays(1).AddMinutes(1)));
        Check(PcRoutineSchedule.NextOccurrence(wake, sunday) == sunday.AddDays(1));
        Check(!stop.Includes(DayOfWeek.Saturday) && wake.Includes(DayOfWeek.Saturday));
        Check(LocalPower.Arguments(PowerCommand.Shutdown) == "/s /t 0");
        Check(LocalPower.Arguments(PowerCommand.Restart) == "/r /t 0");
        Check(WeeklyRule.Create("", 0) is null);
        try { WeeklyRule.Create("24:00", 127); Check(false); } catch (ArgumentException) { checks++; }
        try { WeeklyRule.Create("08:00", 0); Check(false); } catch (ArgumentException) { checks++; }
        Check(PcLan.Broadcast(IPAddress.Parse("192.168.4.121"), 26).ToString() == "192.168.4.127");
        var packet = PcLan.MagicPacket("021122334455");
        Check(packet.Length == 102 && packet.Take(6).All(value => value == 255));
        Check(packet.Skip(6).Chunk(6).All(copy => copy.SequenceEqual(Convert.FromHexString("021122334455"))));
        try { PcLan.MagicPacket("FFFFFFFFFFFF"); Check(false); } catch (ArgumentException) { checks++; }
        var testDevice = new PairedPc(Guid.NewGuid(), "Teste", "192.168.4.121", "021122334455", "", "", Wake: wake);
        PcRoutineSchedule.IntegrationTest(testDevice);
        return checks + 1;
    }
}
