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

        var customCursor = project.Cursor.Style.Equals("Custom", StringComparison.OrdinalIgnoreCase)
                           && IsSupportedCursorImage(project.Cursor.CustomCursorPath);
        var customClickSound = project.Cursor.ClickSoundEnabled
                               && !string.IsNullOrWhiteSpace(project.Cursor.ClickSoundPath)
                               && File.Exists(project.Cursor.ClickSoundPath);

        var assPath = Path.Combine(projectDirectory, "aromotion-cursor.ass");
        await CursorAssWriter.WriteAsync(assPath, info.Width, info.Height, events, project.Cursor, drawCursor: !customCursor);

        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            WorkingDirectory = projectDirectory
        };

        Add(start, "-hide_banner", "-y", "-i", project.SourceVideo);
        var nextInput = 1;
        int? customCursorInput = null;
        int? clickSoundInput = null;

        if (customCursor)
        {
            customCursorInput = nextInput++;
            Add(start, "-loop", "1", "-framerate", F(Math.Clamp(info.Fps, 15, 120)), "-i", project.Cursor.CustomCursorPath!);
        }
        if (customClickSound)
        {
            clickSoundInput = nextInput++;
            Add(start, "-i", project.Cursor.ClickSoundPath!);
        }

        progress?.Invoke("Building editable zoom / 3D / cursor / privacy graph…");
        var graph = BuildFilterGraph(project, info, events, File.Exists(assPath), customCursorInput, clickSoundInput);
        Add(start, "-filter_complex", graph, "-map", "[outv]");

        var hasClickAudio = project.Cursor.ClickSoundEnabled && events.Any(e => e.Type == "mouse_click");
        if (hasClickAudio)
            Add(start, "-map", "[outa]");
        else if (info.HasAudio)
            Add(start, "-map", "0:a:0?");

        Add(start,
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
                while (tail.Count > 40) tail.Dequeue();
            }
        };
        if (!process.Start()) throw new InvalidOperationException("FFmpeg could not start.");
        process.BeginErrorReadLine();
        progress?.Invoke("Rendering non-destructive motion effects…");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            string details;
            lock (tail) details = string.Join(Environment.NewLine, tail);
            throw new InvalidOperationException("Motion render failed.\n\n" + details);
        }
        progress?.Invoke("Render complete");
    }

    private static string BuildFilterGraph(
        MotionProjectState project,
        VideoInfo info,
        List<CaptureEvent> events,
        bool cursorAss,
        int? customCursorInput,
        int? clickSoundInput)
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
            var rx = BuildMotionProperty(motions, m => m.RotateX, k => k.RotateX, 0);
            var ry = BuildMotionProperty(motions, m => m.RotateY, k => k.RotateY, 0);
            var rz = BuildMotionProperty(motions, m => m.RotateZ, k => k.RotateZ, 0);
            var depth = BuildMotionProperty(motions, m => m.Depth / 100.0, k => k.Depth / 100.0, 0);
            var panX = BuildMotionProperty(motions, m => m.PanX, k => k.PanX, 0);
            var panY = BuildMotionProperty(motions, m => m.PanY, k => k.PanY, 0);
            var perspective = BuildMotionProperty(motions, m => m.Perspective, k => k.Perspective, 1);

            var ampX = $"(({ry})*({perspective})*{F(info.Width * .09 / 45.0)})";
            var ampY = $"(({rx})*({perspective})*{F(info.Height * .09 / 45.0)})";

            sb.Append('[').Append(last).Append("]format=rgba,")
              .Append("perspective=x0='").Append(ampX).Append("':y0='").Append(ampY)
              .Append("':x1='W+").Append(ampX).Append("':y1='-").Append(ampY)
              .Append("':x2='-").Append(ampX).Append("':y2='H-").Append(ampY)
              .Append("':x3='W-").Append(ampX).Append("':y3='H+").Append(ampY)
              .Append("':sense=destination:eval=frame:interpolation=cubic,")
              .Append("rotate=angle='(").Append(rz).Append(")*PI/180':ow=iw:oh=ih:fillcolor=none,")
              .Append("scale=w='trunc(iw*(1+(").Append(depth).Append("))/2)*2':h='trunc(ih*(1+(").Append(depth).Append("))/2)*2':eval=frame,")
              .Append("crop=").Append(info.Width).Append(':').Append(info.Height)
              .Append(":x='max(0,min(iw-").Append(info.Width).Append(",(iw-").Append(info.Width).Append(")/2-(").Append(panX).Append(")))'")
              .Append(":y='max(0,min(ih-").Append(info.Height).Append(",(ih-").Append(info.Height).Append(")/2-(").Append(panY).Append(")))'[mraw];");
            last = "mraw";

            var shadowMotions = motions.Where(m => m.Shadow).ToList();
            if (shadowMotions.Count > 0)
            {
                var enable = EnableExpr(shadowMotions);
                var shadowOpacity = F(shadowMotions.Max(m => Math.Clamp(m.ShadowOpacity, 0, .85)));
                var blur = F(shadowMotions.Max(m => Math.Clamp(m.ShadowBlur, 1, 40)));
                sb.Append('[').Append(last).Append("]split=2[shadowbase][shadowsrc];")
                  .Append("[shadowsrc]colorchannelmixer=rr=0:gg=0:bb=0:aa=").Append(shadowOpacity)
                  .Append(",boxblur=luma_radius=").Append(blur).Append(":luma_power=1:alpha_radius=").Append(blur).Append(":alpha_power=1[shadow];")
                  .Append("color=c=#080A10:s=").Append(info.Width).Append('x').Append(info.Height).Append(":r=").Append(F(info.Fps)).Append("[shadowbg];")
                  .Append("[shadowbg][shadow]overlay=x=10:y=12:shortest=1:enable='").Append(enable).Append("'[withshadow];")
                  .Append("[withshadow][shadowbase]overlay=x=0:y=0:shortest=1[mshadow];");
                last = "mshadow";
            }

            var reflectionMotions = motions.Where(m => m.Reflection).ToList();
            if (reflectionMotions.Count > 0)
            {
                var enable = EnableExpr(reflectionMotions);
                var alpha = F(reflectionMotions.Max(m => Math.Clamp(m.ReflectionOpacity, 0, .55)));
                var strip = Math.Max(30, (int)(info.Height * .20));
                sb.Append('[').Append(last).Append("]split=2[refbase][refsrc];")
                  .Append("[refsrc]vflip,crop=iw:").Append(strip).Append(":0:0,format=rgba,colorchannelmixer=aa=").Append(alpha).Append("[reflection];")
                  .Append("[refbase][reflection]overlay=x=0:y=H-h:shortest=1:enable='").Append(enable).Append("'[mref];");
                last = "mref";
            }
        }

        var spotlightIndex = 0;
        foreach (var spot in project.Spotlights.Where(x => x.Enabled))
        {
            var next = "s" + spotlightIndex;
            if (spot.Shape.Equals("Circle", StringComparison.OrdinalIgnoreCase))
            {
                var center = ResolveTrackedCenter(spot.X, spot.Y, spot.FollowCursor, spot.StartMs, spot.EndMs, events);
                var featherFactor = Math.Clamp(spot.Feather / Math.Max(20.0, Math.Min(spot.Width, spot.Height)), 0, .85);
                var angle = F(Math.Clamp(.30 + spot.Darkness * 1.05 - featherFactor * .18, .18, 1.42));
                sb.Append('[').Append(last).Append("]vignette=angle=").Append(angle)
                  .Append(":x0='").Append(center.X).Append("':y0='").Append(center.Y)
                  .Append(":eval=frame:enable='between(t,").Append(S(spot.StartMs)).Append(',').Append(S(spot.EndMs)).Append(")'[").Append(next).Append("]; ");
            }
            else
            {
                var enable = $"between(t,{S(spot.StartMs)},{S(spot.EndMs)})";
                var layers = Math.Clamp((int)Math.Ceiling(spot.Feather / 8.0) + 1, 1, 6);
                var layerInput = last;
                for (var layer = 0; layer < layers; layer++)
                {
                    var fraction = (layer + 1) / (double)layers;
                    var expansion = spot.Feather * (1 - fraction);
                    var alpha = 1 - Math.Pow(1 - Math.Clamp(spot.Darkness, 0, .95), 1.0 / layers);
                    var left = Math.Max(0, spot.X - spot.Width / 2.0 - expansion);
                    var top = Math.Max(0, spot.Y - spot.Height / 2.0 - expansion);
                    var right = Math.Min(info.Width, spot.X + spot.Width / 2.0 + expansion);
                    var bottom = Math.Min(info.Height, spot.Y + spot.Height / 2.0 + expansion);
                    var layerOut = layer == layers - 1 ? next : $"sf{spotlightIndex}_{layer}";
                    AppendRectangleSpotlightLayer(sb, layerInput, layerOut, left, top, right, bottom, alpha, enable);
                    layerInput = layerOut;
                }
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
            var feather = (int)Math.Clamp(Math.Round(blur.Feather), 0, 40);
            var cropW = Math.Min(info.Width, blur.Width + feather * 2);
            var cropH = Math.Min(info.Height, blur.Height + feather * 2);
            var x = $"max(0,min(iw-{cropW},({center.X})-{cropW / 2}))";
            var y = $"max(0,min(ih-{cropH},({center.Y})-{cropH / 2}))";
            var overlayX = $"max(0,min(main_w-overlay_w,({center.X})-{cropW / 2}))";
            var overlayY = $"max(0,min(main_h-overlay_h,({center.Y})-{cropH / 2}))";

            sb.Append('[').Append(last).Append("]split=2[").Append(splitA).Append("][").Append(splitB).Append("]; ")
              .Append('[').Append(splitB).Append("]crop=").Append(cropW).Append(':').Append(cropH).Append(":x='").Append(x).Append("':y='").Append(y).Append("',")
              .Append("boxblur=luma_radius=").Append(F(Math.Clamp(blur.Intensity, 1, 50))).Append(":luma_power=2");

            if (feather > 0)
            {
                var f = Math.Max(1, feather);
                sb.Append(",format=yuva444p,geq=lum='lum(X,Y)':cb='cb(X,Y)':cr='cr(X,Y)':a='255*min(1,min(X/")
                  .Append(f).Append(",(W-1-X)/").Append(f).Append(",Y/").Append(f).Append(",(H-1-Y)/").Append(f).Append("))' ");
            }
            sb.Append('[').Append(blurred).Append("]; ")
              .Append('[').Append(splitA).Append("][").Append(blurred).Append("]overlay=x='").Append(overlayX).Append("':y='").Append(overlayY)
              .Append("':enable='between(t,").Append(S(blur.StartMs)).Append(',').Append(S(blur.EndMs)).Append(")'[").Append(next).Append("]; ");
            last = next;
            blurIndex++;
        }

        if (customCursorInput is not null && !project.Cursor.HideCursor)
        {
            var size = Math.Max(12, (int)Math.Round(36 * Math.Clamp(project.Cursor.Size, .5, 3)));
            var pos = BuildCursorPositionExpressions(events, project.Cursor.Smoothing, info.Width, info.Height);
            sb.Append('[').Append(customCursorInput.Value).Append(":v]format=rgba,scale=").Append(size).Append(':').Append(size)
              .Append(",colorchannelmixer=aa=").Append(F(project.Cursor.Opacity)).Append("[customcursor];")
              .Append('[').Append(last).Append("][customcursor]overlay=x='").Append(pos.X).Append("':y='").Append(pos.Y)
              .Append("':shortest=1:eval=frame[withcustomcursor];");
            last = "withcustomcursor";
        }

        if (cursorAss)
        {
            sb.Append('[').Append(last).Append("]subtitles=aromotion-cursor.ass[vfinal];");
            last = "vfinal";
        }
        sb.Append('[').Append(last).Append("]format=yuv444p[outv];");

        AppendClickAudioGraph(sb, project.Cursor, events, clickSoundInput, info.HasAudio);
        return sb.ToString().TrimEnd(';');
    }

    private static void AppendRectangleSpotlightLayer(StringBuilder sb, string input, string output, double left, double top, double right, double bottom, double alpha, string enable)
    {
        var a = F(Math.Clamp(alpha, 0, .95));
        sb.Append('[').Append(input).Append("]drawbox=x=0:y=0:w=iw:h=").Append(F(top)).Append(":color=black@").Append(a).Append(":t=fill:enable='").Append(enable).Append("',")
          .Append("drawbox=x=0:y=").Append(F(bottom)).Append(":w=iw:h=ih-").Append(F(bottom)).Append(":color=black@").Append(a).Append(":t=fill:enable='").Append(enable).Append("',")
          .Append("drawbox=x=0:y=").Append(F(top)).Append(":w=").Append(F(left)).Append(":h=").Append(F(Math.Max(1, bottom - top))).Append(":color=black@").Append(a).Append(":t=fill:enable='").Append(enable).Append("',")
          .Append("drawbox=x=").Append(F(right)).Append(":y=").Append(F(top)).Append(":w=iw-").Append(F(right)).Append(":h=").Append(F(Math.Max(1, bottom - top))).Append(":color=black@").Append(a).Append(":t=fill:enable='").Append(enable).Append("'[").Append(output).Append("]; ");
    }

    private static void AppendClickAudioGraph(StringBuilder sb, CursorEffectProfile profile, List<CaptureEvent> events, int? clickSoundInput, bool hasSourceAudio)
    {
        if (!profile.ClickSoundEnabled) return;
        var clicks = events.Where(e => e.Type == "mouse_click").OrderBy(e => e.TimestampMs).Take(160).ToList();
        if (clicks.Count == 0) return;

        if (clickSoundInput is not null)
        {
            sb.Append('[').Append(clickSoundInput.Value).Append(":a]aresample=48000,atrim=0:0.14,asetpts=PTS-STARTPTS,volume=").Append(F(profile.ClickSoundVolume)).Append("[clickbase];");
        }
        else
        {
            sb.Append("sine=frequency=980:sample_rate=48000:duration=0.045,afade=t=out:st=0.006:d=0.039,volume=").Append(F(profile.ClickSoundVolume * .45)).Append("[clickbase];");
        }

        if (clicks.Count == 1)
        {
            sb.Append("[clickbase]adelay=").Append(clicks[0].TimestampMs).Append('|').Append(clicks[0].TimestampMs).Append("[clickmix];");
        }
        else
        {
            sb.Append("[clickbase]asplit=").Append(clicks.Count);
            for (var i = 0; i < clicks.Count; i++) sb.Append("[cs").Append(i).Append(']');
            sb.Append(';');
            for (var i = 0; i < clicks.Count; i++)
            {
                var delay = clicks[i].TimestampMs;
                sb.Append("[cs").Append(i).Append("]adelay=").Append(delay).Append('|').Append(delay).Append("[cd").Append(i).Append("]; ");
            }
            for (var i = 0; i < clicks.Count; i++) sb.Append("[cd").Append(i).Append(']');
            sb.Append("amix=inputs=").Append(clicks.Count).Append(":duration=longest:normalize=0[clickmix];");
        }

        if (hasSourceAudio)
            sb.Append("[0:a:0]aresample=48000[basea];[basea][clickmix]amix=inputs=2:duration=first:normalize=0[outa];");
        else
            sb.Append("[clickmix]anull[outa];");
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

    private static string BuildMotionProperty(
        List<Motion3DSegment> motions,
        Func<Motion3DSegment, double> segmentSelector,
        Func<Motion3DKeyframe, double> keySelector,
        double neutral)
    {
        var result = F(neutral);
        foreach (var m in motions.AsEnumerable().Reverse())
        {
            string value;
            if (m.Keyframes.Count >= 2)
                value = BuildKeyframedValue(m, keySelector);
            else
                value = $"{F(neutral)}+({F(segmentSelector(m) - neutral)})*({MotionProgressExpr(m)})";

            value = $"{F(neutral)}+(({value})-{F(neutral)})*{F(Math.Clamp(m.Intensity, 0, 2))}";
            result = $"if(between(t,{S(m.StartMs)},{S(m.EndMs)}),{value},{result})";
        }
        return result;
    }

    private static string BuildKeyframedValue(Motion3DSegment motion, Func<Motion3DKeyframe, double> selector)
    {
        var keys = motion.Keyframes.OrderBy(k => k.TimeMs).ToList();
        if (keys.Count == 0) return "0";
        if (keys.Count == 1) return F(selector(keys[0]));
        var result = F(selector(keys[^1]));
        for (var i = keys.Count - 2; i >= 0; i--)
        {
            var a = keys[i]; var b = keys[i + 1];
            var duration = Math.Max(1, b.TimeMs - a.TimeMs) / 1000.0;
            var p = $"min(1,max(0,((t-{S(a.TimeMs)})/{F(duration)})*{F(Math.Clamp(motion.Speed, .25, 3))}))";
            var eased = EaseExpr(b.Easing, p);
            var lerp = $"{F(selector(a))}+({F(selector(b) - selector(a))})*({eased})";
            result = $"if(lte(t,{S(b.TimeMs)}),{lerp},{result})";
        }
        return result;
    }

    private static string SegmentProgressExpr(long startMs, long endMs, long inMs, long outMs, string easing)
    {
        var start = S(startMs); var end = S(endMs);
        var inEnd = S(startMs + Math.Max(1, inMs)); var outStart = S(endMs - Math.Max(1, outMs));
        var pin = $"min(1,max(0,(t-{start})/max(0.001,{F(Math.Max(1, inMs) / 1000.0)})))";
        var pout = $"min(1,max(0,({end}-t)/max(0.001,{F(Math.Max(1, outMs) / 1000.0)})))";
        return $"if(lt(t,{inEnd}),{EaseExpr(easing, pin)},if(lte(t,{outStart}),1,{EaseExpr(easing, pout)}))";
    }

    private static string MotionProgressExpr(Motion3DSegment m)
    {
        var duration = Math.Max(1, m.EndMs - m.StartMs);
        var p = $"min(1,max(0,((t-{S(m.StartMs)})/{F(duration / 1000.0)})*{F(Math.Clamp(m.Speed, .25, 3))}))";
        return EaseExpr(m.Easing, p);
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
        var stride = Math.Max(1, samples.Count / 40);
        samples = samples.Where((_, i) => i % stride == 0).Take(42).ToList();
        return BuildPositionExpressions(samples, x, y);
    }

    private static (string X, string Y) BuildCursorPositionExpressions(List<CaptureEvent> events, double smoothing, int width, int height)
    {
        var moves = events.Where(e => e.Type == "mouse_move" && e.X.HasValue && e.Y.HasValue).OrderBy(e => e.TimestampMs).ToList();
        if (moves.Count < 2) return (F(width / 2.0), F(height / 2.0));

        var smooth = Math.Clamp(smoothing, 0, .95);
        var filtered = new List<CaptureEvent>();
        double sx = moves[0].X!.Value, sy = moves[0].Y!.Value;
        foreach (var m in moves)
        {
            sx = sx * smooth + m.X!.Value * (1 - smooth);
            sy = sy * smooth + m.Y!.Value * (1 - smooth);
            filtered.Add(m with { X = (int)Math.Round(sx), Y = (int)Math.Round(sy) });
        }
        var stride = Math.Max(1, filtered.Count / 100);
        var sampled = filtered.Where((_, i) => i % stride == 0).Take(105).ToList();
        return BuildPositionExpressions(sampled, width / 2, height / 2);
    }

    private static (string X, string Y) BuildPositionExpressions(List<CaptureEvent> samples, int fallbackX, int fallbackY)
    {
        var ex = F(fallbackX); var ey = F(fallbackY);
        for (var i = samples.Count - 2; i >= 0; i--)
        {
            var a = samples[i]; var b = samples[i + 1];
            var dt = Math.Max(1, b.TimestampMs - a.TimestampMs) / 1000.0;
            var p = $"min(1,max(0,(t-{S(a.TimestampMs)})/{F(dt)}))";
            ex = $"if(between(t,{S(a.TimestampMs)},{S(b.TimestampMs)}),{F(a.X!.Value)}+({F(b.X!.Value - a.X.Value)})*({p}),{ex})";
            ey = $"if(between(t,{S(a.TimestampMs)},{S(b.TimestampMs)}),{F(a.Y!.Value)}+({F(b.Y!.Value - a.Y.Value)})*({p}),{ey})";
        }
        return (ex, ey);
    }

    private static string EnableExpr(IEnumerable<Motion3DSegment> motions)
    {
        var parts = motions.Select(m => $"between(t,{S(m.StartMs)},{S(m.EndMs)})").ToList();
        if (parts.Count == 0) return "0";
        return parts.Count == 1 ? parts[0] : string.Join('+', parts.Select(p => $"({p})"));
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
        Add(start, "-v", "error", "-show_entries", "stream=codec_type,width,height,r_frame_rate", "-of", "json", path);
        using var p = Process.Start(start) ?? throw new InvalidOperationException("ffprobe could not start.");
        var text = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        try
        {
            using var doc = JsonDocument.Parse(text);
            var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();
            var video = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "video");
            var w = video.TryGetProperty("width", out var wp) ? wp.GetInt32() : 1920;
            var h = video.TryGetProperty("height", out var hp) ? hp.GetInt32() : 1080;
            var rate = video.TryGetProperty("r_frame_rate", out var rp) ? rp.GetString() ?? "60/1" : "60/1";
            var parts = rate.Split('/');
            var fps = parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var n) && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d != 0 ? n / d : 60;
            var hasAudio = streams.Any(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "audio");
            return new VideoInfo(w, h, fps, hasAudio);
        }
        catch { return new VideoInfo(1920, 1080, 60, false); }
    }

    private static bool IsSupportedCursorImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".bmp" or ".jpg" or ".jpeg" or ".webp" or ".ico";
    }

    private static string ResolveTool(string bundledName, string fallback)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", bundledName);
        return File.Exists(path) ? path : fallback;
    }
    private static void Add(ProcessStartInfo info, params string[] args) { foreach (var a in args) info.ArgumentList.Add(a); }
    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string S(long ms) => F(ms / 1000.0);
    private sealed record VideoInfo(int Width, int Height, double Fps, bool HasAudio);
}

internal static class CursorAssWriter
{
    public static async Task WriteAsync(string path, int width, int height, List<CaptureEvent> events, CursorEffectProfile profile, bool drawCursor)
    {
        var moves = events.Where(x => x.Type == "mouse_move" && x.X.HasValue && x.Y.HasValue).OrderBy(x => x.TimestampMs).ToList();
        var clicks = events.Where(x => x.Type == "mouse_click" && x.X.HasValue && x.Y.HasValue).OrderBy(x => x.TimestampMs).ToList();
        var sb = new StringBuilder(Header(width, height));

        if (drawCursor && !profile.HideCursor && moves.Count > 0)
        {
            var smoothing = Math.Clamp(profile.Smoothing, 0, .95);
            var filtered = new List<CaptureEvent>();
            double sx = moves[0].X!.Value, sy = moves[0].Y!.Value;
            long lastKept = -1000;
            foreach (var move in moves)
            {
                sx = sx * smoothing + move.X!.Value * (1 - smoothing);
                sy = sy * smoothing + move.Y!.Value * (1 - smoothing);
                if (move.TimestampMs - lastKept < 25) continue;
                lastKept = move.TimestampMs;
                filtered.Add(move with { X = (int)Math.Round(sx), Y = (int)Math.Round(sy) });
            }

            var cursorColor = AssColor(profile.Style == "AROMOTION Dark" ? "#151A22" : profile.Color, profile.Opacity);
            var shadow = profile.Shadow ? $"\\shad{Math.Max(1, (int)Math.Round(2 + profile.ShadowOpacity * 3))}" : "\\shad0";
            var scale = (int)Math.Round(Math.Clamp(profile.Size, .5, 3) * 100);
            var cursorPath = profile.Style switch
            {
                "Minimal Dot" => "m -5 0 b -5 -3 -3 -5 0 -5 b 3 -5 5 -3 5 0 b 5 3 3 5 0 5 b -3 5 -5 3 -5 0",
                "High Contrast" => "m 0 0 l 0 30 l 8 21 l 14 35 l 21 31 l 14 18 l 28 18",
                _ => "m 0 0 l 0 28 l 7 20 l 13 34 l 19 31 l 13 18 l 26 18"
            };

            var trailLayers = profile.MotionBlur <= .04 ? 0 : Math.Clamp((int)Math.Round(profile.MotionBlur * 4), 1, 4);
            for (var i = 0; i < filtered.Count - 1; i++)
            {
                var a = filtered[i]; var b = filtered[i + 1];
                var duration = Math.Max(20, b.TimestampMs - a.TimestampMs);
                for (var trail = trailLayers; trail >= 1; trail--)
                {
                    var previousIndex = Math.Max(0, i - trail);
                    var pa = filtered[previousIndex];
                    var pb = filtered[Math.Min(filtered.Count - 1, previousIndex + 1)];
                    var alpha = 175 + trail * 16;
                    var trailTags = $"{{\\an7\\move({pa.X},{pa.Y},{pb.X},{pb.Y},0,{duration})\\p1\\fscx{scale}\\fscy{scale}\\bord0\\shad0\\1c{cursorColor}\\alpha&H{Math.Clamp(alpha, 0, 245):X2}&}}";
                    sb.Append("Dialogue: 4,").Append(Time(a.TimestampMs)).Append(',').Append(Time(a.TimestampMs + duration + 10)).Append(",Cursor,,0,0,0,,").Append(trailTags).Append(cursorPath).Append("{\\p0}\n");
                }
                var tags = $"{{\\an7\\move({a.X},{a.Y},{b.X},{b.Y},0,{duration})\\p1\\fscx{scale}\\fscy{scale}\\bord1{shadow}\\1c{cursorColor}}}";
                sb.Append("Dialogue: 10,").Append(Time(a.TimestampMs)).Append(',').Append(Time(a.TimestampMs + duration + 15)).Append(",Cursor,,0,0,0,,").Append(tags).Append(cursorPath).Append("{\\p0}\n");
            }
        }

        if (!profile.ClickRingStyle.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var click in clicks)
            {
                var duration = Math.Max(120, profile.ClickAnimationMs);
                var color = AssColor(click.Button == "right" ? profile.RightClickColor : profile.LeftClickColor, .95);
                var circle = "m -10 0 b -10 -6 -6 -10 0 -10 b 6 -10 10 -6 10 0 b 10 6 6 10 0 10 b -6 10 -10 6 -10 0";
                var endScale = profile.ClickRingStyle == "Solid" ? 115 : profile.ClickRingStyle == "Ripple" ? 270 : 205;
                var tags = $"{{\\an5\\pos({click.X},{click.Y})\\p1\\fscx55\\fscy55\\bord2\\shad0\\1c{color}\\alpha&H20&\\t(0,{duration},\\fscx{endScale}\\fscy{endScale}\\alpha&HFF&)}}";
                sb.Append("Dialogue: 20,").Append(Time(click.TimestampMs)).Append(',').Append(Time(click.TimestampMs + duration)).Append(",Click,,0,0,0,,").Append(tags).Append(circle).Append("{\\p0}\n");
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
