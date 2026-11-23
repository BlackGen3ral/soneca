using System.IO;

namespace Soneca;

internal sealed record DesktopFrame(byte[] Image, int PointerX, int PointerY);
internal sealed record DesktopInput(string? Action, int X = 0, int Y = 0, string? Value = null, int Delta = 0)
{
    public bool Valid => Action switch
    {
        "move" or "click" or "right" or "double" or "down" or "up" =>
            X is >= 0 and <= 65535 && Y is >= 0 and <= 65535,
        "scroll" => Delta is -3 or -2 or -1 or 1 or 2 or 3,
        "key" => Value is "Enter" or "Backspace" or "Escape" or "Tab" or "Left" or "Right" or "Up" or "Down",
        "text" => Value is { Length: > 0 and <= 256 } && !Value.Any(char.IsControl),
        _ => false
    };
}

internal interface IDesktopSurface
{
    bool IsAvailable();
    DesktopFrame Capture();
    void Apply(DesktopInput input);
    void Release();
}

internal sealed class RemoteDesktopSession(IDesktopSurface surface, TimeProvider? time = null)
{
    private static readonly TimeSpan ViewerTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly object captureGate = new();
    private bool enabled;
    private Guid? lease;
    private DateTimeOffset lastSeen;
    private DateTimeOffset lastDrag;
    private DateTimeOffset frameAt;
    private bool held;
    private DesktopFrame? frame;
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

    public Guid? Connect()
    {
        lock (gate)
        {
            Sweep();
            if (!enabled || !surface.IsAvailable()) return null;
            ClearViewer();
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

    public DesktopFrame? Read(Guid client)
    {
        // Encoding a frame cannot delay mouse and keyboard events.
        lock (captureGate)
        {
            DateTimeOffset started;
            lock (gate)
            {
                Sweep();
                if (lease != client) return null;
                started = lastSeen = clock.GetUtcNow();
                if (frame is not null && started - frameAt < FrameInterval) return frame;
            }
            var captured = surface.Capture();
            lock (gate)
            {
                Sweep();
                if (lease != client) return null;
                frame = captured;
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
                surface.Apply(input);
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
