using System.IO;

namespace Soneca;
public static class CleanerTests
{
    public static int Run()
    {
        int checks = 0;
        void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        string fixture = Path.Combine(AppContext.BaseDirectory, "cleaner-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            string root = Path.Combine(fixture, "Temp"); Directory.CreateDirectory(root);
            string outside = Path.Combine(fixture, "keep.txt"); File.WriteAllText(outside, "preserve");
            string normal = Path.Combine(root, "normal.tmp"); File.WriteAllText(normal, "remove");
            string locked = Path.Combine(root, "locked.tmp"); File.WriteAllText(locked, "locked");
            string changed = Path.Combine(root, "changed.tmp"); File.WriteAllText(changed, "before");
            Directory.CreateDirectory(Path.Combine(root, "nested")); File.WriteAllText(Path.Combine(root, "nested", "nested.tmp"), "nested");
            using var lockedStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var plan = Cleaner.Scan(root, CleanupKind.Temp);
            Assert(!plan.Files.Any(file => file.Path == locked) && plan.Ignored > 0, "Arquivo em uso entrou na prévia.");
            File.WriteAllText(changed, "after with a different size");
            plan.Files.Add(new(outside, new FileInfo(outside).Length, File.GetLastWriteTimeUtc(outside).ToFileTimeUtc()));
            var result = Cleaner.Execute(plan);
            Assert(result.Deleted == 2, "O limpador deve remover somente os dois arquivos elegíveis.");
            Assert(!File.Exists(normal), "Arquivo elegível não foi removido.");
            Assert(File.Exists(locked), "Arquivo em uso foi removido.");
            Assert(File.Exists(changed), "Arquivo alterado após a análise foi removido.");
            Assert(File.Exists(outside), "Arquivo fora do destino foi removido.");
            Assert(Directory.Exists(root), "A pasta raiz deve ser preservada.");
            string pref = Path.Combine(fixture, "Prefetch"); Directory.CreateDirectory(pref);
            File.WriteAllText(Path.Combine(pref, "APP.pf"), "cache"); File.WriteAllText(Path.Combine(pref, "keep.db"), "keep");
            Directory.CreateDirectory(Path.Combine(pref, "ReadyBoot")); File.WriteAllText(Path.Combine(pref, "ReadyBoot", "keep.pf"), "keep");
            var prefPlan = Cleaner.Scan(pref, CleanupKind.Prefetch); Assert(prefPlan.Files.Count == 1, "Prefetch deve limitar-se a .pf na raiz.");
            var prefResult = Cleaner.Execute(prefPlan); Assert(prefResult.Deleted == 1 && File.Exists(Path.Combine(pref, "keep.db")) && File.Exists(Path.Combine(pref, "ReadyBoot", "keep.pf")), "Escopo do Prefetch incorreto.");
            try { Cleaner.ValidateTempRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); throw new Exception("Destino pessoal aceito."); } catch (IOException) { checks++; }
            // Directory junctions work without Developer Mode and must never be traversed.
            string link = Path.Combine(root, "linked");
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{link}\" \"{pref}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            { process!.WaitForExit(); if (process.ExitCode != 0) throw new Exception("Não foi possível preparar o teste de junction."); }
            var linkPlan = Cleaner.Scan(root, CleanupKind.Temp);
            Assert(!linkPlan.Files.Any(f => f.Path.Contains("linked")) && linkPlan.Ignored > 0, "Junction deve ser ignorada.");
            Directory.Delete(link);
            string hardLink = Path.Combine(root, "linked-file.tmp");
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /H \"{hardLink}\" \"{outside}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            {
                process!.WaitForExit();
                if (process.ExitCode != 0) throw new Exception("Não foi possível preparar o teste de link de arquivo.");
            }
            Assert(Cleaner.Scan(root, CleanupKind.Temp).Files.All(file => file.Path != hardLink) && File.Exists(outside),
                "O link para um arquivo fora do destino entrou na prévia.");

            string ageRoot = Path.Combine(fixture, "age", "Temp");
            Directory.CreateDirectory(ageRoot);
            string old = Path.Combine(ageRoot, "old.tmp");
            string recent = Path.Combine(ageRoot, "recent.tmp");
            string swapped = Path.Combine(ageRoot, "swapped.tmp");
            File.WriteAllText(old, "old");
            File.WriteAllText(recent, "recent");
            File.WriteAllText(swapped, "first");
            var originalTime = DateTime.UtcNow.AddDays(-9);
            File.SetLastWriteTimeUtc(old, originalTime);
            File.SetLastWriteTimeUtc(swapped, originalTime);
            var agePlan = Cleaner.Scan(ageRoot, CleanupKind.Temp, DateTime.UtcNow.AddDays(-7));
            Assert(agePlan.Files.Count == 2 && agePlan.Recent == 1, "Filtro por idade não preservou o arquivo recente.");
            File.Move(swapped, Path.Combine(fixture, "original-swapped.tmp"));
            File.WriteAllText(swapped, "other");
            File.SetLastWriteTimeUtc(swapped, originalTime);
            var currentPlan = Cleaner.Execute(agePlan);
            Assert(currentPlan.Deleted == 1 && File.Exists(recent) && File.Exists(swapped), "Um arquivo substituído entrou na remoção.");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var remainingPlan = Cleaner.Scan(ageRoot, CleanupKind.Temp);
            Assert(Cleaner.Execute(remainingPlan, cancelled.Token).Cancelled && File.Exists(recent),
                "A limpeza cancelada removeu um item.");
            try { Cleaner.Scan(ageRoot, CleanupKind.Temp, cancellation: cancelled.Token); throw new Exception("Análise ignorou o cancelamento."); }
            catch (OperationCanceledException) { checks++; }

            string dumps = Path.Combine(fixture, "CrashDumps");
            Directory.CreateDirectory(Path.Combine(dumps, "nested"));
            string dump = Path.Combine(dumps, "example.dmp");
            string nestedDump = Path.Combine(dumps, "nested", "example.dmp");
            string log = Path.Combine(dumps, "keep.log");
            File.WriteAllText(dump, "example");
            File.WriteAllText(nestedDump, "keep");
            File.WriteAllText(log, "keep");
            var dumpPlan = Cleaner.Scan(dumps, CleanupKind.CrashDumps);
            Assert(dumpPlan.Files.Count == 1 && Cleaner.Execute(dumpPlan).Deleted == 1 &&
                File.Exists(nestedDump) && File.Exists(log), "A limpeza de dumps ultrapassou o escopo.");
            Assert(Cleaner.PrefetchHelper("invalid") == 1, "Nome de canal inválido deve ser recusado.");
            string pipeName = "ArthoControl-" + Guid.NewGuid().ToString("N");
            using (var pipe = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var helper = Task.Run(() => Cleaner.PrefetchHelper(pipeName));
                pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
                // No real Prefetch filenames: verify transport and reject traversal without deleting user data.
                using (var writer = new StreamWriter(pipe, leaveOpen: true))
                {
                    writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(new PrefetchRequest(false, [new("../outside.pf", 0, 0), new("C:\\outside.pf", 0, 0)])));
                    writer.Flush();
                }
                using var reader = new StreamReader(pipe);
                var response = reader.ReadToEndAsync(timeout.Token).GetAwaiter().GetResult();
                var resultIpc = System.Text.Json.JsonSerializer.Deserialize<PrefetchReply>(response)?.Result;
                Assert(helper.GetAwaiter().GetResult() == 0 && resultIpc is { Deleted: 0, Skipped: 2 },
                    "O auxiliar deve ignorar caminhos externos e retornar pelo canal.");
            }
            Assert(Cleaner.BinMatches(new BinInfo(2, 10), new BinInfo(2, 10)), "Prévia da Lixeira idêntica recusada.");
            Assert(!Cleaner.BinMatches(new BinInfo(2, 10), new BinInfo(3, 10)), "Mudança na Lixeira não detectada.");
            Assert(!Cleaner.BinMatches(new BinInfo(2, 10), new BinInfo(2, 11)), "Mudança no volume da Lixeira não detectada.");
            return checks;
        }
        finally
        {
            // Delete only the verified isolated test directory under the executable folder.
            string resolved = Path.GetFullPath(fixture);
            if (resolved.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("cleaner-test-")) Directory.Delete(resolved, true);
        }
    }
}
