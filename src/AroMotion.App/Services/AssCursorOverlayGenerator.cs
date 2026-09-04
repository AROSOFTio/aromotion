using System.Globalization;
using System.Text;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class AssCursorOverlayGenerator
{
    public async Task<string> GenerateAsync(
        string outputPath,
        IReadOnlyList<CursorSample> samples,
        IReadOnlyList<ClickEffectFrame> clickFrames,
        CursorEffectSettings settings,
        int canvasWidth,
        int canvasHeight,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {canvasWidth}");
        sb.AppendLine($"PlayResY: {canvasHeight}");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding");
        sb.AppendLine("Style: Default,Arial,18,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text");

        if (settings.Style != CursorStyle.Hidden && samples.Count > 0)
        {
            // One short vector event per sample interval keeps the pointer editable-looking
            // while avoiding a single enormous nested time expression.
            for (var i = 0; i < samples.Count - 1; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var a = samples[i];
                var b = samples[i + 1];
                var blur = settings.MotionBlur ? Math.Clamp(MotionBlurRadius(a, settings), 0, 18) : 0;
                var draw = CursorDrawing(settings.Style, settings.Size);
                var primary = ToAssColor(settings.Color, settings.Opacity);
                var shadow = settings.Shadow ? Math.Max(0.0, settings.ShadowOpacity * 5.0) : 0.0;
                var tag = $"{{\\an7\\p1\\1c{primary}\\bord0\\shad{F(shadow)}\\blur{F(blur)}\\move({F(a.X)},{F(a.Y)},{F(b.X)},{F(b.Y)},{a.TimestampMs},{b.TimestampMs})}}";
                sb.AppendLine(EventLine(5, a.TimestampMs, Math.Max(a.TimestampMs + 1, b.TimestampMs), tag + draw));
            }
        }

        foreach (var frame in clickFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var duration = 40L;
            var color = ToAssColor(frame.Color, frame.Opacity);
            var radius = Math.Max(2.0, frame.Radius);
            var vector = CircleDrawing(radius, frame.Style == ClickRingStyle.FilledFlash);
            var border = frame.Style == ClickRingStyle.FilledFlash ? 0 : 3;
            var tag = $"{{\\an7\\p1\\pos({F(frame.X)},{F(frame.Y)})\\1c{color}\\3c{color}\\bord{border}\\shad0\\blur0.7}}";
            sb.AppendLine(EventLine(6, frame.TimestampMs, frame.TimestampMs + duration, tag + vector));
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
        return outputPath;
    }

    private static string CursorDrawing(CursorStyle style, double size)
    {
        var s = Math.Clamp(size, 0.25, 4.0);
        return style switch
        {
            CursorStyle.Dot => ScalePath("m -5 -5 b -8 -5 -8 5 -5 8 b 5 8 8 5 8 0 b 8 -5 5 -8 0 -8 b -5 -8 -8 -5 -5 -5", s),
            CursorStyle.Ring => ScalePath("m -7 0 b -7 -4 -4 -7 0 -7 b 4 -7 7 -4 7 0 b 7 4 4 7 0 7 b -4 7 -7 4 -7 0", s),
            CursorStyle.Crosshair => ScalePath("m -8 -1 l -2 -1 l -2 -8 l 2 -8 l 2 -1 l 8 -1 l 8 2 l 2 2 l 2 8 l -2 8 l -2 2 l -8 2", s),
            _ => ScalePath("m 0 0 l 0 19 l 5 14 l 9 23 l 13 21 l 9 12 l 18 12", s)
        };
    }

    private static string CircleDrawing(double radius, bool filled)
    {
        // ASS vector circle approximation with four cubic Bezier arcs.
        var r = radius;
        var k = r * 0.5522847498;
        var path = $"m {F(-r)} 0 b {F(-r)} {F(-k)} {F(-k)} {F(-r)} 0 {F(-r)} b {F(k)} {F(-r)} {F(r)} {F(-k)} {F(r)} 0 b {F(r)} {F(k)} {F(k)} {F(r)} 0 {F(r)} b {F(-k)} {F(r)} {F(-r)} {F(k)} {F(-r)} 0";
        if (filled) return path;

        var inner = Math.Max(1, r - 3);
        var ik = inner * 0.5522847498;
        // Reverse winding for a hollow ring.
        return path + $" m {F(-inner)} 0 b {F(-inner)} {F(ik)} {F(-ik)} {F(inner)} 0 {F(inner)} b {F(ik)} {F(inner)} {F(inner)} {F(ik)} {F(inner)} 0 b {F(inner)} {F(-ik)} {F(ik)} {F(-inner)} 0 {F(-inner)} b {F(-ik)} {F(-inner)} {F(-inner)} {F(-ik)} {F(-inner)} 0";
    }

    private static string ScalePath(string path, double scale)
    {
        // ASS supports fscx/fscy on text but not consistently on p-drawings across renderers;
        // wrap scale into the drawing unit by p level. For this compact cursor set, p1 plus
        // scaled coordinates is more predictable, so keep nominal geometry and use fsc tags
        // only when custom rendering is later introduced.
        return path;
    }

    private static string EventLine(int layer, long startMs, long endMs, string text)
        => $"Dialogue: {layer},{ToAssTime(startMs)},{ToAssTime(endMs)},Default,,0,0,0,,{text}";

    private static string ToAssTime(long ms)
    {
        ms = Math.Max(0, ms);
        var totalCs = ms / 10;
        var cs = totalCs % 100;
        var totalSeconds = totalCs / 100;
        var sec = totalSeconds % 60;
        var totalMinutes = totalSeconds / 60;
        var min = totalMinutes % 60;
        var hour = totalMinutes / 60;
        return $"{hour}:{min:00}:{sec:00}.{cs:00}";
    }

    private static string ToAssColor(string value, double opacity)
    {
        var hex = value.Trim().TrimStart('#');
        if (hex.Length != 6) hex = "FFFFFF";
        var r = hex.Substring(0, 2);
        var g = hex.Substring(2, 2);
        var b = hex.Substring(4, 2);
        var alpha = (int)Math.Round((1.0 - Math.Clamp(opacity, 0, 1)) * 255.0);
        return $"&H{alpha:X2}{b}{g}{r}&";
    }

    private static double MotionBlurRadius(CursorSample sample, CursorEffectSettings settings)
    {
        var speed = Math.Sqrt(sample.VelocityX * sample.VelocityX + sample.VelocityY * sample.VelocityY);
        return Math.Clamp(speed / 1200.0 * settings.MotionBlurStrength * 10.0, 0, 18);
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
