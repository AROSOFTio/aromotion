using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed record EvaluatedSpotlight(
    SpotlightClip Source,
    double X,
    double Y,
    double Width,
    double Height,
    double Darkness,
    double Feather);

public sealed record EvaluatedBlur(
    BlurClip Source,
    double X,
    double Y,
    double Width,
    double Height,
    double Intensity,
    double Feather);

public sealed class AttentionEffectEngine
{
    public IReadOnlyList<EvaluatedSpotlight> EvaluateSpotlights(
        MotionProject project,
        long timestampMs,
        CursorSample? cursor)
    {
        var result = new List<EvaluatedSpotlight>();
        foreach (var clip in project.Spotlights.Where(x => x.Enabled && timestampMs >= x.StartMs && timestampMs <= x.EndMs))
        {
            var x = clip.FollowCursor && cursor is not null ? cursor.X : clip.X;
            var y = clip.FollowCursor && cursor is not null ? cursor.Y : clip.Y;
            result.Add(new EvaluatedSpotlight(
                clip,
                x,
                y,
                clip.Width,
                clip.Height,
                Math.Clamp(clip.Darkness, 0, 1),
                Math.Clamp(clip.Feather, 0, 1)));
        }
        return result;
    }

    public IReadOnlyList<EvaluatedBlur> EvaluateBlurs(
        MotionProject project,
        long timestampMs,
        CursorSample? cursor)
    {
        var result = new List<EvaluatedBlur>();
        foreach (var clip in project.Blurs.Where(x => x.Enabled && timestampMs >= x.StartMs && timestampMs <= x.EndMs))
        {
            var x = clip.TrackCursor && cursor is not null ? cursor.X : clip.X;
            var y = clip.TrackCursor && cursor is not null ? cursor.Y : clip.Y;
            result.Add(new EvaluatedBlur(
                clip,
                x,
                y,
                clip.Width,
                clip.Height,
                Math.Clamp(clip.Intensity, 0.1, 80),
                Math.Clamp(clip.Feather, 0, 1)));
        }
        return result;
    }
}
