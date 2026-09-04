namespace AroMotion.App.Models;

public sealed class ZoomSegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public int FocusX { get; set; }
    public int FocusY { get; set; }
    public double Scale { get; set; } = 1.75;
    public string Easing { get; set; } = "cubic-out";
    public string Source { get; set; } = "manual";
    public string Style { get; set; } = "Focus";
    public long ZoomInMs { get; set; } = 280;
    public long HoldMs { get; set; } = 950;
    public long ZoomOutMs { get; set; } = 320;
    public bool Enabled { get; set; } = true;
    public bool SmartFrame { get; set; } = true;
    public int? FrameX { get; set; }
    public int? FrameY { get; set; }
    public int? FrameWidth { get; set; }
    public int? FrameHeight { get; set; }

    public long DurationMs => Math.Max(1, EndMs - StartMs);

    public ZoomSegment Clone() => (ZoomSegment)MemberwiseClone();
}
