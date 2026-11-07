using System.IO;
using System.Globalization;

namespace Soneca;
public record FolderEntry(string RelativePath, bool Directory, DateTime Modified);
public record FolderListing(List<FolderEntry> Entries, int Skipped);
public static class FolderNames
{
    public static FolderListing Read(string root, bool recursive)
    {
        root = Path.GetFullPath(root);
        if (!System.IO.Directory.Exists(root)) throw new IOException("Escolha uma pasta existente.");
        var entries = new List<FolderEntry>(); int skipped = 0;
        void Visit(string folder)
        {
            try
            {
                foreach (string path in System.IO.Directory.EnumerateFileSystemEntries(folder))
                {
                    try
                    {
                        var attr = File.GetAttributes(path);
                        bool dir = (attr & FileAttributes.Directory) != 0;
                        entries.Add(new(Path.GetRelativePath(root, path), dir, File.GetLastWriteTime(path)));
                        if (recursive && dir && (attr & FileAttributes.ReparsePoint) == 0) Visit(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (folder == root) throw; skipped++; }
        }
        Visit(root); return new(entries, skipped);
    }
    public static string Format(IEnumerable<FolderEntry> source, int kind, bool extensions, bool numbered, int order)
    {
        var entries = source.Where(e => kind == 0 || (kind == 1 ? !e.Directory : e.Directory));
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), true);
        entries = order == 1 ? entries.OrderByDescending(e => e.Modified).ThenBy(e => e.RelativePath, comparer) : entries.OrderBy(e => e.RelativePath, comparer);
        return string.Join(Environment.NewLine, entries.Select((e, i) =>
        {
            string name = e.RelativePath;
            if (!extensions && !e.Directory)
            {
                string? parent = Path.GetDirectoryName(name);
                name = string.IsNullOrEmpty(parent) ? Path.GetFileNameWithoutExtension(name) : Path.Combine(parent, Path.GetFileNameWithoutExtension(name));
            }
            return (numbered ? $"{i + 1}. " : "") + name.Replace("\r", " ").Replace("\n", " ");
        }));
    }
}
