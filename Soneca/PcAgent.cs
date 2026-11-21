using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace Soneca;

internal sealed class PcAgent : IAsyncDisposable
{
    private const int Port = 47835;
    private const string Prefix = "artho-pc:";
    private readonly X509Certificate2 certificate;
    private readonly LanAdapter adapter;
    private readonly Action<PcAgentSettings> persist;
    private readonly Func<bool> powerOff;
    private readonly object sync = new();
    private WebApplication? app;
    private PcAgentSettings settings;
    private string? invite;
    private DateTimeOffset inviteExpires;

    private PcAgent(PcAgentSettings settings, LanAdapter adapter, X509Certificate2 certificate,
        Action<PcAgentSettings> persist, Func<bool> powerOff)
    {
        this.settings = settings;
        this.adapter = adapter;
        this.certificate = certificate;
        this.persist = persist;
        this.powerOff = powerOff;
    }

    public static PcAgentSettings Configure(LanAdapter selected, PcAgentSettings? previous)
    {
        if (previous?.Address == selected.Address.ToString() && previous.Certificate.Length != 0)
            return previous with { Enabled = true };

        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Artho AI PC local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(selected.Address);
        request.CertificateExtensions.Add(san.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
        return new PcAgentSettings(previous?.Id ?? Guid.NewGuid(), selected.Address.ToString(),
            generated.Export(X509ContentType.Pfx), null, true);
    }

    public static async Task<PcAgent> StartAsync(PcAgentSettings settings,
        Action<PcAgentSettings>? persist = null, Func<bool>? powerOff = null)
    {
        var network = PcLan.Adapters().FirstOrDefault(entry => entry.Address.ToString() == settings.Address)
            ?? throw new InvalidOperationException("O endereço de rede deste PC mudou. Selecione o endereço atual e pareie novamente.");
        PcLan.MagicPacket(network.Mac);
        var certificate = X509CertificateLoader.LoadPkcs12(settings.Certificate, null);
        var agent = new PcAgent(settings, network, certificate, persist ?? SaveSettings, powerOff ?? RequestShutdown);
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 0;
                options.Limits.MaxConcurrentConnections = 16;
                options.Listen(network.Address, Port, listener =>
                {
                    listener.Protocols = HttpProtocols.Http1;
                    listener.UseHttps(certificate);
                });
            });
            agent.app = builder.Build();
            agent.app.MapPost("/v1/pc/pair", agent.Accept);
            agent.app.MapGet("/v1/pc/status", agent.Status);
            agent.app.MapPost("/v1/pc/shutdown", agent.Shutdown);
            await agent.app.StartAsync().ConfigureAwait(false);
            return agent;
        }
        catch
        {
            await agent.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public string NewInvite()
    {
        lock (sync)
        {
            invite = Secret();
            inviteExpires = DateTimeOffset.UtcNow.AddMinutes(3);
            var data = new PcInvite(settings.Id, settings.Address, certificate.GetCertHashString(HashAlgorithmName.SHA256), invite);
            return Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(data));
        }
    }

    public void Revoke()
    {
        lock (sync)
        {
            var revoked = settings with { ControllerToken = null, Enabled = false };
            persist(revoked);
            settings = revoked;
            invite = null;
        }
    }

    private IResult Accept(HttpContext context)
    {
        lock (sync)
        {
            if (!settings.Enabled || DateTimeOffset.UtcNow >= inviteExpires || !Authorized(context, invite))
                return Results.Unauthorized();
            var updated = settings with { ControllerToken = Secret() };
            persist(updated);
            settings = updated;
            invite = null;
            return Results.Json(new PcAccepted(settings.Id, Environment.MachineName, adapter.Mac, settings.ControllerToken!));
        }
    }

    private IResult Status(HttpContext context)
    {
        lock (sync)
            return settings.Enabled && Authorized(context, settings.ControllerToken)
                ? Results.Json(new { settings.Id })
                : Results.Unauthorized();
    }

    private IResult Shutdown(HttpContext context)
    {
        lock (sync)
        {
            if (!settings.Enabled || !Authorized(context, settings.ControllerToken)) return Results.Unauthorized();
            return powerOff() ? Results.Ok() : Results.StatusCode(503);
        }
    }

    private static void SaveSettings(PcAgentSettings settings) => PcStore.Update(state => state.Agent = settings);

    private static bool RequestShutdown()
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/s /t 0")
            { UseShellExecute = false, CreateNoWindow = true });
        return process is not null && process.WaitForExit(2500) && process.ExitCode == 0;
    }

    private bool Authorized(HttpContext context, string? token)
    {
        if (token is null || context.Connection.RemoteIpAddress is not { } remote || !PcLan.SameSubnet(adapter, remote))
            return false;
        var provided = Encoding.ASCII.GetBytes(context.Request.Headers.Authorization.ToString());
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes("Bearer " + token), provided);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (app is null) return;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await app.StopAsync(cancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            finally { await app.DisposeAsync().ConfigureAwait(false); }
        }
        finally { certificate.Dispose(); }
    }

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    internal static PcInvite ParseInvite(string text)
    {
        text = text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal) || text.Length > 600)
            throw new ArgumentException("Cole o código completo do outro PC.");
        var data = JsonSerializer.Deserialize<PcInvite>(Convert.FromBase64String(text[Prefix.Length..]));
        if (data is null || data.Id == Guid.Empty || !IPAddress.TryParse(data.Address, out var address) ||
            !PcLan.IsPrivate(address) || data.Address != address.ToString() ||
            !ValidSecret(data.Pin) || !ValidSecret(data.Token))
            throw new ArgumentException("Código de PC inválido.");
        return data;
    }

    public static async Task<PairedPc> PairAsync(PcInvite invite)
    {
        PcLan.For(IPAddress.Parse(invite.Address));
        using var result = await Send(invite.Address, invite.Pin, invite.Token, HttpMethod.Post, "/v1/pc/pair").ConfigureAwait(false);
        var accepted = await JsonSerializer.DeserializeAsync<PcAccepted>(
            await result.Content.ReadAsStreamAsync().ConfigureAwait(false),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).ConfigureAwait(false);
        if (accepted is null || accepted.Id != invite.Id || string.IsNullOrWhiteSpace(accepted.Name) ||
            accepted.Name.Length > 64 || !ValidSecret(accepted.Token))
            throw new IOException("O outro PC enviou dados de pareamento inválidos.");
        PcLan.MagicPacket(accepted.Mac);
        return new PairedPc(accepted.Id, accepted.Name, invite.Address, accepted.Mac, invite.Pin, accepted.Token, DateTimeOffset.Now);
    }

    public static async Task CheckAsync(PairedPc pc)
    {
        PcLan.For(IPAddress.Parse(pc.Address));
        using var result = await Send(pc.Address, pc.Pin, pc.Token, HttpMethod.Get, "/v1/pc/status").ConfigureAwait(false);
        using var data = JsonDocument.Parse(await result.Content.ReadAsStreamAsync().ConfigureAwait(false));
        if (data.RootElement.GetProperty("id").GetGuid() != pc.Id) throw new IOException("O PC não corresponde ao pareamento.");
    }

    public static async Task ShutdownAsync(PairedPc pc)
    {
        PcLan.For(IPAddress.Parse(pc.Address));
        using var result = await Send(pc.Address, pc.Pin, pc.Token, HttpMethod.Post, "/v1/pc/shutdown").ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> Send(string address, string fingerprint, string token, HttpMethod method, string path)
    {
        var pin = Convert.FromHexString(fingerprint);
        using var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
                cert is not null && (errors & (SslPolicyErrors.RemoteCertificateNameMismatch |
                    SslPolicyErrors.RemoteCertificateNotAvailable)) == 0 &&
                cert.NotBefore <= DateTime.Now && cert.NotAfter >= DateTime.Now &&
                CryptographicOperations.FixedTimeEquals(pin, cert.GetCertHash(HashAlgorithmName.SHA256))
        }) { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 8192 };
        using var request = new HttpRequestMessage(method, $"https://{address}:{Port}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var result = await client.SendAsync(request).ConfigureAwait(false);
        if (result.IsSuccessStatusCode) return result;
        var status = result.StatusCode;
        result.Dispose();
        throw new IOException(status == HttpStatusCode.Unauthorized
            ? "O código expirou ou a autorização deste PC foi revogada."
            : $"O outro PC não aceitou a solicitação ({(int)status}).");
    }

    private static bool ValidSecret(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
