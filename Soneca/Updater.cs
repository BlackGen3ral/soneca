using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;

namespace Soneca;
public record ReleaseUpdate(Version Version, Uri Installer, Uri Checksums, long Size)
{
    public Uri Notes => new($"https://github.com/{Updater.Repo}/releases/tag/v{Version}");
}
public record DownloadedUpdate(string Path, string Hash);
public static class Updater
{
    public const string Repo = "BlackGen3ral/soneca";
    private const long MaxSetupBytes = 350L * 1024 * 1024;
    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    private static readonly HttpClient client = CreateClient();
    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ArthoAI-Windows");
        return c;
    }
    public static bool OfficialAsset(Uri uri) => SecureUri(uri) && uri.Host == "github.com"
        && uri.AbsolutePath.StartsWith("/" + Repo + "/releases/download/", StringComparison.Ordinal);
    private static bool SecureUri(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0;
    public static async Task<ReleaseUpdate?> Check()
    {
        using var response = await client.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'), out var version) || version <= Current) return null;
        Uri? installer = null, checksum = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            if (name == "ArthoAI-Setup.exe" || (installer is null && name == "Soneca-Setup.exe"))
            {
                installer = new Uri(asset.GetProperty("browser_download_url").GetString()!);
                size = asset.GetProperty("size").GetInt64();
            }
            if (name == "SHA256SUMS.txt") checksum = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        }
        if (installer is null || checksum is null || !OfficialAsset(installer) || !OfficialAsset(checksum)
            || size is <= 0 or > MaxSetupBytes) throw new IOException("A versão publicada está incompleta.");
        return new ReleaseUpdate(version, installer, checksum, size);
    }
    public static string ExpectedHash(string text, string? assetName = null)
    {
        foreach (string line in text.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            string name = parts[1].TrimStart('*');
            if ((assetName is null ? name is "ArthoAI-Setup.exe" or "Soneca-Setup.exe" : name == assetName)
                && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit)) return parts[0];
        }
        throw new Exception("Verificação de integridade indisponível.");
    }
    public static async Task<DownloadedUpdate> Download(ReleaseUpdate update, IProgress<int> progress, CancellationToken cancellation)
    {
        if (!OfficialAsset(update.Installer) || !OfficialAsset(update.Checksums)) throw new Exception("Origem de atualização inválida.");
        if (update.Size is <= 0 or > MaxSetupBytes) throw new IOException("Tamanho do instalador inválido.");
        string assetName = Path.GetFileName(update.Installer.LocalPath);
        using var manifest = await OpenAsset(update.Checksums, cancellation).ConfigureAwait(false);
        string hash = ExpectedHash(await manifest.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false), assetName);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soneca", "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, assetName);
        try
        {
            using var response = await OpenAsset(update.Installer, cancellation).ConfigureAwait(false);
            await using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            await using var output = File.Create(path);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            long received = 0;
            int percent = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
            {
                received += count;
                if (received > update.Size) throw new IOException("O instalador excede o tamanho publicado.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false);
                digest.AppendData(buffer, 0, count);
                int next = (int)(received * 100 / update.Size);
                if (next != percent) { percent = next; progress.Report(percent); }
            }
            if (received != update.Size) throw new IOException("O download do instalador ficou incompleto.");
            if (!Convert.ToHexString(digest.GetHashAndReset()).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("O arquivo baixado falhou na verificação de integridade.");
            return new DownloadedUpdate(path, hash);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(folder);
            throw;
        }
    }
    private static async Task<HttpResponseMessage> OpenAsset(Uri address, CancellationToken cancellation)
    {
        for (int hop = 0; hop < 5; hop++)
        {
            var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK) return response;
            using (response)
            {
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is null)
                    throw new HttpRequestException($"O GitHub retornou {(int)response.StatusCode}.");
                address = new Uri(address, response.Headers.Location);
                if (!SecureUri(address) || address.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new IOException("O destino do download não pertence ao canal oficial.");
            }
        }
        throw new IOException("Não foi possível resolver o download do GitHub.");
    }
    public static async Task<bool> Verify(DownloadedUpdate update)
    {
        if (!File.Exists(update.Path)) return false;
        await using var stream = File.OpenRead(update.Path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false))
            .Equals(update.Hash, StringComparison.OrdinalIgnoreCase);
    }
    public static void Install(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /UPDATE=1") { UseShellExecute = true });
        if (process is null) throw new IOException("O Windows não conseguiu abrir o instalador.");
    }
}
