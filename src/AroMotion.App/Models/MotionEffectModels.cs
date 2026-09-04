namespace AroMotion.App.Models;

public enum EasingKind
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
    Cubic,
    SmoothStep,
    SpringSoft,
    SpringSnappy
}

public enum ZoomStyle
{
    Focus,
    CursorFollow,
    SmartFrame,
    Push,
    Punch,
    Gentle
}

public enum Motion3DPreset
{
    None,
    Focus,
    Push,
    Tilt,
    Reveal,
    Orbit,
    Parallax
}

public enum CursorStyle
{
    Recorded,
    WindowsArrow,
    Dot,
    Ring,
    Crosshair,
    Custom,
    Hidden
}

public enum ClickRingStyle
{
    None,
    Ripple,
    Pulse,
    DoubleRing,
    FilledFlash
}

public enum SpotlightShape
{
    Circle,
    Rectangle,
    RoundedRectangle
}

public enum BlurShape
{
    Rectangle,
    RoundedRectangle,
    Circle
}

public sealed record MotionPoint(double X, double Y);

public sealed record ZoomClip(
    Guid Id,
    long StartMs,
    long ZoomInMs,
    long HoldMs,
    long ZoomOutMs,
    double Scale,
    double FocusX,
    double FocusY,
    EasingKind Easing,
    ZoomStyle Style,
    bool Enabled,
    string Source)
{
    public long EndMs => StartMs + ZoomInMs + HoldMs + ZoomOutMs;
}

public sealed record Motion3DClip(
    Guid Id,
    long StartMs,
    long DurationMs,
    Motion3DPreset Preset,
    double RotateX,
    double RotateY,
    double RotateZ,
    double PanX,
    double PanY,
    double DepthZ,
    double Perspective,
    double Orbit,
    double Parallax,
    double Intensity,
    long HoldMs,
    EasingKind Easing,
    bool Shadow,
    double ShadowOpacity,
    bool Reflection,
    double ReflectionOpacity,
    bool Enabled,
    string Source)
{
    public long EndMs => StartMs + DurationMs + HoldMs;
}

public sealed record CursorEffectSettings(
    CursorStyle Style,
    string? CustomCursorPath,
    string Color,
    double Size,
    double Opacity,
    bool Shadow,
    double ShadowOpacity,
    bool SmoothMovement,
    double Smoothing,
    bool MotionBlur,
    double MotionBlurStrength,
    ClickRingStyle LeftClickStyle,
    string LeftClickColor,
    ClickRingStyle RightClickStyle,
    string RightClickColor,
    int ClickAnimationMs,
    bool ClickSound,
    string? LeftClickSoundPath,
    string? RightClickSoundPath);

public sealed record SpotlightClip(
    Guid Id,
    long StartMs,
    long DurationMs,
    SpotlightShape Shape,
    double X,
    double Y,
    double Width,
    double Height,
    double Darkness,
    double Feather,
    bool FollowCursor,
    bool Enabled)
{
    public long EndMs => StartMs + DurationMs;
}

public sealed record BlurClip(
    Guid Id,
    long StartMs,
    long DurationMs,
    BlurShape Shape,
    double X,
    double Y,
    double Width,
    double Height,
    double Intensity,
    double Feather,
    bool TrackCursor,
    bool Enabled)
{
    public long EndMs => StartMs + DurationMs;
}

public sealed record MotionProject(
    List<ZoomClip> Zooms,
    List<Motion3DClip> Motions3D,
    CursorEffectSettings Cursor,
    List<SpotlightClip> Spotlights,
    List<BlurClip> Blurs)
{
    public static MotionProject CreateDefault() => new(
        new List<ZoomClip>(),
        new List<Motion3DClip>(),
        new CursorEffectSettings(
            CursorStyle.WindowsArrow,
            null,
            "#FFFFFF",
            1.0,
            1.0,
            true,
            0.35,
            true,
            0.72,
            true,
            0.35,
            ClickRingStyle.Ripple,
            "#5B8CFF",
            ClickRingStyle.Pulse,
            "#FF5A70",
            420,
            true,
            null,
            null),
        new List<SpotlightClip>(),
        new List<BlurClip>());
}
