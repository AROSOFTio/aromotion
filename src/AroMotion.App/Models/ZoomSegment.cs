namespace AroMotion.App.Models;

public sealed record ZoomSegment(
    long StartMs,
    long EndMs,
    int FocusX,
    int FocusY,
    double Scale,
    string Easing,
    string Source);
