using System.Text.Json;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class AutoZoomOptions
{
    public bool FromClicks { get; set; } = true;
    public bool FromShortcuts { get; set; } = true;
    public bool FromTyping { get; set; } = true;
    public bool FromFocusEvents { get; set; } = true;
    public bool CursorFollow { get; set; }
    public bool SmartFraming { get; set; } = true;
    public double DefaultScale { get; set; } = 1.75;
    public long LeadInMs { get; set; } = 220;
    public long ZoomInMs { get; set; } = 280;
    public long HoldMs { get; set; } = 950;
    public long ZoomOutMs { get; set; } = 320;
    public long MergeWindowMs { get; set; } = 650;
    public long CursorFollowIntervalMs { get; set; } = 900;
    public string Easing { get; set; } = "cubic-out";
    public string Style { get; set; } = "Focus";
}

public sealed class AutoZoomGenerator
{
    public async Task<IReadOnlyList<ZoomSegment>> GenerateAsync(string eventsPath, AutoZoomOptions? options = null)
    {
        options ??= new AutoZoomOptions();
        if (!File.Exists(eventsPath)) return Array.Empty<ZoomSegment>();
        var events = await LoadEventsAsync(eventsPath);
        var focusFrames = Path.Combine(Path.GetDirectoryName(eventsPath)!, "focus-frames.jsonl");
        if (File.Exists(focusFrames)) events.AddRange(await LoadEventsAsync(focusFrames));

        var segments = new List<ZoomSegment>();
        CaptureEvent? lastPointer = null;
        CaptureEvent? lastSmartFrame = null;
        long lastCursorFollowMs = long.MinValue;

        foreach (var evt in events.OrderBy(x => x.TimestampMs))
        {
            if (evt.Type is "focus_frame" or "focus_change")
            {
                lastSmartFrame = evt;
                if (!options.FromFocusEvents) continue;
            }

            if (evt.Type == "mouse_move" && evt.X.HasValue && evt.Y.HasValue)
            {
                lastPointer = evt;
                if (options.CursorFollow && evt.TimestampMs - lastCursorFollowMs >= options.CursorFollowIntervalMs)
                {
                    lastCursorFollowMs = evt.TimestampMs;
                    AddOrMerge(segments, CreateSegment(evt, options, "cursor-follow", Math.Max(1.2, options.DefaultScale - 0.18)), options);
                }
                continue;
            }

            var generate = evt.Type switch
            {
                "mouse_click" => options.FromClicks && evt.Button == "left",
                "shortcut" => options.FromShortcuts,
                "typing_activity" => options.FromTyping,
                "focus_frame" or "focus_change" => options.FromFocusEvents,
                _ => false
            };
            if (!generate) continue;

            var anchor = evt;
            if ((!anchor.X.HasValue || !anchor.Y.HasValue) && lastPointer is not null)
                anchor = anchor with { X = lastPointer.X, Y = lastPointer.Y };

            if (options.SmartFraming && anchor.FrameWidth is null && lastSmartFrame is not null
                && Math.Abs(anchor.TimestampMs - lastSmartFrame.TimestampMs) <= 900)
            {
                anchor = anchor with
                {
                    FrameX = lastSmartFrame.FrameX,
                    FrameY = lastSmartFrame.FrameY,
                    FrameWidth = lastSmartFrame.FrameWidth,
                    FrameHeight = lastSmartFrame.FrameHeight,
                    WindowTitle = lastSmartFrame.WindowTitle
                };
            }
            if (!anchor.X.HasValue || !anchor.Y.HasValue) continue;

            var source = evt.Type switch
            {
                "mouse_click" => "auto-click",
                "shortcut" => "auto-shortcut-focus",
                "typing_activity" => "auto-typing-focus",
                _ => "auto-focus"
            };
            AddOrMerge(segments, CreateSegment(anchor, options, source, options.DefaultScale), options);
        }
        return segments;
    }

    public ZoomSegment CreateManual(long atMs, int x, int y, double scale = 1.75, string style = "Focus") => new()
    {
        StartMs = Math.Max(0, atMs - 180), EndMs = atMs + 1550, FocusX = x, FocusY = y,
        Scale = Math.Clamp(scale, 1.05, 4.0), Easing = "cubic-in-out", Source = "manual", Style = style,
        ZoomInMs = 280, HoldMs = 950, ZoomOutMs = 320, SmartFrame = false
    };

    private static async Task<List<CaptureEvent>> LoadEventsAsync(string path)
    {
        var result = new List<CaptureEvent>();
        await foreach (var line in File.ReadLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { var evt = JsonSerializer.Deserialize<CaptureEvent>(line); if (evt is not null) result.Add(evt); } catch (JsonException) { }
        }
        return result;
    }

    private static ZoomSegment CreateSegment(CaptureEvent evt, AutoZoomOptions options, string source, double scale)
    {
        var start = Math.Max(0, evt.TimestampMs - options.LeadInMs);
        var segment = new ZoomSegment
        {
            StartMs = start, EndMs = start + options.ZoomInMs + options.HoldMs + options.ZoomOutMs,
            FocusX = evt.X!.Value, FocusY = evt.Y!.Value, Scale = Math.Clamp(scale, 1.05, 4.0),
            Easing = options.Easing, Source = source, Style = options.Style,
            ZoomInMs = options.ZoomInMs, HoldMs = options.HoldMs, ZoomOutMs = options.ZoomOutMs,
            SmartFrame = options.SmartFraming
        };
        if (options.SmartFraming && evt.FrameWidth is > 8 && evt.FrameHeight is > 8)
        {
            segment.FrameX = evt.FrameX; segment.FrameY = evt.FrameY; segment.FrameWidth = evt.FrameWidth; segment.FrameHeight = evt.FrameHeight;
            segment.FocusX = evt.FrameX!.Value + evt.FrameWidth.Value / 2;
            segment.FocusY = evt.FrameY!.Value + evt.FrameHeight.Value / 2;
        }
        return segment;
    }

    private static void AddOrMerge(List<ZoomSegment> list, ZoomSegment incoming, AutoZoomOptions options)
    {
        if (list.Count == 0) { list.Add(incoming); return; }
        var previous = list[^1];
        if (incoming.StartMs - previous.EndMs <= options.MergeWindowMs)
        {
            previous.EndMs = Math.Max(previous.EndMs, incoming.EndMs);
            previous.FocusX = incoming.FocusX; previous.FocusY = incoming.FocusY;
            previous.FrameX = incoming.FrameX; previous.FrameY = incoming.FrameY; previous.FrameWidth = incoming.FrameWidth; previous.FrameHeight = incoming.FrameHeight;
            previous.Source = previous.Source == incoming.Source ? previous.Source : "auto-merged";
            return;
        }
        list.Add(incoming);
    }
}
