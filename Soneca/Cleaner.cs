using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Soneca;
public record CleanFile(string Path, long Length, long Modified);
public record CleanPlan(string Root, bool Prefetch, List<CleanFile> Files, int Skipped, bool Protected)
{
    public long Bytes => Files.Sum(f => f.Length);
}
public record CleanResult(int Deleted, int Skipped, long Bytes, string? Error = null);
public record BinInfo(long Items, long Bytes, string? Error = null);
internal record PrefetchEntry(string Name, long Length, long Modified);
internal record PrefetchRequest(bool AnalyzeNow, List<PrefetchEntry> Files);

public static class Cleaner
{
    public static bool IsAdministrator => new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    public static string TempRoot => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
    public static string PrefetchRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
    public static string Size(long bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824.0:0.00} GB" : bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1024.0:0.0} KB";
    public static void ValidateTempRoot(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var forbidden = new[] { Path.GetPathRoot(root), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppContext.BaseDirectory };
        if (forbidden.Any(p => !string.IsNullOrEmpty(p) && root.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)), StringComparison.OrdinalIgnoreCase)))
            throw new IOException("A pasta temporária está configurada para um local que não pode ser limpo.");
        // Refuse environment configurations pointing to a non-temporary personal folder.
        if (!Path.GetFileName(root).Contains("temp", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(root).Equals("tmp", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Por segurança, o caminho de %temp% deve terminar em uma pasta Temp ou Tmp.");
        CheckAncestors(root);
    }
    internal static void CheckAncestors(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Links e pastas redirecionadas são ignorados.");
    }
    public static CleanPlan ScanTemp() { ValidateTempRoot(TempRoot); return Scan(TempRoot, false); }
    public static CleanPlan ScanPrefetch() => Scan(PrefetchRoot, true);
    internal static CleanPlan Scan(string root, bool prefetch)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var files = new List<CleanFile>(); int skipped = 0; bool blocked = false;
        try { CheckAncestors(root); }
        catch { return new(root, prefetch, files, 1, true); }
        if (!Directory.Exists(root)) return new(root, prefetch, files, 0, false);
        void Visit(string dir)
        {
            try
            {
                CheckAncestors(dir);
                foreach (string path in Directory.EnumerateFileSystemEntries(dir))
                {
                    try
                    {
                        var attr = File.GetAttributes(path);
                        if ((attr & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if ((attr & FileAttributes.Directory) != 0) { if (!prefetch) Visit(path); continue; }
                        if (prefetch && !Path.GetExtension(path).Equals(".pf", StringComparison.OrdinalIgnoreCase)) continue;
                        var info = new FileInfo(path);
                        files.Add(new(path, info.Length, info.LastWriteTimeUtc.ToFileTimeUtc()));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; if (dir == root) blocked = true; }
        }
        Visit(root); return new(root, prefetch, files, skipped, blocked);
    }
    internal static CleanResult Execute(CleanPlan plan)
    {
        int deleted = 0, skipped = plan.Skipped; long bytes = 0;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Root));
        try { CheckAncestors(root); } catch (Exception ex) { return new(0, plan.Files.Count, 0, ex.Message); }
        foreach (var file in plan.Files)
        {
            try
            {
                string path = Path.GetFullPath(file.Path);
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                if (plan.Prefetch && (!Path.GetDirectoryName(path)!.Equals(root, StringComparison.OrdinalIgnoreCase) || !Path.GetExtension(path).Equals(".pf", StringComparison.OrdinalIgnoreCase))) { skipped++; continue; }
                CheckAncestors(Path.GetDirectoryName(path)!);
                // Exclusive handle: skip files opened by another process. Never follow a file link.
                using var handle = CreateFileW(path, 0x10000 | 0x80, 0, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
                if (handle.IsInvalid) { skipped++; continue; }
                var buffer = new System.Text.StringBuilder(32768);
                uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity) { skipped++; continue; }
                string resolved = buffer.ToString(); if (resolved.StartsWith(@"\\?\")) resolved = resolved[4..];
                if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & ((uint)FileAttributes.ReparsePoint | (uint)FileAttributes.Directory)) != 0) { skipped++; continue; }
                long size = ((long)info.SizeHigh << 32) | info.SizeLow;
                long modified = ((long)(uint)info.WriteHigh << 32) | (uint)info.WriteLow;
                if (size != file.Length || modified != file.Modified) { skipped++; continue; }
                int disposition = 1;
                if (!SetFileInformationByHandle(handle, 4, ref disposition, 4)) { skipped++; continue; }
                deleted++; bytes += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { skipped++; }
        }
        return new(deleted, skipped, bytes);
    }
    public static CleanResult CleanTemp(CleanPlan plan)
    {
        ValidateTempRoot(TempRoot);
        if (!Path.GetFullPath(plan.Root).Equals(TempRoot, StringComparison.OrdinalIgnoreCase) || plan.Prefetch) throw new IOException("Destino temporário inválido.");
        return Execute(plan);
    }
    public static async Task<CleanResult> CleanPrefetch(CleanPlan plan)
    {
        if (!Path.GetFullPath(plan.Root).Equals(PrefetchRoot, StringComparison.OrdinalIgnoreCase) || !plan.Prefetch) throw new IOException("Destino Prefetch inválido.");
        if (IsAdministrator) return await Task.Run(() => Execute(plan));
        string pipeName = "ArthoControl-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--clean-prefetch " + pipeName) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            var connected = pipe.WaitForConnectionAsync(cancellation.Token);
            var exited = process!.WaitForExitAsync(cancellation.Token);
            if (await Task.WhenAny(connected, exited) == exited && !pipe.IsConnected) return new(0, 0, 0, "A limpeza do Prefetch não foi autorizada ou não pôde iniciar.");
            await connected;
            var request = new PrefetchRequest(plan.Protected, plan.Files.Select(f => new PrefetchEntry(Path.GetFileName(f.Path), f.Length, f.Modified)).ToList());
            using (var writer = new StreamWriter(pipe, leaveOpen: true)) { await writer.WriteLineAsync(JsonSerializer.Serialize(request)); await writer.FlushAsync(cancellation.Token); }
            using var reader = new StreamReader(pipe);
            string response = await reader.ReadToEndAsync(cancellation.Token);
            return JsonSerializer.Deserialize<CleanResult>(response) ?? new(0, 0, 0, "Não foi possível obter o resultado do Prefetch.");
        }
        catch (System.ComponentModel.Win32Exception) { return new(0, 0, 0, "Autorização do Prefetch cancelada."); }
        catch (OperationCanceledException) { return new(0, 0, 0, "O tempo para autorizar o Prefetch terminou."); }
    }
    public static int PrefetchHelper(string pipeName)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(pipeName, "^ArthoControl-[a-f0-9]{32}$")) return 1;
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(15000);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var reader = new StreamReader(pipe, leaveOpen: true);
            string? line = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
            if (line is null || line.Length > 4000000) return 1;
            var request = JsonSerializer.Deserialize<PrefetchRequest>(line);
            if (request is null || request.Files.Count > 10000) return 1;
            var plan = request.AnalyzeNow ? ScanPrefetch() : new CleanPlan(PrefetchRoot, true,
                request.Files.Where(f => f.Name == Path.GetFileName(f.Name) && f.Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Path.GetExtension(f.Name).Equals(".pf", StringComparison.OrdinalIgnoreCase))
                .Select(f => new CleanFile(Path.Combine(PrefetchRoot, f.Name), f.Length, f.Modified)).ToList(), 0, false);
            var result = plan.Protected ? new CleanResult(0, plan.Skipped, 0, "O Windows não permitiu acessar o Prefetch.") : Execute(plan);
            using var writer = new StreamWriter(pipe); writer.Write(JsonSerializer.Serialize(result));
            return 0;
        }
        catch { return 1; }
    }
    public static BinInfo QueryBin()
    {
        var info = new QueryInfo { Size = (uint)Marshal.SizeOf<QueryInfo>() };
        int hr = SHQueryRecycleBinW(null, ref info);
        return hr < 0 ? new(0, 0, "Não foi possível analisar a lixeira.") : new(info.Items, info.Bytes);
    }
    public static string? EmptyBin(IntPtr window)
    {
        int hr = SHEmptyRecycleBinW(window, null, 1 | 2 | 4);
        return hr < 0 ? "Não foi possível esvaziar completamente a lixeira." : null;
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueryInfo { public uint Size; public long Bytes, Items; }
    [StructLayout(LayoutKind.Sequential)] private struct HandleInfo { public uint Attributes; public int CreateLow, CreateHigh, AccessLow, AccessHigh, WriteLow, WriteHigh; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, System.Text.StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out HandleInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, ref int value, uint size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref QueryInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(IntPtr window, string? root, uint flags);
}
