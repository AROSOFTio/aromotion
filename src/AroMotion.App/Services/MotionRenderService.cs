using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class MotionRenderService
{
    public async Task RenderAsync(MotionProjectState project, string outputPath, Action<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(project.SourceVideo) || !File.Exists(project.SourceVideo))
            throw new FileNotFoundException("Source video was not found.", project.SourceVideo);

        var ffmpeg = ResolveTool("ffmpeg.exe", "ffmpeg");
        var ffprobe = ResolveTool("ffprobe.exe", "ffprobe");
        var info = await ProbeAsync(ffprobe, project.SourceVideo);
        project.CanvasWidth = info.Width;
        project.CanvasHeight = info.Height;

        var projectDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(projectDirectory);
        var events = await LoadEventsAsync(project.EventsPath);
        var assPath = Path.Combine(projectDirectory, "aromotion-cursor.ass");
        await CursorAssWriter.WriteAsync(assPath, info.Width, info.Height, events, project.Cursor);

        progress?.Invoke("Building non-destructive motion graph…");
        var graph = BuildFilterGraph(project, info, events, File.Exists(assPath) && !project.Cursor.HideCursor);
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            WorkingDirectory = projectDirectory
        };
        Add(start, "-hide_banner", "-y", "-i", project.SourceVideo, "-filter_complex", graph,
            "-map", "[outv]", "-map", "0:a?",
            "-c:v", "libx264", "-preset", "medium", "-crf", "12", "-pix_fmt", "yuv444p",
            "-c:a", "aac", "-b:a", "256k", "-movflags", "+faststart", outputPath);

        using var process = new Process { StartInfo = start };
        var tail = new Queue<string>();
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (tail)
            {
                tail.Enqueue(e.Data);
                while (tail.Count > 30) tail.Dequeue();
            }
        };
        if (!process.Start()) throw new InvalidOperationException("FFmpeg could not start.");
        process.BeginErrorReadLine();
        progress?.Invoke("Rendering zoom, 3D, cursor, spotlight and privacy effects…");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            string details;
            lock (tail) details = string.Join(Environment.NewLine, tail);
            throw new InvalidOperationException("Motion render failed.\n\n" + details);
        }
        progress?.Invoke("Render complete");
    }

    private static string BuildFilterGraph(MotionProjectState project, VideoInfo info, List<CaptureEvent> events, bool cursorAss)
    {
        var sb = new StringBuilder();
        var zoom = BuildZoomExpressions(project.Zooms.Where(x => x.Enabled).OrderBy(x => x.StartMs).ToList(), info.Width, info.Height);
        sb.Append("[0:v]setpts=PTS-STARTPTS,")
          .Append("scale=w='trunc(iw*(").Append(zoom.Scale).Append(")/2)*2':h='trunc(ih*(").Append(zoom.Scale).Append(")/2)*2':eval=frame,")
          .Append("crop=").Append(info.Width).Append(':').Append(info.Height)
          .Append(":x='max(0,min(iw-").Append(info.Width).Append(",(").Append(zoom.X).Append(")*(iw/").Append(info.Width).Append(")-").Append(info.Width / 2).Append("))'")
          .Append(":y='max(0,min(ih-").Append(info.Height).Append(",(").Append(zoom.Y).Append(")*(ih/").Append(info.Height).Append(")-").Append(info.Height / 2).Append("))'[z0];");

        var last = "z0";
        var motions = project.Motions3D.Where(x => x.Enabled).OrderBy(x => x.StartMs).ToList();
        if (motions.Count > 0)
        {
            var perspective = BuildPerspectiveExpressions(motions, info.Width, info.Height);
            var rotate = BuildMotionValue(motions, m => m.RotateZ * m.Intensity, "0");
            var depth = BuildMotionValue(motions, m => m.Depth * m.Intensity / 100.0, "0");
            var panX = BuildMotionValue(motions, m => m.PanX * m.Intensity, "0");
            var panY = BuildMotionValue(motions, m => m.PanY * m.Intensity, "0");
            sb.Append('[').Append(last).Append("]perspective=x0='").Append(perspective.X0).Append("':y0='").Append(perspective.Y0)
              .Append("':x1='").Append(perspective.X1).Append("':y1='").Append(perspective.Y1)
              .Append("':x2='").Append(perspective.X2).Append("':y2='").Append(perspective.Y2)
              .Append("':x3='").Append(perspective.X3).Append("':y3='").Append(perspective.Y3)
              .Append("':sense=destination:eval=frame:interpolation=cubic,")
              .Append("rotate=angle='(").Append(rotate).Append(")*PI/180':ow=iw:oh=ih:fillcolor=black,")
              .Append("scale=w='trunc(iw*(1+(").Append(depth).Append("))/2)*2':h='trunc(ih*(1+(").Append(depth).Append("))/2)*2':eval=frame,")
              .Append("crop=").Append(info.Width).Append(':').Append(info.Height)
              .Append(":x='max(0,min(iw-").Append(info.Width).Append(",(iw-").Append(info.Width).Append(")/2-(").Append(panX).Append(")))'")
              .Append(":y='max(0,min(ih-").Append(info.Height).Append(",(ih-").Append(info.Height).Append(")/2-(").Append(panY).Append(")))'[m0];");
            last = "m0";
        }

        var spotlightIndex = 0;
        foreach (var spot in project.Spotlights.Where(x => x.Enabled))
        {
            var next = "s" + spotlightIndex;
            if (spot.Shape.Equals("Circle", StringComparison.OrdinalIgnoreCase))
            {
                var center = ResolveTrackedCenter(spot.X, spot.Y, spot.FollowCursor, spot.StartMs, spot.EndMs, events);
                var angle = F(Math.Clamp(0.25 + spot.Darkness * 1.1, 0.25, 1.45));
                sb.Append('[').Append(last).Append("]vignette=angle=").Append(angle)
                  .Append(":x0='").Append(center.X).Append("':y0='").Append(center.Y)
                  .Append("':eval=frame:enable='between(t,").Append(S(spot.StartMs)).Append(',').Append(S(spot.EndMs)).Append(")'[").Append(next).Append("]; ");
            }
            else
            {
                var alpha = F(Math.Clamp(spot.Darkness, 0, .95));
                var left = Math.Max(0, spot.X - spot.Width / 2);
                var top = Math.Max(0, spot.Y - spot.Height / 2);
                var right = Math.Min(info.Width, left + spot.Width);
                var bottom = Math.Min(info.Height, top + spot.Height);
                var enable = $"between(t,{S(spot.StartMs)},{S(spot.EndMs)})";
                sb.Append('[').Append(last).Append("]drawbox=x=0:y=0:w=iw:h=").Append(top).Append(":color=black@").Append(alpha).Append(":t=fill:enable='").Append(enable).Append("',")
                  .Append("drawbox=x=0:y=").Append(bottom).Append(":w=iw:h=ih-").Append(bottom).Append(":color=black@").Append(alpha).Append(":t=fill:enable='").Append(enable).Append("',")
                  .Append("drawbox=x=0:y=").Append(top).Append(":w=").Append(left).Append(":h=").Append(Math.Max(1, bottom - top)).Append(":color=black@").Append(alpha).Append(":t=fill:enable='").Append(enable).Append("',")
                  .Append("drawbox=x=").Append(right).Append(":y=").Append(top).Append(":w=iw-").Append(right).Append(":h=").Append(Math.Max(1, bottom - top)).Append(":color=black@").Append(alpha).Append(":t=fill:enable='").Append(enable).Append("'[").Append(next).Append("]; ");
            }
            last = next;
            spotlightIndex++;
        }

        var blurIndex = 0;
        foreach (var blur in project.Blurs.Where(x => x.Enabled))
        {
            var splitA = $"ba{blurIndex}";
            var splitB = $"bb{blurIndex}";
            var blurred = $"bc{blurIndex}";
            var next = $"b{blurIndex}";
            var center = ResolveTrackedCenter(blur.X, blur.Y, blur.TrackCursor, blur.StartMs, blur.EndMs, events);
            var x = $"max(0,min(W-{blur.Width},({center.X})-{blur.Width / 2}))";
            var y = $"max(0,min(H-{blur.Height},({center.Y})-{blur.Height / 2}))";
            sb.Append('[').Append(last).Append("]split=2[").Append(splitA).Append("][").Append(splitB).Append("]; ")
              .Append('[').Append(splitB).Append("]crop=").Append(blur.Width).Append(':').Append(blur.Height).Append(":x='").Append(x).Append("':y='").Append(y)
              .Append("',boxblur=luma_radius=").Append(F(Math.Clamp(blur.Intensity, 1, 50))).Append(":luma_power=2[").Append(blurred).Append("]; ")
              .Append('[').Append(splitA).Append("][").Append(blurred).Append("]overlay=x='").Append(x).Append("':y='").Append(y)
              .Append("':enable='between(t,").Append(S(blur.StartMs)).Append(',').Append(S(blur.EndMs)).Append(")'[").Append(next).Append("]; ");
            last = next;
            blurIndex++;
        }

        if (cursorAss)
        {
            sb.Append('[').Append(last).Append("]subtitles=aromotion-cursor.ass[outv]");
        }
        else
        {
            sb.Append('[').Append(last).Append("]null[outv]");
        }
        return sb.ToString();
    }

    private static (string Scale, string X, string Y) BuildZoomExpressions(List<ZoomSegment> zooms, int width, int height)
    {
        var scale = "1";
        var x = F(width / 2.0);
        var y = F(height / 2.0);
        foreach (var z in zooms.AsEnumerable().Reverse())
        {
            var effectiveScale = z.Scale;
            if (z.SmartFrame && z.FrameWidth is > 8 && z.FrameHeight is > 8)
            {
                var fit = Math.Min(width / (z.FrameWidth.Value * 1.35), height / (z.FrameHeight.Value * 1.35));
                effectiveScale = Math.Clamp(Math.Max(z.Scale, fit), 1.05, 3.5);
            }
            var p = SegmentProgressExpr(z.StartMs, z.EndMs, z.ZoomInMs, z.ZoomOutMs, z.Easing);
            scale = $"if(between(t,{S(z.StartMs)},{S(z.EndMs)}),1+({F(effectiveScale - 1)})*({p}),{scale})";
            x = $"if(between(t,{S(z.StartMs)},{S(z.EndMs)}),{F(z.FocusX)},{x})";
            y = $"if(between(t,{S(z.StartMs)},{S(z.EndMs)}),{F(z.FocusY)},{y})";
        }
        return (scale, x, y);
    }

    private static (string X0, string Y0, string X1, string Y1, string X2, string Y2, string X3, string Y3) BuildPerspectiveExpressions(List<Motion3DSegment> motions, int width, int height)
    {
        var x0 = "0"; var y0 = "0"; var x1 = "W"; var y1 = "0"; var x2 = "0"; var y2 = "H"; var x3 = "W"; var y3 = "H";
        foreach (var m in motions.AsEnumerable().Reverse())
        {
            var p = MotionProgressExpr(m);
            var ampX = F(Math.Clamp(m.RotateY * m.Perspective * m.Intensity / 45.0 * width * .09, -width * .14, width * .14));
            var ampY = F(Math.Clamp(m.RotateX * m.Perspective * m.Intensity / 45.0 * height * .09, -height * .14, height * .14));
            var enable = $"between(t,{S(m.StartMs)},{S(m.EndMs)})";
            x0 = $"if({enable},({ampX})*({p}),{x0})";
            x3 = $"if({enable},W-({ampX})*({p}),{x3})";
            x1 = $"if({enable},W+({ampX})*({p}),{x1})";
            x2 = $"if({enable},-({ampX})*({p}),{x2})";
            y0 = $"if({enable},({ampY})*({p}),{y0})";
            y1 = $"if({enable},-({ampY})*({p}),{y1})";
            y2 = $"if({enable},H-({ampY})*({p}),{y2})";
            y3 = $"if({enable},H+({ampY})*({p}),{y3})";
        }
        return (x0, y0, x1, y1, x2, y2, x3, y3);
    }

    private static string BuildMotionValue(List<Motion3DSegment> motions, Func<Motion3DSegment, double> selector, string fallback)
    {
        var result = fallback;
        foreach (var m in motions.AsEnumerable().Reverse())
        {
            var value = F(selector(m));
            result = $"if(between(t,{S(m.StartMs)},{S(m.EndMs)}),({value})*({MotionProgressExpr(m)}),{result})";
        }
        return result;
    }

    private static string SegmentProgressExpr(long startMs, long endMs, long inMs, long outMs, string easing)
    {
        var start = S(startMs); var end = S(endMs);
        var inEnd = S(startMs + Math.Max(1, inMs)); var outStart = S(endMs - Math.Max(1, outMs));
        var pin = $"(t-{start})/max(0.001,{F(Math.Max(1, inMs) / 1000.0)})";
        var pout = $"({end}-t)/max(0.001,{F(Math.Max(1, outMs) / 1000.0)})";
        return $"if(lt(t,{inEnd}),{EaseExpr(easing, pin)},if(lte(t,{outStart}),1,{EaseExpr(easing, pout)}))";
    }

    private static string MotionProgressExpr(Motion3DSegment m)
    {
        var duration = Math.Max(1, m.EndMs - m.StartMs);
        var p = $"(t-{S(m.StartMs)})/{F(duration / 1000.0)}";
        var speedAdjusted = $"min(1,max(0,({p})*{F(m.Speed)}))";
        return EaseExpr(m.Easing, speedAdjusted);
    }

    private static string EaseExpr(string easing, string p) => easing switch
    {
        "linear" => p,
        "smoothstep" => $"({p})*({p})*(3-2*({p}))",
        "cubic-in" => $"pow(({p}),3)",
        "cubic-out" => $"1-pow(1-({p}),3)",
        "cubic-in-out" => $"if(lt(({p}),0.5),4*pow(({p}),3),1-pow(-2*({p})+2,3)/2)",
        "spring-soft" => $"min(1.08,1-exp(-3.85*({p}))*cos(7*({p})))",
        "spring-snappy" => $"min(1.08,1-exp(-4.41*({p}))*cos(10.5*({p})))",
        _ => $"({p})*({p})*(3-2*({p}))"
    };

    private static (string X, string Y) ResolveTrackedCenter(int x, int y, bool tracked, long startMs, long endMs, List<CaptureEvent> events)
    {
        if (!tracked) return (F(x), F(y));
        var samples = events.Where(e => e.Type == "mouse_move" && e.X.HasValue && e.Y.HasValue && e.TimestampMs >= startMs && e.TimestampMs <= endMs)
            .OrderBy(e => e.TimestampMs).ToList();
        if (samples.Count < 2) return (F(x), F(y));
        // Keep expressions manageable even in long recordings.
        var stride = Math.Max(1, samples.Count / 40);
        samples = samples.Where((_, i) => i % stride == 0).Take(42).ToList();
        var ex = F(x); var ey = F(y);
        for (var i = samples.Count - 2; i >= 0; i--)
        {
            var a = samples[i]; var b = samples[i + 1];
            var dt = Math.Max(1, b.TimestampMs - a.TimestampMs) / 1000.0;
            var p = $"(t-{S(a.TimestampMs)})/{F(dt)}";
            ex = $"if(between(t,{S(a.TimestampMs)},{S(b.TimestampMs)}),{F(a.X!.Value)}+({F(b.X!.Value - a.X.Value)})*({p}),{ex})";
            ey = $"if(between(t,{S(a.TimestampMs)},{S(b.TimestampMs)}),{F(a.Y!.Value)}+({F(b.Y!.Value - a.Y.Value)})*({p}),{ey})";
        }
        return (ex, ey);
    }

    private static async Task<List<CaptureEvent>> LoadEventsAsync(string? path)
    {
        var result = new List<CaptureEvent>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return result;
        await foreach (var line in File.ReadLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { var e = JsonSerializer.Deserialize<CaptureEvent>(line); if (e is not null) result.Add(e); } catch { }
        }
        return result;
    }

    private static async Task<VideoInfo> ProbeAsync(string ffprobe, string path)
    {
        var start = new ProcessStartInfo { FileName = ffprobe, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        Add(start, "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,r_frame_rate", "-of", "csv=p=0:s=x", path);
        using var p = Process.Start(start) ?? throw new InvalidOperationException("ffprobe could not start.");
        var text = (await p.StandardOutput.ReadToEndAsync()).Trim();
        await p.WaitForExitAsync();
        var values = text.Split('x');
        if (values.Length < 3 || !int.TryParse(values[0], out var w) || !int.TryParse(values[1], out var h)) return new VideoInfo(1920, 1080, 60);
        var fpsParts = values[2].Split('/');
        var fps = fpsParts.Length == 2 && double.TryParse(fpsParts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var n) && double.TryParse(fpsParts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d != 0 ? n / d : 60;
        return new VideoInfo(w, h, fps);
    }

    private static string ResolveTool(string bundledName, string fallback)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", bundledName);
        return File.Exists(path) ? path : fallback;
    }
    private static void Add(ProcessStartInfo info, params string[] args) { foreach (var a in args) info.ArgumentList.Add(a); }
    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string S(long ms) => F(ms / 1000.0);
    private sealed record VideoInfo(int Width, int Height, double Fps);
}

internal static class CursorAssWriter
{
    public static async Task WriteAsync(string path, int width, int height, List<CaptureEvent> events, CursorEffectProfile profile)
    {
        var moves = events.Where(x => x.Type == "mouse_move" && x.X.HasValue && x.Y.HasValue).OrderBy(x => x.TimestampMs).ToList();
        var clicks = events.Where(x => x.Type == "mouse_click" && x.X.HasValue && x.Y.HasValue).OrderBy(x => x.TimestampMs).ToList();
        if (profile.HideCursor || moves.Count == 0) { await File.WriteAllTextAsync(path, Header(width, height)); return; }

        var smoothing = Math.Clamp(profile.Smoothing, 0, .95);
        var filtered = new List<CaptureEvent>();
        double sx = moves[0].X!.Value, sy = moves[0].Y!.Value;
        long lastKept = -1000;
        foreach (var move in moves)
        {
            sx = sx * smoothing + move.X!.Value * (1 - smoothing);
            sy = sy * smoothing + move.Y!.Value * (1 - smoothing);
            if (move.TimestampMs - lastKept < 28) continue;
            lastKept = move.TimestampMs;
            filtered.Add(move with { X = (int)Math.Round(sx), Y = (int)Math.Round(sy) });
        }

        var sb = new StringBuilder(Header(width, height));
        var cursorColor = AssColor(profile.Color, profile.Opacity);
        var shadow = profile.Shadow ? "\\shad2" : "\\shad0";
        var blur = profile.MotionBlur > .02 ? $"\\blur{(1 + profile.MotionBlur * 4).ToString("0.0", CultureInfo.InvariantCulture)}" : "";
        var scale = (int)Math.Round(Math.Clamp(profile.Size, .5, 3) * 100);
        var cursorPath = profile.Style switch
        {
            "Minimal Dot" => "m -5 0 b -5 -3 -3 -5 0 -5 b 3 -5 5 -3 5 0 b 5 3 3 5 0 5 b -3 5 -5 3 -5 0",
            "High Contrast" => "m 0 0 l 0 28 l 7 20 l 13 34 l 19 31 l 13 18 l 26 18",
            _ => "m 0 0 l 0 28 l 7 20 l 13 34 l 19 31 l 13 18 l 26 18"
        };

        for (var i = 0; i < filtered.Count - 1; i++)
        {
            var a = filtered[i]; var b = filtered[i + 1];
            var duration = Math.Max(20, b.TimestampMs - a.TimestampMs);
            var tags = $"{{\\an7\\move({a.X},{a.Y},{b.X},{b.Y},0,{duration})\\p1\\fscx{scale}\\fscy{scale}\\bord1{shadow}{blur}\\1c{cursorColor}}}";
            sb.Append("Dialogue: 10,").Append(Time(a.TimestampMs)).Append(',').Append(Time(a.TimestampMs + duration + 15)).Append(",Cursor,,0,0,0,,")
              .Append(tags).Append(cursorPath).Append("{\\p0}\n");
        }

        if (!profile.ClickRingStyle.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var click in clicks)
            {
                var duration = Math.Max(120, profile.ClickAnimationMs);
                var color = AssColor(click.Button == "right" ? profile.RightClickColor : profile.LeftClickColor, .95);
                var circle = "m -10 0 b -10 -6 -6 -10 0 -10 b 6 -10 10 -6 10 0 b 10 6 6 10 0 10 b -6 10 -10 6 -10 0";
                var endScale = profile.ClickRingStyle == "Solid" ? 115 : profile.ClickRingStyle == "Ripple" ? 260 : 200;
                var tags = $"{{\\an5\\pos({click.X},{click.Y})\\p1\\fscx55\\fscy55\\bord2\\shad0\\1c{color}\\alpha&H20&\\t(0,{duration},\\fscx{endScale}\\fscy{endScale}\\alpha&HFF&)}}";
                sb.Append("Dialogue: 20,").Append(Time(click.TimestampMs)).Append(',').Append(Time(click.TimestampMs + duration)).Append(",Click,,0,0,0,,")
                  .Append(tags).Append(circle).Append("{\\p0}\n");
            }
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Header(int width, int height) => $"""[Script Info]
ScriptType: v4.00+
PlayResX: {width}
PlayResY: {height}
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding
Style: Cursor,Arial,24,&H00FFFFFF,&H00FFFFFF,&HCC000000,&H88000000,0,0,0,0,100,100,0,0,1,1,2,7,0,0,0,1
Style: Click,Arial,24,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,5,0,0,0,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
""";

    private static string Time(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
    }

    private static string AssColor(string hex, double opacity)
    {
        try
        {
            var raw = hex.TrimStart('#');
            if (raw.Length != 6) raw = "FFFFFF";
            var r = raw[..2]; var g = raw.Substring(2, 2); var b = raw.Substring(4, 2);
            var alpha = (int)Math.Round((1 - Math.Clamp(opacity, 0, 1)) * 255);
            return $"&H{alpha:X2}{b}{g}{r}&";
        }
        catch { return "&H00FFFFFF&"; }
    }
}
