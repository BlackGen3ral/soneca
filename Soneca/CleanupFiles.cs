using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Soneca;

internal enum CleanupKind { Temp, Prefetch, CrashDumps }

internal sealed record CleanFile(string Path, long Length, long Modified, uint Volume = 0, ulong Identity = 0, long Created = 0);
internal sealed record CleanPlan(string Root, CleanupKind Kind, List<CleanFile> Files, int Ignored, int Recent,
    bool Protected = false, string? Error = null)
{
    public long Bytes => Files.Sum(file => file.Length);
}
internal sealed record CleanResult(int Deleted, int Skipped, long Bytes, string? Error = null, bool Cancelled = false);

internal static partial class Cleaner
{
    public static string TempRoot => Normalize(Path.GetTempPath());
    public static string PrefetchRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
    public static string CrashDumpRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");

    public static string Size(long bytes) => bytes switch
    {
        >= 1073741824 => string.Create(BrazilianPortuguese, $"{bytes / 1073741824.0:0.00} GB"),
        >= 1048576 => string.Create(BrazilianPortuguese, $"{bytes / 1048576.0:0.0} MB"),
        >= 1024 => string.Create(BrazilianPortuguese, $"{bytes / 1024.0:0.0} KB"),
        _ => $"{bytes} B"
    };
    private static readonly CultureInfo BrazilianPortuguese = CultureInfo.GetCultureInfo("pt-BR");

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool SamePath(string left, string right) => Normalize(left).Equals(Normalize(right), StringComparison.OrdinalIgnoreCase);

    internal static void ValidateTempRoot(string root)
    {
        root = Normalize(root);
        var leaf = Path.GetFileName(root);
        if (!leaf.Equals("Temp", StringComparison.OrdinalIgnoreCase) && !leaf.Equals("Tmp", StringComparison.OrdinalIgnoreCase))
            throw new IOException("O caminho de %temp% deve apontar para uma pasta chamada Temp ou Tmp.");
        if (Path.GetPathRoot(root)?.Length != 3)
            throw new IOException("A pasta temporária precisa estar em uma unidade local.");
        var forbidden = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppContext.BaseDirectory
        };
        if (forbidden.Any(path => path.Length > 0 && SamePath(root, path)))
            throw new IOException("A pasta temporária está configurada para um destino protegido.");
        CheckAncestors(root);
    }

    private static void CheckAncestors(string directory)
    {
        var ancestors = new Stack<string>();
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            ancestors.Push(current.FullName);
        while (ancestors.TryPop(out string? path))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Links e pastas redirecionadas são ignorados.");
        }
    }

    public static CleanPlan ScanTemp(int minimumDays, CancellationToken cancellation, IProgress<int>? progress)
    {
        ValidateTempRoot(TempRoot);
        DateTime? cutoff = minimumDays > 0 ? DateTime.UtcNow.AddDays(-minimumDays) : null;
        return Scan(TempRoot, CleanupKind.Temp, cutoff, cancellation, progress);
    }

    public static CleanPlan ScanCrashDumps(CancellationToken cancellation) =>
        Scan(CrashDumpRoot, CleanupKind.CrashDumps, cancellation: cancellation);

    internal static CleanPlan Scan(string root, CleanupKind kind, DateTime? cutoff = null,
        CancellationToken cancellation = default, IProgress<int>? progress = null)
    {
        root = Normalize(root);
        var files = new List<CleanFile>();
        int ignored = 0, recent = 0, seen = 0;
        bool blocked = false;
        long minAgeCutoff = cutoff?.ToFileTimeUtc() ?? long.MaxValue;
        string? error = null;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                CheckAncestors(directory);
                foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++seen % 128 == 0) progress?.Report(seen);
                    try
                    {
                        if (!BelowRoot(root, path)) { ignored++; continue; }
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { ignored++; continue; }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (kind == CleanupKind.Temp) pending.Push(path);
                            continue;
                        }
                        if (!EligiblePath(root, path, kind)) continue;
                        using var handle = Open(path);
                        var file = Identify(path, handle);
                        if (file is null) { ignored++; continue; }
                        if (file.Modified > minAgeCutoff) { recent++; continue; }
                        files.Add(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ignored++; }
                }
            }
            catch (DirectoryNotFoundException) when (directory == root) { }
            catch (UnauthorizedAccessException) when (directory == root)
            {
                blocked = true;
                error = "Acesso à pasta bloqueado.";
            }
            catch (IOException ex) when (directory == root)
            {
                error = ex.Message;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ignored++; }
        }
        return new CleanPlan(root, kind, files, ignored, recent, blocked, error);
    }

    public static CleanResult CleanFiles(CleanPlan plan, CancellationToken cancellation, IProgress<int>? progress)
    {
        switch (plan.Kind)
        {
            case CleanupKind.Temp:
                ValidateTempRoot(TempRoot);
                ValidatePlan(plan, CleanupKind.Temp, TempRoot);
                break;
            case CleanupKind.CrashDumps:
                ValidatePlan(plan, CleanupKind.CrashDumps, CrashDumpRoot);
                break;
            default: throw new IOException("Categoria de arquivos inválida.");
        }
        return Execute(plan, cancellation, progress);
    }

    private static void ValidatePlan(CleanPlan plan, CleanupKind kind, string root)
    {
        if (plan.Kind != kind || plan.Protected || plan.Error is not null || !SamePath(plan.Root, root))
            throw new IOException("Destino ou análise inválida. Analise novamente.");
    }

    internal static CleanResult Execute(CleanPlan plan, CancellationToken cancellation = default, IProgress<int>? progress = null)
    {
        int deleted = 0, skipped = 0, seen = 0;
        long bytes = 0;
        string root = Normalize(plan.Root);
        try { CheckAncestors(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new CleanResult(0, plan.Files.Count, 0, ex.Message); }
        foreach (var file in plan.Files)
        {
            if (cancellation.IsCancellationRequested) return new CleanResult(deleted, skipped, bytes, Cancelled: true);
            if (++seen % 128 == 0) progress?.Report(seen);
            try
            {
                string path = Path.GetFullPath(file.Path);
                if (!EligiblePath(root, path, plan.Kind)) { skipped++; continue; }
                CheckAncestors(Path.GetDirectoryName(path)!);
                using var handle = Open(path);
                var current = Identify(path, handle);
                if (current is null || current != file) { skipped++; continue; }
                int disposition = 1;
                if (!SetFileInformationByHandle(handle, 4, ref disposition, 4)) { skipped++; continue; }
                deleted++;
                bytes += current.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { skipped++; }
        }
        return new CleanResult(deleted, skipped, bytes);
    }

    private static bool BelowRoot(string root, string path) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool EligiblePath(string root, string path, CleanupKind kind)
    {
        if (!BelowRoot(root, path)) return false;
        if (kind == CleanupKind.Temp) return true;
        string extension = kind == CleanupKind.Prefetch ? ".pf" : ".dmp";
        return Path.GetDirectoryName(path)!.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase);
    }

    // Both phases use exclusive handles. The delete flag is set only after the second identity check.
    private static SafeFileHandle Open(string path) => CreateFileW(path, 0x10000 | 0x80, 0, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);

    private static CleanFile? Identify(string path, SafeFileHandle handle)
    {
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) || info.Links != 1 ||
            (info.Attributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.ReadOnly)) != 0)
            return null;
        var buffer = new StringBuilder(260);
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity && length < 32768)
        {
            buffer.EnsureCapacity((int)length + 1);
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (length == 0 || length >= buffer.Capacity) return null;
        string resolved = buffer.ToString();
        if (resolved.StartsWith(@"\\?\")) resolved = resolved[4..];
        if (!resolved.Equals(path, StringComparison.OrdinalIgnoreCase)) return null;
        return new CleanFile(path, ((long)info.SizeHigh << 32) | info.SizeLow,
            CombineTime(info.WriteHigh, info.WriteLow), info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow,
            CombineTime(info.CreateHigh, info.CreateLow));
    }

    private static long CombineTime(int high, int low) => ((long)(uint)high << 32) | (uint)low;

    [StructLayout(LayoutKind.Sequential)] private struct HandleInfo
    {
        public uint Attributes;
        public int CreateLow, CreateHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, ref int value, uint size);
}
