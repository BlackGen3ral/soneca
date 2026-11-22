using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace Soneca;

internal sealed class AndroidControl(string folder)
{
    private const string Package = "scrcpy-win64-v5.0.1";
    private const string Release = "https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/" + Package + ".zip";
    private const string ExpectedHash = "B12A2C4EE8BE317422451FC7DCF8EE20A71B5EA7EF9AD73DDD825A25316227A5";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2), MaxResponseContentBufferSize = 16 * 1024 * 1024 };
    private string PackagePath => Path.Combine(folder, Package);

    public async Task PrepareAsync(Action downloading, CancellationToken token)
    {
        if (IsPackage(PackagePath)) return;
        downloading();
        var archive = await Client.GetByteArrayAsync(Release, token);
        if (!Convert.ToHexString(SHA256.HashData(archive)).Equals(ExpectedHash, StringComparison.Ordinal))
            throw new IOException("O download do scrcpy não passou na verificação de integridade.");
        await Task.Run(() => Install(archive, token), token);
    }

    private void Install(byte[] archive, CancellationToken token)
    {
        Directory.CreateDirectory(folder);
        string staging = Path.Combine(folder, ".download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var stream = new MemoryStream(archive, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            zip.ExtractToDirectory(staging);
            string source = Path.Combine(staging, Package);
            if (!IsPackage(source)) throw new IOException("O pacote do scrcpy está incompleto.");
            token.ThrowIfCancellationRequested();
            if (!IsPackage(PackagePath)) Directory.Move(source, PackagePath);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private static bool IsPackage(string path) =>
        File.Exists(Path.Combine(path, "scrcpy.exe")) &&
        File.Exists(Path.Combine(path, "adb.exe")) &&
        File.Exists(Path.Combine(path, "scrcpy-server"));

    public static string LocalEndpoint(string value)
    {
        string text = value.Trim();
        if (!IPEndPoint.TryParse(text, out var endpoint) || endpoint.Port == 0 ||
            !PcLan.IsPrivate(endpoint.Address) || endpoint.ToString() != text)
            throw new ArgumentException("Copie o endereço IPv4 e a porta exibidos no celular.");
        var adapter = PcLan.Adapters().FirstOrDefault(local => local.Prefix <= 30 &&
            PcLan.SameSubnet(local, endpoint.Address) && !local.Address.Equals(endpoint.Address) &&
            !PcLan.Broadcast(local.Address, local.Prefix).Equals(endpoint.Address));
        if (adapter is null) throw new ArgumentException("O PC e o celular precisam estar na mesma rede local.");
        return endpoint.ToString();
    }

    public static bool ValidCode(string code) => code.Length == 6 && code.All(char.IsAsciiDigit);

    public async Task PairAsync(string endpoint, string code, CancellationToken token)
    {
        var log = await RunAdbAsync(["pair", endpoint], code, token);
        if (!log.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase))
            throw new IOException("O Android recusou o pareamento. Abra um novo código e confira o endereço e a porta dessa tela.");
    }

    public async Task MirrorAsync(string? endpoint, Action onOpen, CancellationToken token)
    {
        if (endpoint is not null)
        {
            var log = await RunAdbAsync(["connect", endpoint], null, token);
            if (!log.Contains("connected to " + endpoint, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Não consegui conectar ao Android. Confira o pareamento e o endereço da tela principal de Depuração sem fio.");
        }
        token.ThrowIfCancellationRequested();
        using var viewer = Start("scrcpy.exe", [
            endpoint is null ? "--select-usb" : "--serial=" + endpoint,
            "--window-title=Artho AI · Tela do Android", "--max-size=1600", "--max-fps=60", "--no-audio"
        ]);
        using var stopping = token.Register(() => Stop(viewer));
        var output = viewer.StandardOutput.ReadToEndAsync();
        var errors = viewer.StandardError.ReadToEndAsync();
        try
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (!viewer.HasExited)
            {
                viewer.Refresh();
                if (viewer.MainWindowHandle != IntPtr.Zero)
                {
                    onOpen();
                    await viewer.WaitForExitAsync(token);
                    token.ThrowIfCancellationRequested();
                    if (viewer.ExitCode == 0) return;
                    break;
                }
                if (DateTimeOffset.UtcNow >= deadline)
                    throw new IOException("O celular demorou para responder. Confira se está desbloqueado e se autorizou a depuração.");
                await Task.Delay(160, token);
            }
            token.ThrowIfCancellationRequested();
            string log = (await output) + (await errors);
            if (log.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Desbloqueie o Android e aceite a autorização de depuração no celular.");
            if (log.Contains("multiple", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Há mais de um Android conectado. Deixe conectado apenas o celular que quer controlar.");
            if (log.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
                throw new IOException("O celular foi desconectado. Reconecte e tente de novo.");
            throw new IOException(endpoint is null
                ? "Não consegui abrir o Android por USB. Confira o cabo de dados, a Depuração USB e a autorização no celular."
                : "Não consegui abrir a tela do Android. Confira se a Depuração sem fio continua ativa e tente de novo.");
        }
        finally { Stop(viewer); }
    }

    private async Task<string> RunAdbAsync(string[] args, string? input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(18));
        using var command = Start("adb.exe", args, input is not null);
        using var stopping = timeout.Token.Register(() => Stop(command));
        try
        {
            var output = command.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = command.StandardError.ReadToEndAsync(timeout.Token);
            if (input is not null)
            {
                await command.StandardInput.WriteAsync((input + "\n").AsMemory(), timeout.Token);
                command.StandardInput.Close();
            }
            await command.WaitForExitAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            return (await output) + (await errors);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException("O Android demorou para responder. Confira a rede e a Depuração sem fio no celular."); }
        finally { Stop(command); }
    }

    private Process Start(string executable, string[] args, bool redirectInput = false)
    {
        var start = new ProcessStartInfo(Path.Combine(PackagePath, executable))
        {
            WorkingDirectory = PackagePath,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = redirectInput
        };
        start.Environment["ADB"] = Path.Combine(PackagePath, "adb.exe");
        start.Environment["SCRCPY_SERVER_PATH"] = Path.Combine(PackagePath, "scrcpy-server");
        start.Environment.Remove("ANDROID_SERIAL");
        start.Environment.Remove("ADB_SERVER_SOCKET");
        start.Environment.Remove("ANDROID_ADB_SERVER_PORT");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new IOException("Não consegui iniciar o controle do Android.");
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
