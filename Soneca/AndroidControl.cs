using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Soneca;

internal sealed class AndroidControl(string folder)
{
    private const string Package = "scrcpy-win64-v5.0.1";
    private const string Release = "https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/" + Package + ".zip";
    private const string ExpectedHash = "B12A2C4EE8BE317422451FC7DCF8EE20A71B5EA7EF9AD73DDD825A25316227A5";
    private const string ConnectService = "_adb-tls-connect._tcp";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2), MaxResponseContentBufferSize = 16 * 1024 * 1024 };
    private string PackagePath => Path.Combine(folder, Package);
    private string SavedDevicePath => Path.Combine(folder, "wifi-device.txt");

    public string? RememberedDevice()
    {
        if (!File.Exists(SavedDevicePath)) return null;
        string name = File.ReadAllText(SavedDevicePath).Trim();
        return Regex.IsMatch(name, @"^[a-zA-Z0-9._-]{1,128}$") ? name : null;
    }

    public void Remember(string name)
    {
        string temporary = SavedDevicePath + ".new";
        File.WriteAllText(temporary, name);
        File.Move(temporary, SavedDevicePath, overwrite: true);
    }

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

    public static string LocalEndpoint(string value) => LocalEndpoint(value, PcLan.Adapters()).ToString();

    private static IPEndPoint LocalEndpoint(string value, LanAdapter[] adapters)
    {
        string text = value.Trim();
        if (!IPEndPoint.TryParse(text, out var endpoint) || endpoint.Port == 0 ||
            !PcLan.IsPrivate(endpoint.Address) || endpoint.ToString() != text)
            throw new ArgumentException("Copie o endereço IPv4 e a porta exibidos no celular.");
        var adapter = adapters.FirstOrDefault(local => local.Prefix <= 30 &&
            PcLan.SameSubnet(local, endpoint.Address) && !local.Address.Equals(endpoint.Address) &&
            !PcLan.Broadcast(local.Address, local.Prefix).Equals(endpoint.Address));
        if (adapter is null) throw new ArgumentException("O PC e o celular precisam estar na mesma rede local.");
        return endpoint;
    }

    public static bool ValidCode(string code) => code.Length == 6 && code.All(char.IsAsciiDigit);

    public async Task<string> PairAsync(string endpoint, string code, CancellationToken token)
    {
        var log = await RunAdbAsync(["pair", endpoint], code, token);
        var paired = Regex.Match(log, @"Successfully paired to [^\r\n]+? \[guid=([a-zA-Z0-9._-]{1,128})\]");
        if (!paired.Success)
            throw new IOException("O Android recusou o pareamento. Abra um novo código e confira o endereço e a porta dessa tela.");
        return paired.Groups[1].Value;
    }

    public async Task<string> PairQrAsync(AndroidPairingQr invite, Action scanned, CancellationToken token)
    {
        var pair = await FindServiceAsync("_adb-tls-pairing._tcp", invite.Name, null, TimeSpan.FromMinutes(2), token)
            ?? throw new IOException("O QR Code expirou. Gere outro ou abra o pareamento manual.");
        token.ThrowIfCancellationRequested();
        scanned();
        return await PairAsync(pair.ToString(), invite.Secret, token);
    }

    private async Task<IPEndPoint?> FindServiceAsync(string type, string name, IPAddress? pairedAddress,
        TimeSpan duration, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(duration);
        try
        {
            while (true)
            {
                var log = await RunAdbAsync(["mdns", "services"], null, deadline.Token);
                var endpoint = FindService(log, type, name, pairedAddress, PcLan.Adapters());
                if (endpoint is not null) return endpoint;
                await Task.Delay(800, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        { return null; }
    }

    internal static IPEndPoint? FindService(string log, string type, string name, IPAddress? pairedAddress, LanAdapter[] adapters)
        => LocalServices(log, type, adapters).FirstOrDefault(service =>
            (service.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                (pairedAddress is not null && MatchesDevice(service.Name, name))) &&
            (pairedAddress is null || service.Endpoint.Address.Equals(pairedAddress)))?.Endpoint;

    private static IEnumerable<WifiService> LocalServices(string log, string type, LanAdapter[] adapters)
    {
        foreach (string line in log.Split('\n'))
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 || fields[1].TrimEnd('.') != type) continue;
            if (!Regex.IsMatch(fields[0], @"^[a-zA-Z0-9._-]{1,128}$")) continue;
            foreach (string field in fields.Skip(2))
            {
                IPEndPoint endpoint;
                try { endpoint = LocalEndpoint(field, adapters); }
                catch (ArgumentException) { continue; }
                yield return new WifiService(fields[0], endpoint);
            }
        }
    }

    private sealed record WifiService(string Name, IPEndPoint Endpoint)
    {
        public string Serial => Name + "." + ConnectService;
    }

    private static bool MatchesDevice(string advertised, string remembered) =>
        advertised.Equals(remembered, StringComparison.OrdinalIgnoreCase) ||
        advertised.StartsWith(remembered + "-", StringComparison.OrdinalIgnoreCase);

    private async Task<WifiService[]> DiscoverWifiAsync(CancellationToken token)
    {
        var local = PcLan.Adapters();
        if (local.Length == 0) return [];
        string listing = await RunAdbAsync(["mdns", "services"], null, token);
        return LocalServices(listing, ConnectService, local).Distinct().ToArray();
    }

    private async Task<HashSet<string>> OnlineDevicesAsync(CancellationToken token)
    {
        string listing = await RunAdbAsync(["devices"], null, token);
        return listing.Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length == 2 && fields[1] == "device")
            .Select(fields => fields[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string?> FindExistingDeviceAsync(string? endpoint, CancellationToken token)
    {
        var services = await DiscoverWifiAsync(token);
        if (endpoint is not null)
            return services.FirstOrDefault(device => device.Endpoint.ToString() == endpoint)?.Name;
        var online = await OnlineDevicesAsync(token);
        var matches = services.Where(device => OnlineSerial(device, online) is not null).Select(device => device.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string? OnlineSerial(WifiService device, HashSet<string> online)
    {
        if (online.Contains(device.Serial)) return device.Serial;
        string address = device.Endpoint.ToString();
        return online.Contains(address) ? address : null;
    }

    internal static string? ConnectedWirelessSerial(string remembered, IEnumerable<string> online)
    {
        const string suffix = "." + ConnectService;
        var matches = online.Where(serial => serial.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            MatchesDevice(serial[..^suffix.Length], remembered)).ToArray();
        return matches.FirstOrDefault(serial => serial.Equals(remembered + suffix, StringComparison.OrdinalIgnoreCase))
            ?? (matches.Length == 1 ? matches[0] : null);
    }

    public async Task MirrorWifiAsync(string name, Action waiting, Action<string> addressChanged,
        Action<AndroidViewWindow> onOpen, CancellationToken token)
    {
        waiting();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var online = await OnlineDevicesAsync(token);
                if (ConnectedWirelessSerial(name, online) is { } connected)
                {
                    await MirrorAsync(null, onOpen, token, connected);
                    return;
                }
                var matching = (await DiscoverWifiAsync(token)).Where(device => MatchesDevice(device.Name, name)).ToArray();
                var exact = matching.Where(device => device.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                var candidates = exact.Length == 0 ? matching : exact;
                if (candidates.Length > 0)
                {
                    var active = candidates.Where(device => online.Contains(device.Endpoint.ToString())).ToArray();
                    var device = candidates.Length == 1 ? candidates[0] : active.Length == 1 ? active[0] : null;
                    if (device is not null)
                    {
                        string endpoint = device.Endpoint.ToString();
                        addressChanged(endpoint);
                        await MirrorAsync(endpoint, onOpen, token, OnlineSerial(device, online));
                        return;
                    }
                }
            }
            catch (AndroidConnectionException) { waiting(); }
            await Task.Delay(1800, token);
        }
    }

    public async Task MirrorAsync(string? endpoint, Action<AndroidViewWindow> onOpen, CancellationToken token, string? onlineSerial = null)
    {
        bool wireless = endpoint is not null || onlineSerial is not null;
        if (endpoint is not null && onlineSerial is null)
        {
            var log = await RunAdbAsync(["connect", endpoint], null, token);
            if (!log.Contains("connected to " + endpoint, StringComparison.OrdinalIgnoreCase))
                throw new AndroidConnectionException("Não consegui conectar ao Android. Confira o endereço atual na tela de Depuração por Wi-Fi.");
        }
        token.ThrowIfCancellationRequested();
        string? serial = onlineSerial ?? endpoint;
        if (serial is null)
        {
            string selected = (await RunAdbAsync(["-d", "get-serialno"], null, token)).Split('\n', 2)[0].Trim();
            if (Regex.IsMatch(selected, @"^[a-zA-Z0-9._-]{1,128}$") && selected != "unknown") serial = selected;
        }
        using var viewer = Start("scrcpy.exe", [
            serial is null ? "--select-usb" : "--serial=" + serial,
            "--window-title=Artho AI · Tela do Android", "--max-size=1600", "--max-fps=60", "--no-audio",
            "--window-borderless", "--window-x=-30000", "--window-y=-30000",
            "--no-window-aspect-ratio-lock", "--render-fit=letterbox"
        ]);
        IntPtr viewerWindow = IntPtr.Zero;
        int closeRequested = 0;
        using var stopping = token.Register(RequestClose);
        var output = viewer.StandardOutput.ReadToEndAsync();
        var errors = viewer.StandardError.ReadToEndAsync();
        try
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (!viewer.HasExited)
            {
                viewer.Refresh();
                if (viewer.MainWindowHandle is var handle && handle != IntPtr.Zero)
                {
                    Interlocked.Exchange(ref viewerWindow, handle);
                    token.ThrowIfCancellationRequested();
                    onOpen(new AndroidViewWindow(handle, viewer.Id, serial));
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
            if (viewer.ExitCode == 2 || log.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
                throw new AndroidConnectionException("O celular foi desconectado. Reconecte e tente de novo.");
            if (wireless && (log.Contains("device offline", StringComparison.OrdinalIgnoreCase) ||
                (log.Contains("ADB device", StringComparison.OrdinalIgnoreCase) && log.Contains("not found", StringComparison.OrdinalIgnoreCase))))
                throw new AndroidConnectionException("O Android saiu da rede antes de abrir a tela.");
            throw new IOException(wireless
                ? "Não consegui abrir a tela do Android. Confira se a Depuração sem fio continua ativa e tente de novo."
                : "Não consegui abrir o Android por USB. Confira o cabo de dados, a Depuração USB e a autorização no celular.");
        }
        finally
        {
            RequestClose();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await viewer.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { Stop(viewer); }
        }

        void RequestClose()
        {
            if (Interlocked.Exchange(ref closeRequested, 1) != 0 || viewer.HasExited) return;
            IntPtr handle = Volatile.Read(ref viewerWindow);
            if (handle != IntPtr.Zero && GetWindowThreadProcessId(handle, out var owner) != 0 &&
                owner == viewer.Id && PostMessageW(handle, 0x0010, IntPtr.Zero, IntPtr.Zero)) return;
            Stop(viewer);
        }
    }

    public async Task WakeScreenAsync(string serial, CancellationToken token)
    {
        await RunAdbAsync(["-s", serial, "shell", "input", "keyevent", "KEYCODE_WAKEUP"], null, token, requireSuccess: true);
    }

    private async Task<string> RunAdbAsync(string[] args, string? input, CancellationToken token, bool requireSuccess = false)
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
            string log = (await output) + (await errors);
            if (requireSuccess && command.ExitCode != 0)
                throw new AndroidConnectionException("Não consegui acender a tela. Confira se o Android ainda está conectado.");
            return log;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new AndroidConnectionException("O Android demorou para responder. Confira a rede e a Depuração sem fio no celular."); }
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

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}

internal sealed class AndroidConnectionException(string message) : IOException(message);
internal sealed record AndroidViewWindow(IntPtr Handle, int ProcessId, string? Serial);

internal sealed record AndroidPairingQr(string Name, string Secret)
{
    public static AndroidPairingQr Create() => new("studio-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)),
        RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 10));

    public string Payload => $"WIFI:T:ADB;S:{Name};P:{Secret};;";
}
