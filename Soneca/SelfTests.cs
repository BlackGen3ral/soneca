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
            Assert(Updater.ExpectedHash(new string('a', 64) + "  Soneca-Setup.exe\n") == new string('a', 64));
            Assert(Updater.ExpectedHash(new string('b', 64) + "  ArthoAI-Setup.exe\n") == new string('b', 64));
            var remote = new RemoteComputerConfiguration(new[] { new ComputerNode("PC 1", "pc1", null, 1), new ComputerNode("PC 2", "pc2", null, 2), new ComputerNode("PC 3", "pc3", null, 3) });
            Assert(remote.Computers.Count == RemoteComputerConfiguration.MaximumComputers);
            try { new RemoteComputerConfiguration(remote.Computers.Append(new ComputerNode("PC 4", "pc4", null, 4))); throw new Exception("Limite de PCs ignorado"); } catch (ArgumentOutOfRangeException) { count++; }
            Assert(StartupRegistration.ResolvePreference(null));
            Assert(!StartupRegistration.ResolvePreference(false));
            try { Updater.ExpectedHash("bad  Soneca-Setup.exe"); throw new Exception("Hash inválido aceito"); } catch (Exception ex) when (ex.Message.StartsWith("Verificação")) { count++; }
            Schedule.IntegrationTest(); count++;
            count += CleanerTests.Run();
            count += FolderNamesTests.Run();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), $"PASS: {count} checks; harmless scheduled task created and removed; no shutdown executed.");
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), ex.ToString()); return 1; }
    }
}
