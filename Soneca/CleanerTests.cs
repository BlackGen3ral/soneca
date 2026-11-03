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
            var plan = Cleaner.Scan(root, false);
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
            var prefPlan = Cleaner.Scan(pref, true); Assert(prefPlan.Files.Count == 1, "Prefetch deve limitar-se a .pf na raiz.");
            var prefResult = Cleaner.Execute(prefPlan); Assert(prefResult.Deleted == 1 && File.Exists(Path.Combine(pref, "keep.db")) && File.Exists(Path.Combine(pref, "ReadyBoot", "keep.pf")), "Escopo do Prefetch incorreto.");
            try { Cleaner.ValidateTempRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); throw new Exception("Destino pessoal aceito."); } catch (IOException) { checks++; }
            // Directory junctions work without Developer Mode and must never be traversed.
            string link = Path.Combine(root, "linked");
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{link}\" \"{pref}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            { process!.WaitForExit(); if (process.ExitCode != 0) throw new Exception("Não foi possível preparar o teste de junction."); }
            var linkPlan = Cleaner.Scan(root, false);
            Assert(!linkPlan.Files.Any(f => f.Path.Contains("linked")) && linkPlan.Skipped > 0, "Junction deve ser ignorada.");
            Directory.Delete(link);
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
                var resultIpc = System.Text.Json.JsonSerializer.Deserialize<CleanResult>(response);
                Assert(helper.GetAwaiter().GetResult() == 0 && resultIpc?.Deleted == 0, "O auxiliar deve ignorar caminhos externos e retornar pelo canal.");
            }
            Assert(Cleaner.QueryBin().Error is null, "Falha na análise somente leitura da lixeira.");
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
