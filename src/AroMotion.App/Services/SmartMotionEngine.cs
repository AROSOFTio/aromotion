using System.Text.Json;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class SmartMotionOptions
{
    public bool ZoomOnClicks { get; set; } = true;
    public bool ZoomOnShortcuts { get; set; } = true;
    public bool CursorFollow { get; set; }
    public bool SmartFrame { get; set; } = true;
    public bool Auto3DFromClicks { get; set; } = true;
    public double DefaultScale { get; set; } = 1.75;
    public long ZoomInMs { get; set; } = 260;
    public long HoldMs { get; set; } = 1100;
    public long ZoomOutMs { get; set; } = 360;
    public long MergeWindowMs { get; set; } = 520;
    public EasingKind Easing { get; set; } = EasingKind.SpringSoft;
    public ZoomStyle ZoomStyle { get; set; } = ZoomStyle.Focus;
    public Motion3DPreset ThreeDPreset { get; set; } = Motion3DPreset.Focus;
    public double ThreeDIntensity { get; set; } = 0.55;
    public int CanvasWidth { get; set; } = 1920;
    public int CanvasHeight { get; set; } = 1080;
}

public sealed class SmartMotionEngine
{
    public async Task<MotionProject> GenerateAsync(
        string eventsPath,
        SmartMotionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SmartMotionOptions();
        var project = MotionProject.CreateDefault();
        var events = await ReadEventsAsync(eventsPath, cancellationToken);
        if (events.Count == 0)
        {
            return project;
        }

        var moves = events
            .Where(e => e.Type == "mouse_move" && e.X is not null && e.Y is not null)
            .OrderBy(e => e.TimestampMs)
            .ToList();

        var triggers = new List<CaptureEvent>();
        if (options.ZoomOnClicks)
        {
            triggers.AddRange(events.Where(e => e.Type == "mouse_click" && e.Button == "left" && e.X is not null && e.Y is not null));
        }

        if (options.ZoomOnShortcuts)
        {
            foreach (var keyEvent in events.Where(e => e.Type is "shortcut" or "keyboard_shortcut"))
            {
                var anchor = FindNearestPosition(moves, keyEvent.TimestampMs, options.CanvasWidth / 2, options.CanvasHeight / 2);
                triggers.Add(keyEvent with { X = anchor.X, Y = anchor.Y });
            }
        }

        triggers = triggers.OrderBy(t => t.TimestampMs).ToList();
        BuildTriggerZooms(project, triggers, options);

        if (options.CursorFollow)
        {
            BuildCursorFollowZooms(project, moves, options);
        }

        if (options.Auto3DFromClicks)
        {
            BuildAuto3D(project, project.Zooms, options);
        }

        project.Zooms.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        project.Motions3D.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        return project;
    }

    public ZoomClip CreateManualZoom(
        long startMs,
        double focusX,
        double focusY,
        double scale = 1.75,
        long zoomInMs = 260,
        long holdMs = 1100,
        long zoomOutMs = 360,
        EasingKind easing = EasingKind.SpringSoft,
        ZoomStyle style = ZoomStyle.Focus)
        => new(
            Guid.NewGuid(),
            Math.Max(0, startMs),
            Math.Max(40, zoomInMs),
            Math.Max(0, holdMs),
            Math.Max(40, zoomOutMs),
            Math.Clamp(scale, 1.01, 4.0),
            focusX,
            focusY,
            easing,
            style,
            true,
            "manual");

    public Motion3DClip CreateManual3D(
        long startMs,
        long durationMs,
        Motion3DPreset preset,
        double intensity = 0.6,
        EasingKind easing = EasingKind.Cubic)
    {
        var values = PresetValues(preset, intensity);
        return new Motion3DClip(
            Guid.NewGuid(),
            Math.Max(0, startMs),
            Math.Max(120, durationMs),
            preset,
            values.RotateX,
            values.RotateY,
            values.RotateZ,
            values.PanX,
            values.PanY,
            values.DepthZ,
            values.Perspective,
            values.Orbit,
            values.Parallax,
            Math.Clamp(intensity, 0.0, 1.5),
            0,
            easing,
            true,
            0.30,
            preset is Motion3DPreset.Push or Motion3DPreset.Orbit,
            0.18,
            true,
            "manual");
    }

    public ZoomClip MoveZoom(ZoomClip clip, long newStartMs, double focusX, double focusY)
        => clip with { StartMs = Math.Max(0, newStartMs), FocusX = focusX, FocusY = focusY, Source = "edited" };

    public ZoomClip ResizeZoom(ZoomClip clip, long zoomInMs, long holdMs, long zoomOutMs)
        => clip with
        {
            ZoomInMs = Math.Max(40, zoomInMs),
            HoldMs = Math.Max(0, holdMs),
            ZoomOutMs = Math.Max(40, zoomOutMs),
            Source = "edited"
        };

    public ZoomClip SetZoomScaleAndEasing(ZoomClip clip, double scale, EasingKind easing)
        => clip with { Scale = Math.Clamp(scale, 1.01, 4.0), Easing = easing, Source = "edited" };

    public Motion3DClip ApplyPreset(Motion3DClip clip, Motion3DPreset preset, double intensity)
    {
        var values = PresetValues(preset, intensity);
        return clip with
        {
            Preset = preset,
            RotateX = values.RotateX,
            RotateY = values.RotateY,
            RotateZ = values.RotateZ,
            PanX = values.PanX,
            PanY = values.PanY,
            DepthZ = values.DepthZ,
            Perspective = values.Perspective,
            Orbit = values.Orbit,
            Parallax = values.Parallax,
            Intensity = Math.Clamp(intensity, 0.0, 1.5),
            Source = "edited"
        };
    }

    public (double RotateX, double RotateY, double RotateZ, double PanX, double PanY, double DepthZ, double Perspective, double Orbit, double Parallax)
        PresetValues(Motion3DPreset preset, double intensity)
    {
        var k = Math.Clamp(intensity, 0.0, 1.5);
        return preset switch
        {
            Motion3DPreset.Focus => (-2.0 * k, 4.5 * k, 0, 0, 0, 90 * k, 900, 0, 0.08 * k),
            Motion3DPreset.Push => (-3.5 * k, 0, 0, 0, -12 * k, 160 * k, 820, 0, 0.10 * k),
            Motion3DPreset.Tilt => (7.0 * k, -9.0 * k, 1.5 * k, 0, 0, 70 * k, 780, 0, 0.12 * k),
            Motion3DPreset.Reveal => (0, 11.0 * k, -2.0 * k, -42 * k, 0, 85 * k, 760, 0, 0.14 * k),
            Motion3DPreset.Orbit => (-4.0 * k, 9.0 * k, 1.0 * k, 0, 0, 110 * k, 720, 14.0 * k, 0.18 * k),
            Motion3DPreset.Parallax => (-2.0 * k, 6.0 * k, 0, 18 * k, -8 * k, 80 * k, 840, 0, 0.32 * k),
            _ => (0, 0, 0, 0, 0, 0, 1000, 0, 0)
        };
    }

    private static async Task<List<CaptureEvent>> ReadEventsAsync(string path, CancellationToken cancellationToken)
    {
        var list = new List<CaptureEvent>();
        if (!File.Exists(path)) return list;

        await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var item = JsonSerializer.Deserialize<CaptureEvent>(line);
                if (item is not null) list.Add(item);
            }
            catch (JsonException)
            {
                // A single damaged metadata line must not destroy the whole project.
            }
        }
        return list;
    }

    private static void BuildTriggerZooms(MotionProject project, List<CaptureEvent> triggers, SmartMotionOptions options)
    {
        foreach (var trigger in triggers)
        {
            var rawX = trigger.X ?? options.CanvasWidth / 2;
            var rawY = trigger.Y ?? options.CanvasHeight / 2;
            var focus = options.SmartFrame
                ? SmartFrame(rawX, rawY, options.CanvasWidth, options.CanvasHeight)
                : new MotionPoint(rawX, rawY);

            var start = Math.Max(0, trigger.TimestampMs - options.ZoomInMs / 2);
            var candidate = new ZoomClip(
                Guid.NewGuid(),
                start,
                options.ZoomInMs,
                options.HoldMs,
                options.ZoomOutMs,
                Math.Clamp(options.DefaultScale, 1.1, 3.5),
                focus.X,
                focus.Y,
                options.Easing,
                options.SmartFrame ? ZoomStyle.SmartFrame : options.ZoomStyle,
                true,
                trigger.Type == "mouse_click" ? "auto-click" : "auto-shortcut");

            if (project.Zooms.Count > 0)
            {
                var previous = project.Zooms[^1];
                if (candidate.StartMs - previous.EndMs <= options.MergeWindowMs)
                {
                    project.Zooms[^1] = previous with
                    {
                        HoldMs = Math.Max(previous.HoldMs, candidate.EndMs - previous.StartMs - previous.ZoomInMs - previous.ZoomOutMs),
                        FocusX = candidate.FocusX,
                        FocusY = candidate.FocusY,
                        Source = previous.Source + "+merged"
                    };
                    continue;
                }
            }

            project.Zooms.Add(candidate);
        }
    }

    private static void BuildCursorFollowZooms(MotionProject project, List<CaptureEvent> moves, SmartMotionOptions options)
    {
        if (moves.Count < 2) return;
        const long sampleWindowMs = 900;
        var bucketStart = moves[0].TimestampMs;
        var bucket = new List<CaptureEvent>();

        foreach (var move in moves)
        {
            if (move.TimestampMs - bucketStart <= sampleWindowMs)
            {
                bucket.Add(move);
                continue;
            }

            AddCursorFollowClip(project, bucket, bucketStart, options);
            bucket.Clear();
            bucketStart = move.TimestampMs;
            bucket.Add(move);
        }
        AddCursorFollowClip(project, bucket, bucketStart, options);
    }

    private static void AddCursorFollowClip(MotionProject project, List<CaptureEvent> bucket, long startMs, SmartMotionOptions options)
    {
        if (bucket.Count < 2) return;
        var first = bucket[0];
        var last = bucket[^1];
        var dx = (last.X ?? 0) - (first.X ?? 0);
        var dy = (last.Y ?? 0) - (first.Y ?? 0);
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < Math.Min(options.CanvasWidth, options.CanvasHeight) * 0.12) return;

        var focus = SmartFrame(last.X ?? options.CanvasWidth / 2, last.Y ?? options.CanvasHeight / 2, options.CanvasWidth, options.CanvasHeight);
        project.Zooms.Add(new ZoomClip(
            Guid.NewGuid(),
            Math.Max(0, startMs),
            240,
            520,
            300,
            Math.Max(1.25, options.DefaultScale - 0.25),
            focus.X,
            focus.Y,
            EasingKind.Cubic,
            ZoomStyle.CursorFollow,
            true,
            "auto-cursor-follow"));
    }

    private void BuildAuto3D(MotionProject project, List<ZoomClip> zooms, SmartMotionOptions options)
    {
        foreach (var zoom in zooms.Where(z => z.Enabled && z.Source.Contains("click", StringComparison.OrdinalIgnoreCase)))
        {
            var clip = CreateManual3D(zoom.StartMs, Math.Max(280, zoom.ZoomInMs + Math.Min(zoom.HoldMs, 650)), options.ThreeDPreset, options.ThreeDIntensity, zoom.Easing);
            var horizontal = (zoom.FocusX / Math.Max(1, options.CanvasWidth)) - 0.5;
            project.Motions3D.Add(clip with
            {
                RotateY = clip.RotateY + horizontal * 10.0 * options.ThreeDIntensity,
                PanX = clip.PanX - horizontal * 44.0 * options.ThreeDIntensity,
                Source = "auto-click-3d"
            });
        }
    }

    private static MotionPoint SmartFrame(int x, int y, int width, int height)
    {
        // Keep the focus inside a safe frame so zoomed content does not hug an edge.
        // The slight grid snap produces steadier moves for common toolbar/form layouts.
        var marginX = width * 0.16;
        var marginY = height * 0.18;
        var clampedX = Math.Clamp((double)x, marginX, width - marginX);
        var clampedY = Math.Clamp((double)y, marginY, height - marginY);
        var gridX = Math.Max(1.0, width / 24.0);
        var gridY = Math.Max(1.0, height / 14.0);
        return new MotionPoint(
            Math.Round(clampedX / gridX) * gridX,
            Math.Round(clampedY / gridY) * gridY);
    }

    private static MotionPoint FindNearestPosition(List<CaptureEvent> moves, long timestampMs, int fallbackX, int fallbackY)
    {
        if (moves.Count == 0) return new MotionPoint(fallbackX, fallbackY);
        CaptureEvent? best = null;
        var bestDelta = long.MaxValue;
        foreach (var move in moves)
        {
            var delta = Math.Abs(move.TimestampMs - timestampMs);
            if (delta < bestDelta)
            {
                best = move;
                bestDelta = delta;
            }
            if (move.TimestampMs > timestampMs && delta > bestDelta) break;
        }
        return new MotionPoint(best?.X ?? fallbackX, best?.Y ?? fallbackY);
    }
}
