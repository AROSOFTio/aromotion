using AroMotion.App.Models;

namespace AroMotion.App.Services;

public static class MotionPresetService
{
    public static readonly string[] Presets = ["Focus", "Push", "Tilt", "Reveal", "Orbit"];
    public static readonly string[] Easings = [
        "linear", "smoothstep", "cubic-in", "cubic-out", "cubic-in-out", "spring-soft", "spring-snappy"
    ];

    public static Motion3DSegment CreatePreset(string preset, long startMs, long endMs)
    {
        var result = new Motion3DSegment
        {
            StartMs = Math.Max(0, startMs),
            EndMs = Math.Max(startMs + 1, endMs),
            Preset = preset,
            Easing = "cubic-in-out",
            Perspective = 1.0,
            Speed = 1.0,
            Intensity = 1.0,
            HoldMs = 650,
            Shadow = true
        };

        switch (preset)
        {
            case "Push": result.Depth = 18; result.RotateX = -2; result.RotateY = 5; break;
            case "Tilt": result.RotateX = -7; result.RotateY = 10; result.Depth = 8; result.Perspective = 1.15; break;
            case "Reveal": result.PanX = 12; result.RotateY = -12; result.Depth = 12; result.Perspective = 1.2; break;
            case "Orbit": result.RotateX = -5; result.RotateY = 15; result.RotateZ = 1.5; result.PanX = 6; result.Depth = 10; result.Perspective = 1.25; break;
            default: result.Depth = 14; result.RotateX = -2.5; result.RotateY = 4; break;
        }

        var duration = Math.Max(1, result.EndMs - result.StartMs);
        var inTime = result.StartMs + Math.Min(420, duration / 3);
        var outTime = result.EndMs - Math.Min(420, duration / 3);
        result.Keyframes.Add(new Motion3DKeyframe { TimeMs = result.StartMs, Easing = result.Easing });
        result.Keyframes.Add(Peak(result, inTime));
        if (outTime > inTime + 60) result.Keyframes.Add(Peak(result, outTime));
        result.Keyframes.Add(new Motion3DKeyframe { TimeMs = result.EndMs, Easing = result.Easing });
        return result;
    }

    public static Motion3DKeyframe Peak(Motion3DSegment m, long timeMs) => new()
    {
        TimeMs = timeMs,
        RotateX = m.RotateX,
        RotateY = m.RotateY,
        RotateZ = m.RotateZ,
        Depth = m.Depth,
        PanX = m.PanX,
        PanY = m.PanY,
        Perspective = m.Perspective,
        Easing = m.Easing
    };

    public static double Ease(string easing, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return easing switch
        {
            "linear" => t,
            "smoothstep" => t * t * (3 - 2 * t),
            "cubic-in" => t * t * t,
            "cubic-out" => 1 - Math.Pow(1 - t, 3),
            "cubic-in-out" => t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
            "spring-soft" => Spring(t, 7.0, 0.55),
            "spring-snappy" => Spring(t, 10.5, 0.42),
            _ => t * t * (3 - 2 * t)
        };
    }

    private static double Spring(double t, double frequency, double damping)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        var value = 1 - Math.Exp(-frequency * damping * t) * Math.Cos(frequency * t);
        return Math.Clamp(value, 0, 1.08);
    }
}
