using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Soneca;

internal sealed record InternetShutdownCommand(long Issued, string? Signature);

internal sealed class InternetShutdown(string tokenHash)
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri("https://artho-ai-power.arthurgabrielfera.workers.dev"),
        MaxResponseContentBufferSize = 4096,
        Timeout = TimeSpan.FromSeconds(5)
    };
    private readonly byte[] key = Convert.FromHexString(tokenHash);

    internal bool MatchesPairing(string hash) => string.Equals(hash, tokenHash, StringComparison.Ordinal);

    internal async Task<InternetShutdownCommand?> PollAsync(CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/pc/poll");
        request.Headers.Add("Authorization", "Bearer " + Derive("pc"));
        request.Headers.Add("X-Artho-Phone-Hash", Digest(Derive("phone")));
        using var reply = await Client.SendAsync(request, cancellation);
        reply.EnsureSuccessStatusCode();
        return (await reply.Content.ReadFromJsonAsync<PollReply>(cancellation))?.Command;
    }

    internal async Task AcknowledgeAsync(long issued, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/pc/receipt");
        request.Headers.Add("Authorization", "Bearer " + Derive("pc"));
        request.Headers.Add("X-Artho-Issued", issued.ToString(CultureInfo.InvariantCulture));
        using var reply = await Client.SendAsync(request, timeout.Token);
        reply.EnsureSuccessStatusCode();
    }

    internal bool CanExecute(InternetShutdownCommand command, long now, long lastIssued)
    {
        if (command.Issued <= lastIssued || command.Issued < now - 30_000 ||
            command.Issued > now + 5_000 || command.Signature is not { Length: 64 } signature) return false;
        var expected = Sign(command.Issued);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature));
    }

    internal string Sign(long issued) => Derive("shutdown:" + issued.ToString(CultureInfo.InvariantCulture));

    private string Derive(string purpose) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes("artho-internet-power-v1:" + purpose)));

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(value)));

    private sealed record PollReply(InternetShutdownCommand? Command);
}
