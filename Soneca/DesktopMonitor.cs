namespace Soneca;

internal enum MonitorEdge { None, Left, Right, Top, Bottom }

internal readonly record struct DesktopPoint(int X, int Y);

internal sealed record DesktopMonitor(string Id, int Left, int Top, int Right, int Bottom, bool Primary)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public bool Contains(DesktopPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public DesktopPoint PointFor(DesktopInput input, IReadOnlyList<DesktopMonitor> layout)
    {
        var point = new DesktopPoint(Left + WindowsDesktopSurface.Coordinate(input.X, Width),
            Top + WindowsDesktopSurface.Coordinate(input.Y, Height));
        if (input.Action != "move") return point;
        var crossing = input.Edge switch
        {
            MonitorEdge.Left when input.X == 0 => point with { X = Left - 1 },
            MonitorEdge.Right when input.X == 65535 => point with { X = Right },
            MonitorEdge.Top when input.Y == 0 => point with { Y = Top - 1 },
            MonitorEdge.Bottom when input.Y == 65535 => point with { Y = Bottom },
            _ => point
        };
        return crossing != point && layout.Any(monitor => monitor.Id != Id && monitor.Contains(crossing))
            ? crossing : point;
    }
}
