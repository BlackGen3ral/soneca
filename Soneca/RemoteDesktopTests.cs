namespace Soneca;

internal static class RemoteDesktopTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition)
        {
            count++;
            if (!condition) throw new InvalidOperationException($"Falha no teste de tela remota {count}.");
        }

        var surface = new FakeSurface();
        var clock = new FakeClock();
        var desktop = new RemoteDesktopSession(surface, clock);
        Check(desktop.Connect() is null && surface.Captures == 0);
        Check(RemoteDesktopPreference.Resolve(null) && RemoteDesktopPreference.Resolve(1) && !RemoteDesktopPreference.Resolve(0));
        int viewerChanges = 0;
        desktop.ViewerChanged += () => viewerChanges++;
        surface.Available = false;
        desktop.Enable();
        clock.Advance(90);
        Check(desktop.Status() == (true, false) && viewerChanges == 0 && surface.Captures == 0 && surface.AvailabilityChecks == 0);
        Check(desktop.Connect() is null && viewerChanges == 0);
        surface.Available = true;
        var lease = desktop.Connect()!.Value;
        Check(viewerChanges == 1 && surface.Captures == 0);
        Check(desktop.Read(Guid.NewGuid()) is null && surface.Captures == 0);
        Check(desktop.Read(lease) is not null && desktop.Read(lease) is not null && surface.Captures == 1);
        clock.Advance(0.2);
        Check(desktop.Read(lease) is not null && surface.Captures == 2);
        Check(!desktop.Input(lease, new DesktopInput("move", -1, 0)) && surface.Inputs == 0);
        Check(!desktop.Input(lease, new DesktopInput("key", Value: "unknown")) && surface.Inputs == 0);
        Check(desktop.Input(lease, new DesktopInput("down", 4000, 5000)) && surface.Inputs == 1);
        clock.Advance(3);
        desktop.Status();
        Check(surface.Releases == 1 && desktop.Read(lease) is not null);
        Check(desktop.Input(lease, new DesktopInput("down", 6000, 5000)));
        desktop.Disconnect(lease);
        Check(surface.Releases == 2 && desktop.Read(lease) is null && !desktop.Status().Viewing && viewerChanges == 2);
        lease = desktop.Connect()!.Value;
        clock.Advance(9);
        Check(desktop.Read(lease) is null);
        lease = desktop.Connect()!.Value;
        surface.Available = false;
        Check(desktop.Read(lease) is null && desktop.Status().Enabled && desktop.Connect() is null);
        surface.Available = true;
        clock.Advance(60 * 60 * 24);
        lease = desktop.Connect()!.Value;
        using var encoding = new ManualResetEventSlim();
        using var releaseFrame = new ManualResetEventSlim();
        surface.OnCapture = () => { encoding.Set(); releaseFrame.Wait(TimeSpan.FromSeconds(5)); };
        var pendingFrame = Task.Run(() => desktop.Read(lease));
        try
        {
            Check(encoding.Wait(TimeSpan.FromSeconds(5)));
            var command = Task.Run(() => desktop.Input(lease, new DesktopInput("move", 1000, 1000)));
            Check(command.Wait(TimeSpan.FromSeconds(2)) && command.Result);
            desktop.Disable();
        }
        finally { releaseFrame.Set(); }
        Check(pendingFrame.Wait(TimeSpan.FromSeconds(5)) && pendingFrame.Result is null);
        Check(desktop.Connect() is null);
        Check(WindowsDesktopSurface.Coordinate(65535, 3840) == 3839 &&
            WindowsDesktopSurface.Coordinate(0, 2160) == 0 &&
            WindowsDesktopSurface.Coordinate(32768, 1920) is >= 958 and <= 961);
        return count;
    }

    private sealed class FakeSurface : IDesktopSurface
    {
        public bool Available = true;
        public int Captures, Inputs, Releases, AvailabilityChecks;
        public Action? OnCapture;
        public bool IsAvailable() { AvailabilityChecks++; return Available; }
        public DesktopFrame Capture() { Captures++; OnCapture?.Invoke(); return new DesktopFrame([1], 0, 0); }
        public void Apply(DesktopInput input) => Inputs++;
        public void Release() => Releases++;
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset current = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(double seconds) => current = current.AddSeconds(seconds);
    }
}
