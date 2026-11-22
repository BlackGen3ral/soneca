using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace Soneca;

internal sealed record MobileProfile(string Address, byte[] Certificate, string TokenHash, bool AllowDevices, bool AllowPower);

internal sealed class MobilePairingServer(WebApplication app, X509Certificate2 certificate, string? payload, DateTimeOffset expires,
    Action forget, Func<bool> hasLink)
    : IAsyncDisposable
{
    public string? Payload { get; } = payload;
    public DateTimeOffset Expires { get; } = expires;
    public void Forget() => forget();
    public bool HasLink => hasLink();

    public static IPAddress[] Addresses() => PcLan.Adapters().Select(adapter => adapter.Address).ToArray();

    public static async Task<MobilePairingServer> StartAsync(IPAddress address, Action connected, bool allowDevices = false,
        bool allowLocalPower = false, Action? scheduleChanged = null, Func<PowerCommand, bool>? performPower = null,
        MobileProfile? restored = null, Action? revoked = null, Action<MobileProfile?>? persist = null, int port = 47834)
    {
        var adapter = PcLan.Adapters().FirstOrDefault(candidate => candidate.Address.Equals(address))
            ?? throw new InvalidOperationException("O endereço de rede mudou.");
        if (restored is not null && restored.Address != address.ToString())
            throw new InvalidOperationException("O Wi-Fi ou o endereço de rede mudou.");
        persist ??= profile => PcStore.Update(state => state.Mobile = profile);

        byte[] certificateBytes = restored?.Certificate ?? CreateCertificate(address);
        var certificate = X509CertificateLoader.LoadPkcs12(certificateBytes, null);
        WebApplication? app = null;
        try
        {
            byte[]? inviteToken = restored is null ? RandomNumberGenerator.GetBytes(32) : null;
            string? invitation = inviteToken is null ? null : Invite(address, certificate, inviteToken);
            string? expectedInvite = inviteToken is null ? null : Convert.ToHexString(inviteToken);
            if (inviteToken is not null) CryptographicOperations.ZeroMemory(inviteToken);
            string? activeHash = restored?.TokenHash;
            bool closed = false;
            var expires = DateTimeOffset.UtcNow.AddMinutes(3);
            var sync = new object();

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 0;
                options.Limits.MaxConcurrentConnections = 24;
                options.Listen(address, port, listener =>
                {
                    listener.Protocols = HttpProtocols.Http1;
                    listener.UseHttps(certificate);
                });
            });
            app = builder.Build();

            string? LinkToken(HttpContext context)
            {
                if (context.Connection.RemoteIpAddress is not { } source || !PcLan.SameSubnet(adapter, source)) return null;
                string value = context.Request.Headers.Authorization.ToString();
                return value.Length == 71 && value.StartsWith("Bearer ", StringComparison.Ordinal) && Valid(value[7..])
                    ? value[7..] : null;
            }

            bool Authorized(HttpContext context, bool allowInvitation = false)
            {
                if (LinkToken(context) is not { } token) return false;
                lock (sync)
                {
                    if (closed) return false;
                    if (Equal(activeHash, Hash(token))) return true;
                    return allowInvitation && DateTimeOffset.UtcNow < expires && Equal(expectedInvite, token);
                }
            }

            app.MapPost("/v1/pair/complete", (HttpContext context) =>
            {
                string proposed = context.Request.Headers["X-Artho-Pair-Key"].ToString();
                if (!Valid(proposed) || LinkToken(context) is not { } token) return Results.Unauthorized();
                string nextHash = Hash(proposed);
                lock (sync)
                {
                    if (closed) return Results.Unauthorized();
                    if (Equal(activeHash, Hash(token)) && Equal(activeHash, nextHash))
                        return Results.Json(new { saved = true });
                    if (DateTimeOffset.UtcNow >= expires || !Equal(expectedInvite, token) || Equal(token, proposed))
                        return Results.Unauthorized();
                    persist(new MobileProfile(address.ToString(), certificateBytes, nextHash, allowDevices, allowLocalPower));
                    activeHash = nextHash;
                    expectedInvite = null;
                    connected();
                    return Results.Json(new { saved = true });
                }
            });
            app.MapPost("/v1/pair/revoke", (HttpContext context) =>
            {
                if (LinkToken(context) is not { } token) return Results.Unauthorized();
                lock (sync)
                {
                    if (!Equal(activeHash, Hash(token))) return Results.Unauthorized();
                    ForgetLink();
                    // Respond before releasing the listener.
                    if (revoked is not null) context.Response.OnCompleted(() => { revoked(); return Task.CompletedTask; });
                    return Results.Json(new { revoked = true });
                }
            });
            app.MapGet("/v1/status", (HttpContext context) =>
            {
                if (!Authorized(context, allowInvitation: true)) return Results.Unauthorized();
                context.Response.Headers.CacheControl = "no-store";
                var pc = LocalComputerAgent.Read();
                return Results.Json(new
                {
                    pc.Name, pc.WindowsVersion, pc.ProcessorCount, pc.MemoryInUseGb, pc.TotalMemoryGb,
                    UptimeHours = Math.Floor(pc.Uptime.TotalHours), pc.NetworkAvailable
                });
            });
            if (restored?.AllowDevices ?? allowDevices) MobileDeviceApi.Map(app, context => Authorized(context), address);
            if (restored?.AllowPower ?? allowLocalPower)
                LocalPower.Map(app, context => Authorized(context), adapter, scheduleChanged, performPower);
            await app.StartAsync().ConfigureAwait(false);
            return new MobilePairingServer(app, certificate, invitation, expires, ForgetLink, HasLink);

            bool HasLink()
            {
                lock (sync) return activeHash is not null;
            }

            void ForgetLink()
            {
                lock (sync)
                {
                    if (activeHash is not null) persist(null);
                    closed = true;
                    activeHash = null;
                    expectedInvite = null;
                }
            }
        }
        catch
        {
            certificate.Dispose();
            if (app is not null) await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static byte[] CreateCertificate(IPAddress address)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Artho AI local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(address);
        request.CertificateExtensions.Add(san.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        return generated.Export(X509ContentType.Pfx);
    }

    private static string Invite(IPAddress address, X509Certificate2 certificate, byte[] token)
    {
        var pairing = new byte[68];
        address.GetAddressBytes().CopyTo(pairing, 0);
        certificate.GetCertHash(HashAlgorithmName.SHA256).CopyTo(pairing, 4);
        token.CopyTo(pairing, 36);
        try { return "artho-ai:" + Convert.ToBase64String(pairing).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        finally { CryptographicOperations.ZeroMemory(pairing); }
    }

    private static bool Valid(string token) => token.Length == 64 && token.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private static bool Equal(string? first, string second) => first is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(first), Encoding.ASCII.GetBytes(second));

    public async ValueTask DisposeAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await app.StopAsync(cancellation.Token).ConfigureAwait(false); }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
            certificate.Dispose();
        }
    }
}
