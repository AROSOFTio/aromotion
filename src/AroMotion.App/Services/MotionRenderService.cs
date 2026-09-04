using System.Diagnostics;
using System.Globalization;
using System.Text;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class MotionRenderService
{
    private readonly CursorMotionEngine _cursorEngine = new();
    private readonly AssCursorOverlayGenerator _assGenerator = new();

    public event Action<string>? LogReceived;

    public async Task RenderPreviewAsync(
        string masterVideoPath,
        string eventsPath,
        MotionProject project,
        string outputPath,
        int framesPerSecond,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(masterVideoPath))
            throw new FileNotFoundException("The lossless master video was not found.", masterVideoPath);

        var ffmpeg = ResolveTool("ffmpeg.exe", "ffmpeg");
        var ffprobe = ResolveTool("ffprobe.exe", "ffprobe");
        var (width, height) = await ProbeSizeAsync(ffprobe, masterVideoPath, cancellationToken);

        var cursorSamples = await _cursorEngine.ReconstructAsync(eventsPath, project.Cursor, framesPerSecond, cancellationToken);
        var clickFrames = await _cursorEngine.BuildClickFramesAsync(eventsPath, project.Cursor, framesPerSecond, cancellationToken);
        var overlayPath = Path.Combine(Path.GetDirectoryName(outputPath)!, "cursor-overlay.ass");
        await _assGenerator.GenerateAsync(overlayPath, cursorSamples, clickFrames, project.Cursor, width, height, cancellationToken);

        var graph = BuildFilterGraph(project, cursorSamples, overlayPath, width, height, framesPerSecond);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        Add(start,
            "-hide_banner", "-y",
            "-i", masterVideoPath,
            "-filter_complex", graph,
            "-map", "[vout]",
            "-map", "0:a?",
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "12",
            "-pix_fmt", "yuv444p",
            "-c:a", "copy",
            outputPath);

        await RunAsync(start, cancellationToken);
    }

    public string BuildFilterGraph(
        MotionProject project,
        IReadOnlyList<CursorSample> cursorSamples,
        string assOverlayPath,
        int width,
        int height,
        int fps)
    {
        var parts = new List<string>();
        var current = "[0:v]";
        var index = 0;

        if (project.Zooms.Any(z => z.Enabled))
        {
            var z = BuildZoomScaleExpression(project.Zooms.Where(x => x.Enabled).ToList());
            var fx = BuildZoomFocusExpression(project.Zooms.Where(x => x.Enabled).ToList(), width, true);
            var fy = BuildZoomFocusExpression(project.Zooms.Where(x => x.Enabled).ToList(), height, false);
            var next = $"[z{index++}]";
            parts.Add($"{current}scale=w='trunc({width}*({z})/2)*2':h='trunc({height}*({z})/2)*2':eval=frame," +
                      $"crop={width}:{height}:x='max(0,min(iw-{width},({fx})*iw/{width}-{width / 2}))':" +
                      $"y='max(0,min(ih-{height},({fy})*ih/{height}-{height / 2}))'{next}");
            current = next;
        }

        if (project.Motions3D.Any(m => m.Enabled))
        {
            var next = $"[m{index++}]";
            var p = BuildPerspectiveFilter(project.Motions3D.Where(x => x.Enabled).ToList(), width, height, fps);
            parts.Add($"{current}{p}{next}");
            current = next;
        }

        foreach (var blur in project.Blurs.Where(b => b.Enabled))
        {
            var splitA = $"[bbase{index}]";
            var splitB = $"[bsrc{index}]";
            var blurred = $"[blur{index}]";
            var next = $"[b{index++}]";
            var start = Sec(blur.StartMs);
            var end = Sec(blur.EndMs);
            var xExpr = blur.TrackCursor
                ? BuildCursorExpression(cursorSamples, blur.StartMs, blur.EndMs, true, blur.X) + $"-{F(blur.Width / 2)}"
                : F(blur.X);
            var yExpr = blur.TrackCursor
                ? BuildCursorExpression(cursorSamples, blur.StartMs, blur.EndMs, false, blur.Y) + $"-{F(blur.Height / 2)}"
                : F(blur.Y);
            parts.Add($"{current}split=2{splitA}{splitB}");
            parts.Add($"{splitB}crop=w={F(blur.Width)}:h={F(blur.Height)}:x='{xExpr}':y='{yExpr}',gblur=sigma={F(blur.Intensity)}:steps=2{blurred}");
            parts.Add($"{splitA}{blurred}overlay=x='{xExpr}':y='{yExpr}':enable='between(t,{start},{end})'{next}");
            current = next;
        }

        foreach (var spotlight in project.Spotlights.Where(s => s.Enabled))
        {
            var next = $"[s{index++}]";
            var start = Sec(spotlight.StartMs);
            var end = Sec(spotlight.EndMs);
            var cx = spotlight.FollowCursor
                ? BuildCursorExpression(cursorSamples, spotlight.StartMs, spotlight.EndMs, true, spotlight.X)
                : F(spotlight.X);
            var cy = spotlight.FollowCursor
                ? BuildCursorExpression(cursorSamples, spotlight.StartMs, spotlight.EndMs, false, spotlight.Y)
                : F(spotlight.Y);
            var w = Math.Max(8, spotlight.Width);
            var h = Math.Max(8, spotlight.Height);
            var alpha = Math.Clamp(spotlight.Darkness, 0, 1);

            if (spotlight.Shape == SpotlightShape.Circle)
            {
                // A vignette-like inverse ellipse mask generated as an RGBA source.
                var mask = $"[smask{index}]";
                var blurredMask = $"[smaskb{index}]";
                var sigma = Math.Max(0.1, spotlight.Feather * 40.0);
                var inside = $"lte(pow((X-({cx}))/({F(w / 2)}),2)+pow((Y-({cy}))/({F(h / 2)}),2),1)";
                parts.Add($"color=c=black@{F(alpha)}:s={width}x{height}:r={fps},format=rgba,geq=a='if({inside},0,{F(alpha * 255)})',gblur=sigma={F(sigma)}:steps=2{blurredMask}");
                parts.Add($"{current}{blurredMask}overlay=0:0:enable='between(t,{start},{end})'{next}");
            }
            else
            {
                // Four translucent panels preserve a clean rectangular/rounded spotlight hole.
                var left = $"({cx})-{F(w / 2)}";
                var right = $"({cx})+{F(w / 2)}";
                var top = $"({cy})-{F(h / 2)}";
                var bottom = $"({cy})+{F(h / 2)}";
                var color = $"black@{F(alpha)}";
                parts.Add($"{current}drawbox=x=0:y=0:w=iw:h='max(0,{top})':color={color}:t=fill:enable='between(t,{start},{end})'," +
                          $"drawbox=x=0:y='{bottom}':w=iw:h='max(0,ih-({bottom}))':color={color}:t=fill:enable='between(t,{start},{end})'," +
                          $"drawbox=x=0:y='{top}':w='max(0,{left})':h={F(h)}:color={color}:t=fill:enable='between(t,{start},{end})'," +
                          $"drawbox=x='{right}':y='{top}':w='max(0,iw-({right}))':h={F(h)}:color={color}:t=fill:enable='between(t,{start},{end})'{next}");
            }
            current = next;
        }

        var escapedAss = EscapeFilterPath(assOverlayPath);
        parts.Add($"{current}subtitles=filename='{escapedAss}'[vout]");
        return string.Join(';', parts);
    }

    private static string BuildZoomScaleExpression(IReadOnlyList<ZoomClip> clips)
    {
        var expr = "1";
        foreach (var clip in clips.OrderByDescending(x => x.StartMs))
        {
            var s = clip.StartMs / 1000.0;
            var a = (clip.StartMs + clip.ZoomInMs) / 1000.0;
            var b = (clip.StartMs + clip.ZoomInMs + clip.HoldMs) / 1000.0;
            var e = clip.EndMs / 1000.0;
            var scale = F(clip.Scale);
            var inCurve = EaseExpression($"(t-{F(s)})/{F(Math.Max(0.001, a - s))}", clip.Easing);
            var outCurve = EaseExpression($"(t-{F(b)})/{F(Math.Max(0.001, e - b))}", clip.Easing);
            var phase = $"if(between(t,{F(s)},{F(a)}),1+({scale}-1)*({inCurve}),if(between(t,{F(a)},{F(b)}),{scale},{scale}-({scale}-1)*({outCurve})))";
            expr = $"if(between(t,{F(s)},{F(e)}),{phase},{expr})";
        }
        return expr;
    }

    private static string BuildZoomFocusExpression(IReadOnlyList<ZoomClip> clips, int fallbackDimension, bool x)
    {
        var expr = F(fallbackDimension / 2.0);
        foreach (var clip in clips.OrderByDescending(c => c.StartMs))
        {
            var value = x ? clip.FocusX : clip.FocusY;
            expr = $"if(between(t,{Sec(clip.StartMs)},{Sec(clip.EndMs)}),{F(value)},{expr})";
        }
        return expr;
    }

    private static string BuildPerspectiveFilter(IReadOnlyList<Motion3DClip> clips, int width, int height, int fps)
    {
        string x0 = "0", y0 = "0", x1 = "W", y1 = "0", x2 = "0", y2 = "H", x3 = "W", y3 = "H";
        foreach (var clip in clips.OrderByDescending(c => c.StartMs))
        {
            var startFrame = Math.Max(0, (long)Math.Round(clip.StartMs / 1000.0 * fps));
            var motionFrames = Math.Max(1, (long)Math.Round(clip.DurationMs / 1000.0 * fps));
            var endFrame = Math.Max(startFrame + 1, (long)Math.Round(clip.EndMs / 1000.0 * fps));
            var pRaw = $"min(1,max(0,(on-{startFrame})/{motionFrames}.0))";
            var p = EaseExpression(pRaw, clip.Easing);
            var active = $"between(on,{startFrame},{endFrame})";

            // Map the editable 3D parameters onto perspective corner offsets.
            var yaw = clip.RotateY / 90.0 * width * 0.18;
            var pitch = clip.RotateX / 90.0 * height * 0.18;
            var roll = clip.RotateZ / 90.0 * Math.Min(width, height) * 0.10;
            var depth = clip.DepthZ / Math.Max(200.0, clip.Perspective) * Math.Min(width, height) * 0.22;
            var panX = clip.PanX;
            var panY = clip.PanY;
            var orbit = clip.Orbit / 90.0 * width * 0.12;
            var parallax = clip.Parallax * width * 0.04;

            var leftDx = F((yaw + depth + panX + orbit + parallax) * clip.Intensity);
            var rightDx = F((-yaw - depth + panX + orbit - parallax) * clip.Intensity);
            var topDy = F((pitch + depth + panY + roll) * clip.Intensity);
            var bottomDy = F((-pitch - depth + panY - roll) * clip.Intensity);

            x0 = $"if({active},({leftDx})*({p}),{x0})";
            y0 = $"if({active},({topDy})*({p}),{y0})";
            x1 = $"if({active},W+({rightDx})*({p}),{x1})";
            y1 = $"if({active},({topDy})*({p}),{y1})";
            x2 = $"if({active},({leftDx})*({p}),{x2})";
            y2 = $"if({active},H+({bottomDy})*({p}),{y2})";
            x3 = $"if({active},W+({rightDx})*({p}),{x3})";
            y3 = $"if({active},H+({bottomDy})*({p}),{y3})";
        }

        return $"perspective=x0='{x0}':y0='{y0}':x1='{x1}':y1='{y1}':x2='{x2}':y2='{y2}':x3='{x3}':y3='{y3}':sense=destination:eval=frame:interpolation=cubic";
    }

    private static string BuildCursorExpression(IReadOnlyList<CursorSample> samples, long startMs, long endMs, bool x, double fallback)
    {
        var selected = samples
            .Where(s => s.TimestampMs >= startMs && s.TimestampMs <= endMs)
            .Where((_, index) => index % 12 == 0)
            .ToList();
        if (selected.Count == 0) return F(fallback);
        if (selected.Count == 1) return F(x ? selected[0].X : selected[0].Y);

        var expr = F(x ? selected[^1].X : selected[^1].Y);
        for (var i = selected.Count - 2; i >= 0; i--)
        {
            var a = selected[i];
            var b = selected[i + 1];
            var av = x ? a.X : a.Y;
            var bv = x ? b.X : b.Y;
            var ta = a.TimestampMs / 1000.0;
            var tb = b.TimestampMs / 1000.0;
            var denom = Math.Max(0.001, tb - ta);
            var linear = $"{F(av)}+({F(bv - av)})*(t-{F(ta)})/{F(denom)}";
            expr = $"if(between(t,{F(ta)},{F(tb)}),{linear},{expr})";
        }
        return expr;
    }

    private static string EaseExpression(string t, EasingKind easing) => easing switch
    {
        EasingKind.Linear => $"({t})",
        EasingKind.EaseIn => $"pow(({t}),2)",
        EasingKind.EaseOut => $"1-pow(1-({t}),2)",
        EasingKind.EaseInOut => $"if(lt(({t}),0.5),2*pow(({t}),2),1-pow(-2*({t})+2,2)/2)",
        EasingKind.Cubic => $"if(lt(({t}),0.5),4*pow(({t}),3),1-pow(-2*({t})+2,3)/2)",
        EasingKind.SmoothStep => $"pow(({t}),2)*(3-2*({t}))",
        EasingKind.SpringSoft => $"min(1.08,max(0,1-exp(-5.85*({t}))*cos(7.5*({t}))))",
        EasingKind.SpringSnappy => $"min(1.08,max(0,1-exp(-6.82*({t}))*cos(11*({t}))))",
        _ => $"({t})"
    };

    private static async Task<(int Width, int Height)> ProbeSizeAsync(string ffprobe, string path, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        Add(psi, "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=s=x:p=0", path);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start ffprobe.");
        var output = await p.StandardOutput.ReadToEndAsync(cancellationToken);
        await p.WaitForExitAsync(cancellationToken);
        if (p.ExitCode != 0) throw new InvalidOperationException("ffprobe could not read the master video.");
        var parts = output.Trim().Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h))
            throw new InvalidDataException($"Unexpected ffprobe size: {output.Trim()}");
        return (w, h);
    }

    private async Task RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogReceived?.Invoke(e.Data); };
        if (!process.Start()) throw new InvalidOperationException("FFmpeg could not start.");
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException($"FFmpeg render failed with exit code {process.ExitCode}.");
    }

    private static string ResolveTool(string bundledName, string fallback)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", bundledName);
        return File.Exists(bundled) ? bundled : fallback;
    }

    private static void Add(ProcessStartInfo info, params string[] args)
    {
        foreach (var arg in args) info.ArgumentList.Add(arg);
    }

    private static string EscapeFilterPath(string path)
        => path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");

    private static string Sec(long ms) => F(ms / 1000.0);
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
