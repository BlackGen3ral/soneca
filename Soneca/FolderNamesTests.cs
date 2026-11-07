using System.IO;

namespace Soneca;
public static class FolderNamesTests
{
    public static int Run()
    {
        int checks = 0;
        void Assert(bool condition) { checks++; if (!condition) throw new Exception("Falha na listagem de nomes: " + checks); }
        string root = Path.Combine(AppContext.BaseDirectory, "names-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Árvore completa 123.zip"), "preserve");
            File.WriteAllText(Path.Combine(root, "beta.rar"), "preserve");
            Directory.CreateDirectory(Path.Combine(root, "Pasta.com.pontos"));
            File.WriteAllText(Path.Combine(root, "Pasta.com.pontos", "Arquivo longo.7z"), "preserve");
            var flat = FolderNames.Read(root, false);
            Assert(flat.Entries.Count == 3);
            var recursive = FolderNames.Read(root, true);
            Assert(recursive.Entries.Count == 4);
            string files = FolderNames.Format(flat.Entries, 1, true, false, 0);
            Assert(files == "Árvore completa 123.zip" + Environment.NewLine + "beta.rar");
            string directories = FolderNames.Format(recursive.Entries, 2, false, false, 0);
            Assert(directories == "Pasta.com.pontos");
            string formatted = FolderNames.Format(recursive.Entries, 1, false, true, 0);
            Assert(formatted.Contains("1. Árvore completa 123") && formatted.Contains(@"Pasta.com.pontos\Arquivo longo"));
            var now = DateTime.Now;
            var ordered = FolderNames.Format([new("a.zip", false, now.AddDays(-1)), new("z.rar", false, now)], 1, true, false, 1);
            Assert(ordered.StartsWith("z.rar"));
            Assert(FolderNames.Format([], 0, true, false, 0) == "");
            Assert(File.ReadAllText(Path.Combine(root, "beta.rar")) == "preserve");
            return checks;
        }
        finally
        {
            string resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("names-test-")) Directory.Delete(resolved, true);
        }
    }
}
