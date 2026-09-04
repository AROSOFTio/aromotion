using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed record EvaluatedZoom(double Scale, double FocusX, double FocusY, double Progress, ZoomClip? Source);
public sealed record Evaluated3D(
    double RotateX,
    double RotateY,
    double RotateZ,
    double PanX,
    double PanY,
    double DepthZ,
    double Perspective,
    double Orbit,
    double Parallax,
    double ShadowOpacity,
    double ReflectionOpacity,
    double Progress,
    Motion3DClip? Source);

public sealed class MotionEvaluator
{
    public EvaluatedZoom EvaluateZoom(MotionProject project, long timestampMs)
    {
        var clip = project.Zooms
            .Where(x => x.Enabled && timestampMs >= x.StartMs && timestampMs <= x.EndMs)
            .OrderByDescending(x => x.StartMs)
            .FirstOrDefault();

        if (clip is null)
            return new EvaluatedZoom(1.0, 0.5, 0.5, 0, null);

        var local = timestampMs - clip.StartMs;
        double scale;
        double progress;

        if (local <= clip.ZoomInMs)
        {
            progress = Ease(SafeRatio(local, clip.ZoomInMs), clip.Easing);
            scale = Lerp(1.0, clip.Scale, progress);
        }
        else if (local <= clip.ZoomInMs + clip.HoldMs)
        {
            progress = 1.0;
            scale = clip.Scale;
        }
        else
        {
            var outLocal = local - clip.ZoomInMs - clip.HoldMs;
            var eased = Ease(SafeRatio(outLocal, clip.ZoomOutMs), clip.Easing);
            progress = 1.0 - eased;
            scale = Lerp(clip.Scale, 1.0, eased);
        }

        return new EvaluatedZoom(scale, clip.FocusX, clip.FocusY, progress, clip);
    }

    public Evaluated3D Evaluate3D(MotionProject project, long timestampMs)
    {
        var clip = project.Motions3D
            .Where(x => x.Enabled && timestampMs >= x.StartMs && timestampMs <= x.EndMs)
            .OrderByDescending(x => x.StartMs)
            .FirstOrDefault();

        if (clip is null)
            return new Evaluated3D(0, 0, 0, 0, 0, 0, 1000, 0, 0, 0, 0, 0, null);

        var activeDuration = Math.Max(1, clip.DurationMs);
        var local = Math.Min(activeDuration, timestampMs - clip.StartMs);
        var raw = SafeRatio(local, activeDuration);
        var eased = Ease(raw, clip.Easing);

        // 3D clips enter and settle. When a hold exists, keep the final pose.
        var weight = timestampMs <= clip.StartMs + clip.DurationMs ? eased : 1.0;
        return new Evaluated3D(
            clip.RotateX * weight,
            clip.RotateY * weight,
            clip.RotateZ * weight,
            clip.PanX * weight,
            clip.PanY * weight,
            clip.DepthZ * weight,
            Lerp(1000, clip.Perspective, weight),
            clip.Orbit * weight,
            clip.Parallax * weight,
            clip.Shadow ? clip.ShadowOpacity * weight : 0,
            clip.Reflection ? clip.ReflectionOpacity * weight : 0,
            weight,
            clip);
    }

    public IReadOnlyList<SpotlightClip> ActiveSpotlights(MotionProject project, long timestampMs)
        => project.Spotlights.Where(x => x.Enabled && timestampMs >= x.StartMs && timestampMs <= x.EndMs).ToArray();

    public IReadOnlyList<BlurClip> ActiveBlurs(MotionProject project, long timestampMs)
        => project.Blurs.Where(x => x.Enabled && timestampMs >= x.StartMs && timestampMs <= x.EndMs).ToArray();

    public static double Ease(double t, EasingKind kind)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return kind switch
        {
            EasingKind.Linear => t,
            EasingKind.EaseIn => t * t,
            EasingKind.EaseOut => 1 - Math.Pow(1 - t, 2),
            EasingKind.EaseInOut => t < .5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
            EasingKind.Cubic => t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
            EasingKind.SmoothStep => t * t * (3 - 2 * t),
            EasingKind.SpringSoft => Spring(t, 7.5, 0.78),
            EasingKind.SpringSnappy => Spring(t, 11.0, 0.62),
            _ => t
        };
    }

    private static double Spring(double t, double frequency, double damping)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        var value = 1 - Math.Exp(-damping * frequency * t) * Math.Cos(frequency * t);
        // Normalize and clamp lightly to keep a pleasing overshoot without allowing wild camera jumps.
        return Math.Clamp(value, 0, 1.08);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double SafeRatio(long value, long total) => total <= 0 ? 1.0 : Math.Clamp((double)value / total, 0.0, 1.0);
}
