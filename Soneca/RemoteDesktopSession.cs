using System.IO;

namespace Soneca;

internal sealed record DesktopFrame(byte[] Image, int PointerX, int PointerY, string? Monitor = null,
    int MonitorNumber = 1, int MonitorCount = 1);

internal readonly record struct DesktopFrameProfile(int LongEdge, int Quality, int IntervalMs, bool FastScaling)
{
    public static DesktopFrameProfile Detail => new(1280, 63, 50, false);

    public static DesktopFrameProfile FromQuery(string value) => value switch
    {
        "motion" => new(960, 52, 33, true),
        "light" => new(768, 46, 40, true),
        "low" => new(640, 40, 50, true),
        _ => Detail
    };
}
internal sealed record DesktopInput(string? Action, int X = 0, int Y = 0, string? Value = null, int Delta = 0,
    string? Monitor = null, MonitorEdge Edge = MonitorEdge.None)
{
    public bool Valid => (Monitor is null or { Length: > 0 and <= 32 }) &&
        Edge is >= MonitorEdge.None and <= MonitorEdge.Bottom && (Action switch
    {
        "move" or "click" or "right" or "double" or "down" or "up" =>
            X is >= 0 and <= 65535 && Y is >= 0 and <= 65535,
        "scroll" => Delta is -3 or -2 or -1 or 1 or 2 or 3,
        "key" => Value is "Enter" or "Backspace" or "Escape" or "Tab" or "Left" or "Right" or "Up" or "Down",
        "text" => Value is { Length: > 0 and <= 256 } && !Value.Any(char.IsControl),
        _ => false
    });
}

internal interface IDesktopSurface
{
    bool IsAvailable();
    DesktopFrame Capture(bool followMonitor, DesktopFrameProfile profile);
    void Apply(DesktopInput input, bool followMonitor);
    void Release();
}

internal sealed class RemoteDesktopSession(IDesktopSurface surface, TimeProvider? time = null)
{
    private static readonly TimeSpan ViewerTimeout = TimeSpan.FromSeconds(20);
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly object captureGate = new();
    private bool enabled;
    private Guid? lease;
    private DateTimeOffset lastSeen;
    private DateTimeOffset lastDrag;
    private DateTimeOffset frameAt;
    private bool held;
    private bool followMonitor;
    private DesktopFrame? frame;
    private DesktopFrameProfile frameProfile;
    public event Action? ViewerChanged;

    public void Enable()
    {
        lock (gate)
        {
            enabled = true;
        }
    }

    public void Disable()
    {
        lock (gate)
        {
            ClearViewer();
            enabled = false;
        }
    }

    public (bool Enabled, bool Viewing) Status()
    {
        lock (gate)
        {
            Sweep();
            return (enabled, lease is not null);
        }
    }

    public Guid? Connect(bool multiMonitor = false)
    {
        lock (gate)
        {
            Sweep();
            if (!enabled || !surface.IsAvailable()) return null;
            ClearViewer();
            followMonitor = multiMonitor;
            lastSeen = clock.GetUtcNow();
            lease = Guid.NewGuid();
            ViewerChanged?.Invoke();
            return lease;
        }
    }

    public void Disconnect(Guid client)
    {
        lock (gate)
            if (lease == client) ClearViewer();
    }

    public DesktopFrame? Read(Guid client, DesktopFrameProfile? profile = null)
    {
        var requested = profile ?? DesktopFrameProfile.Detail;
        // Encoding a frame cannot delay mouse and keyboard events.
        lock (captureGate)
        {
            DateTimeOffset started;
            bool trackMonitor;
            lock (gate)
            {
                Sweep();
                if (lease != client) return null;
                started = lastSeen = clock.GetUtcNow();
                if (frame is not null && frameProfile == requested &&
                    started - frameAt < TimeSpan.FromMilliseconds(requested.IntervalMs)) return frame;
                trackMonitor = followMonitor;
            }
            var captured = surface.Capture(trackMonitor, requested);
            lock (gate)
            {
                Sweep();
                if (lease != client) return null;
                frame = captured;
                frameProfile = requested;
                frameAt = started;
                return frame;
            }
        }
    }

    public bool Input(Guid client, DesktopInput input)
    {
        lock (gate)
        {
            Sweep();
            if (lease != client || !input.Valid) return false;
            var now = clock.GetUtcNow();
            if (input.Action == "up" && !held) return true;
            if (held && input.Action is not ("move" or "up")) Release();
            try
            {
                // Mark a press before enqueueing it so a partial native operation can be released.
                if (input.Action == "down") held = true;
                surface.Apply(input, followMonitor);
                if (input.Action == "up") held = false;
            }
            catch (IOException) { Release(); throw; }
            lastDrag = lastSeen = now;
            return true;
        }
    }

    private void Sweep()
    {
        if (!enabled || lease is null) return;
        var now = clock.GetUtcNow();
        if (!surface.IsAvailable() || now - lastSeen > ViewerTimeout) ClearViewer();
        else if (held && now - lastDrag > TimeSpan.FromSeconds(2)) Release();
    }

    private void ClearViewer()
    {
        bool hadViewer = lease is not null;
        Release();
        lease = null;
        frame = null;
        if (hadViewer) ViewerChanged?.Invoke();
    }

    private void Release()
    {
        if (!held) return;
        surface.Release();
        held = false;
    }
}
