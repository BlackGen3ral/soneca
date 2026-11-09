using System.Net;

namespace Soneca;

// Stable boundaries for optional integrations. Implementations are added only when a real
// device or service has been configured and can be checked.
public enum IntegrationAvailability { Ready, NotConfigured, Unavailable }
public sealed record IntegrationStatus(string Name, IntegrationAvailability Availability, string Detail);
public sealed record ComputerNode(string Name, string Address, string? MacAddress, int Slot);
public sealed record ComputerNodeStatus(ComputerNode Computer, IntegrationAvailability Availability, DateTimeOffset? CheckedAt);
public sealed record DailyBriefing(DateOnly Date, IReadOnlyList<string> Items, string Source);
public sealed record CameraDevice(string Name, Uri Endpoint, string Provider);

public sealed class RemoteComputerConfiguration
{
    public const int MaximumComputers = 3;
    public IReadOnlyList<ComputerNode> Computers { get; }

    public RemoteComputerConfiguration(IEnumerable<ComputerNode> computers)
    {
        var configured = computers.OrderBy(x => x.Slot).ToArray();
        if (configured.Length > MaximumComputers) throw new ArgumentOutOfRangeException(nameof(computers), "O Artho AI aceita até três computadores remotos.");
        if (configured.Any(x => x.Slot is < 1 or > MaximumComputers)) throw new ArgumentOutOfRangeException(nameof(computers), "Use posições de 1 a 3.");
        if (configured.Select(x => x.Slot).Distinct().Count() != configured.Length) throw new ArgumentException("Cada computador precisa de uma posição única.", nameof(computers));
        if (configured.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != configured.Length) throw new ArgumentException("Os nomes dos computadores devem ser únicos.", nameof(computers));
        Computers = configured;
    }
}

public interface ILocalWindowsAgent
{
    LocalComputerSnapshot ReadSnapshot();
}

public interface IRemoteComputerService
{
    Task<IReadOnlyList<ComputerNodeStatus>> CheckAsync(IReadOnlyList<ComputerNode> computers, CancellationToken cancellationToken);
    Task WakeOnLanAsync(string macAddress, IPAddress broadcastAddress, CancellationToken cancellationToken);
}

public interface IBusinessBriefingProvider
{
    Task<DailyBriefing?> GetBriefingAsync(DateOnly date, CancellationToken cancellationToken);
}

public interface ICameraIntegration
{
    Task<IntegrationStatus> CheckAsync(CameraDevice camera, CancellationToken cancellationToken);
}
