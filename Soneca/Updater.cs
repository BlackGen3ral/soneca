using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;

namespace Soneca;
public record ReleaseUpdate(Version Version, Uri Installer, Uri Checksums);
public static class Updater
{
    public const string Repo = "BlackGen3ral/soneca";
    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    private static readonly HttpClient client = CreateClient();
    private static HttpClient CreateClient() { var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("Soneca/1.0"); return c; }
    public static bool OfficialAsset(Uri uri) => uri.Scheme == "https" && uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/" + Repo + "/releases/download/", StringComparison.Ordinal);
    public static async Task<ReleaseUpdate?> Check()
    {
        using var response = await client.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v'), out var version) || version <= Current) return null;
        Uri? installer = null, checksum = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            if (name == "Soneca-Setup.exe") installer = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (name == "SHA256SUMS.txt") checksum = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        }
        if (installer is null || checksum is null || !OfficialAsset(installer) || !OfficialAsset(checksum)) throw new Exception("A versão publicada está incompleta.");
        return new ReleaseUpdate(version, installer, checksum);
    }
    public static string ExpectedHash(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == "Soneca-Setup.exe" && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit)) return parts[0];
        }
        throw new Exception("Verificação de integridade indisponível.");
    }
    public static async Task<string> Download(ReleaseUpdate update)
    {
        if (!OfficialAsset(update.Installer) || !OfficialAsset(update.Checksums)) throw new Exception("Origem de atualização inválida.");
        string hash = ExpectedHash(await client.GetStringAsync(update.Checksums));
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Soneca", "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Soneca-Setup.exe");
        using (var response = await client.GetAsync(update.Installer, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(path); await response.Content.CopyToAsync(file);
        }
        await using var input = File.OpenRead(path);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(input));
        if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) { input.Close(); File.Delete(path); throw new Exception("O arquivo baixado falhou na verificação de integridade."); }
        return path;
    }
    public static void Install(string path) => Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /UPDATE=1") { UseShellExecute = true });
}
