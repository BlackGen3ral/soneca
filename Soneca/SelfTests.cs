using System.IO;

namespace Soneca;
public static class SelfTests
{
    public static int Run()
    {
        try
        {
            int count = 0;
            void Assert(bool value) { count++; if (!value) throw new Exception("Falha no teste " + count); }
            var now = DateTimeOffset.Now;
            Assert(TimeRules.After("30", now) == now.AddMinutes(30));
            foreach (string invalid in new[] { "0", "-1", "10081", "abc", "1.5" })
            { try { TimeRules.After(invalid, now); throw new Exception("Entrada inválida aceita"); } catch (ArgumentException) { count++; } }
            Assert(TimeRules.At("23:59", now) > now);
            Assert(TimeRules.At("00:00", now) > now);
            try { TimeRules.At("25:90", now); throw new Exception("Horário inválido aceito"); } catch (ArgumentException) { count++; }
            Assert(TimeRules.Countdown(now.AddHours(25), now) == "25:00:00");
            Assert(TimeRules.Countdown(now.AddSeconds(-1), now) == "00:00:00");
            Assert(TimeRules.Countdown(now.AddMilliseconds(29999), now) == "00:00:30");
            var fake = new Schedule(true); fake.Set(now.AddMinutes(5)); Assert(fake.Read() == now.AddMinutes(5)); fake.Cancel(); Assert(fake.Read() is null);
            Assert(Updater.OfficialAsset(new Uri("https://github.com/BlackGen3ral/soneca/releases/download/v1.0.0/Soneca-Setup.exe")));
            Assert(!Updater.OfficialAsset(new Uri("https://github.com.evil.test/BlackGen3ral/soneca/releases/download/x")));
            Assert(!Updater.OfficialAsset(new Uri("https://github.com/other/soneca/releases/download/x")));
            Assert(!Updater.OfficialAsset(new Uri("https://github.com:444/BlackGen3ral/soneca/releases/download/x")));
            Assert(!Updater.OfficialAsset(new Uri("https://example@github.com/BlackGen3ral/soneca/releases/download/x")));
            Assert(Updater.ExpectedHash(new string('a', 64) + "  Soneca-Setup.exe\n") == new string('a', 64));
            Assert(Updater.ExpectedHash(new string('b', 64) + "  ArthoAI-Setup.exe\n") == new string('b', 64));
            Assert(Updater.ExpectedHash(new string('a', 64) + "  Soneca-Setup.exe\n" + new string('b', 64) +
                "  ArthoAI-Setup.exe\n", "ArthoAI-Setup.exe") == new string('b', 64));
            var remote = new RemoteComputerConfiguration(new[] { new ComputerNode("PC 1", "pc1", null, 1), new ComputerNode("PC 2", "pc2", null, 2), new ComputerNode("PC 3", "pc3", null, 3) });
            Assert(remote.Computers.Count == RemoteComputerConfiguration.MaximumComputers);
            try { new RemoteComputerConfiguration(remote.Computers.Append(new ComputerNode("PC 4", "pc4", null, 4))); throw new Exception("Limite de PCs ignorado"); } catch (ArgumentOutOfRangeException) { count++; }
            Assert(StartupRegistration.ResolvePreference(null));
            Assert(!StartupRegistration.ResolvePreference(false));
            try { Updater.ExpectedHash("bad  Soneca-Setup.exe"); throw new Exception("Hash inválido aceito"); } catch (Exception ex) when (ex.Message.StartsWith("Verificação")) { count++; }
            var today = new DateOnly(2026, 11, 23);
            var plan = PlanReminder.Create("conta@exemplo.com", today.AddDays(7), 7, null);
            Assert(plan.Countdown(today) == "Faltam 7 dias" && plan.Due(today));
            Assert(!plan.Due(today.AddDays(-1)) && !plan.Due(today.AddDays(8)));
            var noticed = plan.AfterNotice(today);
            var renewal = today.AddDays(7);
            Assert(!noticed.Due(today.AddDays(1)) && noticed.Due(renewal));
            Assert(!noticed.AfterNotice(renewal).Due(renewal));
            Assert(PlanReminder.Create(plan.Email, today.AddDays(28), 7, noticed).Due(today.AddDays(21)));
            var withoutDate = PlanReminder.Create(plan.Email, null, 7, noticed);
            Assert(withoutDate.Id == plan.Id && withoutDate.DaysLeft(today) is null);
            Assert(withoutDate.Countdown(today) == "Sem data de renovação" && !withoutDate.Due(today));
            Assert(withoutDate.AfterNotice(today) == withoutDate);
            Assert(PlanReminder.Create(plan.Email, renewal, 7, withoutDate).Due(today));
            var savedPlans = System.Text.Json.JsonSerializer.Serialize(new[] { plan, withoutDate });
            Assert(System.Text.Json.JsonSerializer.Deserialize<PlanReminder[]>(savedPlans)!.SequenceEqual([plan, withoutDate]));
            Assert(PcDeviceService.PowerStatus(PcResponse.Unreachable, 0) == ("checking", 1));
            Assert(PcDeviceService.PowerStatus(PcResponse.Unreachable, 1).State == "unreachable");
            Assert(PcDeviceService.PowerStatus(PcResponse.Authenticated, 2) == ("on", 0));
            Assert(PcDeviceService.PowerStatus(PcResponse.OutsideNetwork, 2) == ("outside_network", 0));
            Schedule.IntegrationTest(); count++;
            Schedule.IntegrationTest(suspend: true); count++;
            count += PcDeviceTests.Run();
            count += RemoteDesktopTests.Run();
            Assert(new CpuTimes(20, 60, 30).UsageSince(new CpuTimes(10, 40, 10)) == 75);
            Assert(new CpuTimes(1, 1, 1).UsageSince(new CpuTimes(2, 2, 2)) is null);
            var networkBefore = new NetworkCounters("rede", 1000, 1000);
            var networkAfter = new NetworkCounters("rede", 5000, 3000);
            Assert(networkAfter.RateSince(networkBefore, 2) == new NetworkRate(2000, 1000));
            Assert(networkBefore.RateSince(networkAfter, 2) is null);
            Assert(networkAfter.RateSince(networkBefore with { InterfaceId = "outra" }, 2) is null);
            count += CleanerTests.Run();
            count += FolderNamesTests.Run();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), $"PASS: {count} checks; harmless scheduled task created and removed; no shutdown executed.");
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), ex.ToString()); return 1; }
    }
}
