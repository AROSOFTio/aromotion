using System.Text.Json;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed record CursorSample(long TimestampMs, double X, double Y, double VelocityX, double VelocityY);
public sealed record ClickEffectFrame(long TimestampMs, double X, double Y, string Button, double Progress, ClickRingStyle Style, string Color, double Radius, double Opacity);

public sealed class CursorMotionEngine
{
    public async Task<IReadOnlyList<CursorSample>> ReconstructAsync(
        string eventsPath,
        CursorEffectSettings settings,
        int targetFps = 60,
        CancellationToken cancellationToken = default)
    {
        var events = await ReadEventsAsync(eventsPath, cancellationToken);
        var moves = events
            .Where(e => e.Type == "mouse_move" && e.X is not null && e.Y is not null)
            .OrderBy(e => e.TimestampMs)
            .ToList();

        if (moves.Count == 0)
            return Array.Empty<CursorSample>();

        var stepMs = Math.Max(1.0, 1000.0 / Math.Clamp(targetFps, 1, 240));
        var endMs = moves[^1].TimestampMs;
        var result = new List<CursorSample>((int)(endMs / stepMs) + 2);
        var sourceIndex = 0;
        double smoothX = moves[0].X!.Value;
        double smoothY = moves[0].Y!.Value;
        double previousX = smoothX;
        double previousY = smoothY;
        double alpha = settings.SmoothMovement
            ? Math.Clamp(1.0 - settings.Smoothing * 0.82, 0.08, 1.0)
            : 1.0;

        for (double t = moves[0].TimestampMs; t <= endMs; t += stepMs)
        {
            while (sourceIndex + 1 < moves.Count && moves[sourceIndex + 1].TimestampMs <= t)
                sourceIndex++;

            var a = moves[sourceIndex];
            var b = sourceIndex + 1 < moves.Count ? moves[sourceIndex + 1] : a;
            var span = Math.Max(1, b.TimestampMs - a.TimestampMs);
            var local = Math.Clamp((t - a.TimestampMs) / span, 0.0, 1.0);
            var eased = local * local * (3 - 2 * local);
            var rawX = Lerp(a.X!.Value, b.X!.Value, eased);
            var rawY = Lerp(a.Y!.Value, b.Y!.Value, eased);

            smoothX += (rawX - smoothX) * alpha;
            smoothY += (rawY - smoothY) * alpha;
            var vx = (smoothX - previousX) / stepMs * 1000.0;
            var vy = (smoothY - previousY) / stepMs * 1000.0;

            result.Add(new CursorSample((long)Math.Round(t), smoothX, smoothY, vx, vy));
            previousX = smoothX;
            previousY = smoothY;
        }

        return result;
    }

    public async Task<IReadOnlyList<ClickEffectFrame>> BuildClickFramesAsync(
        string eventsPath,
        CursorEffectSettings settings,
        int targetFps = 60,
        CancellationToken cancellationToken = default)
    {
        var events = await ReadEventsAsync(eventsPath, cancellationToken);
        var clicks = events
            .Where(e => e.Type == "mouse_click" && e.X is not null && e.Y is not null)
            .OrderBy(e => e.TimestampMs)
            .ToList();

        var frames = new List<ClickEffectFrame>();
        var stepMs = Math.Max(1.0, 1000.0 / Math.Clamp(targetFps, 1, 240));
        var duration = Math.Max(50, settings.ClickAnimationMs);

        foreach (var click in clicks)
        {
            var isRight = string.Equals(click.Button, "right", StringComparison.OrdinalIgnoreCase);
            var style = isRight ? settings.RightClickStyle : settings.LeftClickStyle;
            var color = isRight ? settings.RightClickColor : settings.LeftClickColor;
            if (style == ClickRingStyle.None) continue;

            for (double local = 0; local <= duration; local += stepMs)
            {
                var p = Math.Clamp(local / duration, 0.0, 1.0);
                var eased = p * p * (3 - 2 * p);
                var radius = style switch
                {
                    ClickRingStyle.DoubleRing => 12 + 34 * eased,
                    ClickRingStyle.FilledFlash => 18 + 14 * eased,
                    ClickRingStyle.Pulse => 14 + 26 * Math.Sin(Math.PI * Math.Min(1.0, p)),
                    _ => 10 + 38 * eased
                };
                var opacity = style == ClickRingStyle.FilledFlash
                    ? Math.Max(0, 0.78 * (1 - p))
                    : Math.Max(0, 1 - eased);

                frames.Add(new ClickEffectFrame(
                    click.TimestampMs + (long)Math.Round(local),
                    click.X!.Value,
                    click.Y!.Value,
                    click.Button ?? "left",
                    p,
                    style,
                    color,
                    radius,
                    opacity));
            }
        }

        return frames;
    }

    public double MotionBlurRadius(CursorSample sample, CursorEffectSettings settings)
    {
        if (!settings.MotionBlur) return 0;
        var speed = Math.Sqrt(sample.VelocityX * sample.VelocityX + sample.VelocityY * sample.VelocityY);
        return Math.Clamp(speed / 1200.0 * settings.MotionBlurStrength * 12.0, 0.0, 24.0);
    }

    private static async Task<List<CaptureEvent>> ReadEventsAsync(string path, CancellationToken cancellationToken)
    {
        var result = new List<CaptureEvent>();
        if (!File.Exists(path)) return result;
        await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<CaptureEvent>(line);
                if (e is not null) result.Add(e);
            }
            catch (JsonException)
            {
            }
        }
        return result;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
