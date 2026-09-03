namespace AroMotion.App.Models;

public sealed record CaptureEvent(
    long TimestampMs,
    string Type,
    int? X = null,
    int? Y = null,
    string? Button = null,
    string? Key = null,
    string? Modifiers = null,
    int? Delta = null);
