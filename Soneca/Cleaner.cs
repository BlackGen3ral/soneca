using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Soneca;

internal sealed record PrefetchRequest(bool AnalyzeOnly, List<CleanFile> Files);
internal sealed record PrefetchReply(CleanPlan? Plan = null, CleanResult? Result = null, string? Error = null);
internal sealed record BinInfo(long Items, long Bytes, string? Error = null);

internal static partial class Cleaner
{
    private const int PrefetchLimit = 10_000;
    private const int PipeLimit = 4_000_000;

    public static bool IsAdministrator => new System.Security.Principal.WindowsPrincipal(
        System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(
            System.Security.Principal.WindowsBuiltInRole.Administrator);

    public static async Task<CleanPlan> ReadPrefetchAsync(CancellationToken cancellation)
    {
        var plan = await Task.Run(() => Scan(PrefetchRoot, CleanupKind.Prefetch, cancellation: cancellation));
        if (!plan.Protected || IsAdministrator) return plan;
        var reply = await PrefetchMessageAsync(new PrefetchRequest(true, []), cancellation);
        return reply.Plan is { } elevated && SamePath(elevated.Root, PrefetchRoot) && elevated.Kind == CleanupKind.Prefetch
            ? elevated : plan with { Error = reply.Error ?? "Não foi possível analisar o Prefetch." };
    }

    public static async Task<CleanResult> CleanPrefetchAsync(CleanPlan plan)
    {
        ValidatePlan(plan, CleanupKind.Prefetch, PrefetchRoot);
        if (IsAdministrator) return await Task.Run(() => Execute(plan));
        var files = plan.Files.Select(file => file with { Path = Path.GetFileName(file.Path) }).ToList();
        var reply = await PrefetchMessageAsync(new PrefetchRequest(false, files), CancellationToken.None);
        return reply.Result ?? new CleanResult(0, 0, 0, reply.Error ?? "O auxiliar do Prefetch não respondeu.");
    }

    private static async Task<PrefetchReply> PrefetchMessageAsync(PrefetchRequest request, CancellationToken cancellation)
    {
        if (request.Files.Count > PrefetchLimit) return new(Error: "O Prefetch excede o limite desta operação.");
        string pipeName = "ArthoControl-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--clean-prefetch " + pipeName)
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (process is null) return new(Error: "O auxiliar do Prefetch não iniciou.");
            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            if (await Task.WhenAny(connected, process.WaitForExitAsync(timeout.Token)) != connected && !pipe.IsConnected)
                return new(Error: "O auxiliar não conseguiu acessar o canal do mesmo usuário.");
            await connected;
            using var writer = new StreamWriter(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
            await writer.FlushAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            string? response = await ReadMessageAsync(reader, timeout.Token);
            return response is null ? new(Error: "O auxiliar do Prefetch não respondeu.")
                : JsonSerializer.Deserialize<PrefetchReply>(response) ?? new(Error: "Resposta do Prefetch inválida.");
        }
        catch (System.ComponentModel.Win32Exception) { return new(Error: "A autorização do Prefetch foi cancelada."); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { return new(Error: "O prazo para receber o resultado do Prefetch terminou. Analise novamente antes de repetir."); }
        catch (Exception ex) when (ex is IOException or JsonException)
        { return new(Error: "Não foi possível confirmar o resultado do Prefetch. Analise novamente antes de repetir."); }
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
            var line = ReadMessageAsync(reader, timeout.Token).GetAwaiter().GetResult();
            var request = line is null ? null : JsonSerializer.Deserialize<PrefetchRequest>(line);
            if (request?.Files is null || request.Files.Count > PrefetchLimit) return 1;
            var reply = HandlePrefetch(request);
            using var writer = new StreamWriter(pipe);
            writer.WriteLine(JsonSerializer.Serialize(reply));
            return 0;
        }
        catch { return 1; }
    }

    private static PrefetchReply HandlePrefetch(PrefetchRequest request)
    {
        if (request.AnalyzeOnly)
        {
            if (request.Files.Count != 0) return new(Error: "Solicitação de análise inválida.");
            var plan = Scan(PrefetchRoot, CleanupKind.Prefetch);
            return plan.Files.Count <= PrefetchLimit
                ? new(Plan: plan) : new(Error: "O Prefetch excede o limite desta operação.");
        }

        var accepted = request.Files.Where(file => file is not null && !string.IsNullOrWhiteSpace(file.Path)
            && file.Path == Path.GetFileName(file.Path) && file.Path.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && Path.GetExtension(file.Path).Equals(".pf", StringComparison.OrdinalIgnoreCase))
            .Select(file => file with { Path = Path.Combine(PrefetchRoot, file.Path) }).ToList();
        if (accepted.Count == 0) return new(Result: new CleanResult(0, request.Files.Count, 0));
        var result = Execute(new CleanPlan(PrefetchRoot, CleanupKind.Prefetch, accepted, 0, 0));
        return new(Result: result with { Skipped = result.Skipped + request.Files.Count - accepted.Count });
    }

    private static async Task<string?> ReadMessageAsync(StreamReader reader, CancellationToken cancellation)
    {
        var message = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false);
            if (count == 0) return null;
            int newline = Array.IndexOf(buffer, '\n', 0, count);
            int used = newline < 0 ? count : newline;
            if (message.Length + used > PipeLimit) return null;
            message.Append(buffer, 0, used);
            if (newline >= 0) return message.ToString();
        }
    }

    public static BinInfo QueryBin()
    {
        var info = new QueryInfo { Size = (uint)Marshal.SizeOf<QueryInfo>() };
        int hr = SHQueryRecycleBinW(null, ref info);
        return hr < 0 ? new(0, 0, "Não foi possível analisar a Lixeira.") : new(info.Items, info.Bytes);
    }

    internal static bool BinMatches(BinInfo preview, BinInfo current) =>
        preview.Error is null && current.Error is null && preview.Items == current.Items && preview.Bytes == current.Bytes;

    public static string EmptyBin(BinInfo preview, IntPtr window)
    {
        if (!BinMatches(preview, QueryBin())) return "A Lixeira mudou depois da análise. Analise novamente para esvaziar.";
        int hr = SHEmptyRecycleBinW(window, null, 1 | 2 | 4);
        if (hr < 0) return "O Windows não conseguiu esvaziar toda a Lixeira.";
        var after = QueryBin();
        if (after.Error is not null) return "O Windows recebeu a solicitação. O resultado da Lixeira não pôde ser conferido.";
        return after.Items == 0 ? "Lixeira esvaziada." : $"A Lixeira ainda tem {after.Items} itens.";
    }

    [StructLayout(LayoutKind.Sequential)] private struct QueryInfo { public uint Size; public long Bytes, Items; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref QueryInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(IntPtr window, string? root, uint flags);
}
