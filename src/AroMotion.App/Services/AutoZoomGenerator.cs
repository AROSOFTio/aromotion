using System.Text.Json;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class AutoZoomGenerator
{
    private const long LeadInMs = 220;
    private const long HoldAfterClickMs = 1450;
    private const long MergeWindowMs = 700;
    private const double DefaultScale = 1.75;

    public async Task<IReadOnlyList<ZoomSegment>> GenerateAsync(string eventsPath)
    {
        if (!File.Exists(eventsPath))
        {
            return Array.Empty<ZoomSegment>();
        }

        var clicks = new List<CaptureEvent>();

        await foreach (var line in File.ReadLinesAsync(eventsPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            CaptureEvent? captureEvent;
            try
            {
                captureEvent = JsonSerializer.Deserialize<CaptureEvent>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (captureEvent is
                {
                    Type: "mouse_click",
                    Button: "left",
                    X: not null,
                    Y: not null
                })
            {
                clicks.Add(captureEvent);
            }
        }

        if (clicks.Count == 0)
        {
            return Array.Empty<ZoomSegment>();
        }

        var segments = new List<ZoomSegment>();

        foreach (var click in clicks.OrderBy(x => x.TimestampMs))
        {
            var clickX = click.X!.Value;
            var clickY = click.Y!.Value;

            if (segments.Count > 0)
            {
                var previous = segments[^1];
                var previousClickApprox = previous.EndMs - HoldAfterClickMs;

                // Multiple clicks in one short interaction should feel like one
                // intentional camera move, not several nervous zooms.
                if (click.TimestampMs - previousClickApprox <= MergeWindowMs)
                {
                    segments[^1] = previous with
                    {
                        EndMs = Math.Max(previous.EndMs, click.TimestampMs + HoldAfterClickMs),
                        FocusX = clickX,
                        FocusY = clickY
                    };
                    continue;
                }
            }

            segments.Add(new ZoomSegment(
                StartMs: Math.Max(0, click.TimestampMs - LeadInMs),
                EndMs: click.TimestampMs + HoldAfterClickMs,
                FocusX: clickX,
                FocusY: clickY,
                Scale: DefaultScale,
                Easing: "smoothstep",
                Source: "auto-click"));
        }

        return segments;
    }
}
