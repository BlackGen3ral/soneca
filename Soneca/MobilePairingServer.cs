using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace Soneca;

internal sealed class MobilePairingServer(WebApplication app, X509Certificate2 certificate, string payload, DateTimeOffset expires)
    : IAsyncDisposable
{
    public string Payload { get; } = payload;
    public DateTimeOffset Expires { get; } = expires;

    public static IPAddress[] Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
        .Select(entry => entry.Address).Where(IsPrivateIpv4).Distinct().ToArray();

    public static async Task<MobilePairingServer> StartAsync(IPAddress address, Action connected, bool allowDevices = false,
        bool allowLocalPower = false, Action? scheduleChanged = null, Func<PowerCommand, bool>? performPower = null)
    {
        if (!Addresses().Contains(address)) throw new InvalidOperationException("O endereço de rede mudou.");

        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Artho AI local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(address);
        request.CertificateExtensions.Add(san.Build());
        var now = DateTimeOffset.UtcNow;
        using var generated = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(7));
        var certificateBytes = generated.Export(X509ContentType.Pfx);
        X509Certificate2 certificate;
        try { certificate = X509CertificateLoader.LoadPkcs12(certificateBytes, null); }
        finally { CryptographicOperations.ZeroMemory(certificateBytes); }

        WebApplication? app = null;
        try
        {
            var pairing = new byte[68];
            address.GetAddressBytes().CopyTo(pairing, 0);
            certificate.GetCertHash(HashAlgorithmName.SHA256).CopyTo(pairing, 4);
            RandomNumberGenerator.Fill(pairing.AsSpan(36));
            string payload = "artho-ai:" + Convert.ToBase64String(pairing).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            byte[] expected = Encoding.ASCII.GetBytes("Bearer " + Convert.ToHexString(pairing.AsSpan(36)));
            CryptographicOperations.ZeroMemory(pairing);

            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 0;
                options.Listen(address, 47834, listener =>
                {
                    listener.Protocols = HttpProtocols.Http1;
                    listener.UseHttps(certificate);
                });
            });
            app = builder.Build();
            var expires = DateTimeOffset.UtcNow.AddMinutes(3);
            var sync = new object();
            bool paired = false;
            bool Authorized(HttpContext context)
            {
                var actual = Encoding.ASCII.GetBytes(context.Request.Headers.Authorization.ToString());
                if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return false;
                lock (sync)
                {
                    if (!paired)
                    {
                        if (DateTimeOffset.UtcNow >= expires) return false;
                        paired = true;
                        connected();
                    }
                }
                return true;
            }

            app.MapGet("/v1/status", (HttpContext context) =>
            {
                if (!Authorized(context)) return Results.Unauthorized();
                context.Response.Headers.CacheControl = "no-store";
                var pc = LocalComputerAgent.Read();
                return Results.Json(new
                {
                    pc.Name, pc.WindowsVersion, pc.ProcessorCount, pc.MemoryInUseGb, pc.TotalMemoryGb,
                    UptimeHours = Math.Floor(pc.Uptime.TotalHours), pc.NetworkAvailable
                });
            });
            if (allowDevices) MobileDeviceApi.Map(app, Authorized, address);
            if (allowLocalPower)
            {
                var adapter = PcLan.Adapters().First(entry => entry.Address.Equals(address));
                LocalPower.Map(app, context => context.Connection.RemoteIpAddress is { } source &&
                    PcLan.SameSubnet(adapter, source) && Authorized(context), adapter, scheduleChanged, performPower);
            }
            await app.StartAsync().ConfigureAwait(false);
            return new MobilePairingServer(app, certificate, payload, expires);
        }
        catch
        {
            certificate.Dispose();
            if (app is not null) await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

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

    private static bool IsPrivateIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168));
    }
}
