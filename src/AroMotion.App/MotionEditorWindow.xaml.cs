using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AroMotion.App.Models;
using AroMotion.App.Services;

namespace AroMotion.App;

public partial class MotionEditorWindow : Window
{
    private readonly string _projectDirectory;
    private readonly MotionTimelineService _timeline = new();
    private readonly AutoZoomGenerator _autoZoom = new();
    private readonly MotionRenderService _renderer = new();
    private readonly DispatcherTimer _playTimer;
    private MotionProjectState _project = new();
    private ZoomSegment? _selectedZoom;
    private Motion3DSegment? _selected3D;
    private SpotlightEffect? _selectedSpotlight;
    private BlurEffect? _selectedBlur;
    private bool _loadingInspector;
    private bool _playing;
    private double _durationMs = 60_000;
    private double _pxPerMs = 0.02;

    private Border? _dragBlock;
    private object? _dragItem;
    private string _dragMode = "move";
    private Point _dragStartPoint;
    private long _dragOriginalStart;
    private long _dragOriginalEnd;

    public MotionEditorWindow(string projectDirectory)
    {
        InitializeComponent();
        _projectDirectory = projectDirectory;
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _playTimer.Tick += (_, _) => UpdatePlayheadFromMedia();
        Loaded += MotionEditorWindow_Loaded;
    }

    private async void MotionEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _project = await _timeline.OpenAsync(_projectDirectory);
        ProjectNameText.Text = new DirectoryInfo(_projectDirectory).Name;
        LoadCursorInspector();

        if (!string.IsNullOrWhiteSpace(_project.SourceVideo) && File.Exists(_project.SourceVideo))
        {
            PreviewMedia.Source = new Uri(_project.SourceVideo);
            PreviewMedia.Position = TimeSpan.Zero;
            PreviewMedia.Pause();
        }

        RebuildTimeline();
    }

    private void PreviewMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (PreviewMedia.NaturalDuration.HasTimeSpan)
        {
            _durationMs = Math.Max(1000, PreviewMedia.NaturalDuration.TimeSpan.TotalMilliseconds);
            PlayheadSlider.Maximum = _durationMs;
        }
        RebuildTimeline();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await _timeline.SaveAsync(_projectDirectory);
        ProjectNameText.Text = new DirectoryInfo(_projectDirectory).Name + "  ✓ saved";
    }

    private async void Render_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_project.SourceVideo) || !File.Exists(_project.SourceVideo))
        {
            MessageBox.Show(this, "This project has no source video.", "AROMOTION", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await _timeline.SaveAsync(_projectDirectory);
        var output = Path.Combine(_projectDirectory, "AROMOTION-motion-preview.mp4");
        try
        {
            ProjectNameText.Text = "Rendering motion preview…";
            await _renderer.RenderAsync(_project, output, progress => Dispatcher.Invoke(() => ProjectNameText.Text = progress));
            ProjectNameText.Text = "Render ready ✓";
            if (MessageBox.Show(this, "Motion preview rendered. Open it now?", "AROMOTION", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo { FileName = output, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            ProjectNameText.Text = "Render failed";
            MessageBox.Show(this, ex.Message, "AROMOTION render", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AutoZoom_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_project.EventsPath) || !File.Exists(_project.EventsPath))
        {
            MessageBox.Show(this, "No interaction metadata exists for this recording.", "AROMOTION", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var options = new AutoZoomOptions
        {
            CursorFollow = _project.Cursor.CursorFollowZoom,
            DefaultScale = ZoomScaleSlider.Value is >= 1.05 and <= 4 ? ZoomScaleSlider.Value : 1.75,
            Easing = ComboText(ZoomEasingCombo, "cubic-out"),
            Style = ComboText(ZoomStyleCombo, "Focus"),
            SmartFraming = SmartFrameCheck.IsChecked == true
        };
        var generated = await _autoZoom.GenerateAsync(_project.EventsPath, options);
        _timeline.ReplaceAutoZooms(generated);
        RebuildTimeline();
    }

    private void AddZoom_Click(object sender, RoutedEventArgs e)
    {
        var p = PreviewCenterVideoPoint();
        _selectedZoom = _timeline.AddManualZoom(CurrentMs, p.X, p.Y, 1.75);
        SelectZoom(_selectedZoom);
        RebuildTimeline();
    }

    private void Add3D_Click(object sender, RoutedEventArgs e)
    {
        var preset = ComboText(PresetCombo, "Focus");
        _selected3D = _timeline.Add3D(CurrentMs, Math.Min((long)_durationMs, CurrentMs + 2200), preset);
        Select3D(_selected3D);
        RebuildTimeline();
    }

    private void AddSpotlight_Click(object sender, RoutedEventArgs e)
    {
        var p = PreviewCenterVideoPoint();
        _selectedSpotlight = _timeline.AddSpotlight(CurrentMs, Math.Min((long)_durationMs, CurrentMs + 2200), p.X, p.Y);
        _selectedBlur = null;
        InspectorTabs.SelectedIndex = 3;
        RebuildTimeline();
    }

    private void AddBlur_Click(object sender, RoutedEventArgs e)
    {
        var p = PreviewCenterVideoPoint();
        _selectedBlur = _timeline.AddBlur(CurrentMs, Math.Min((long)_durationMs, CurrentMs + 2200), p.X, p.Y);
        _selectedSpotlight = null;
        InspectorTabs.SelectedIndex = 3;
        RebuildTimeline();
    }

    private void DeleteZoom_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedZoom is null) return;
        _timeline.DeleteZoom(_selectedZoom);
        _selectedZoom = null;
        RebuildTimeline();
        ApplyPreviewTransform();
    }

    private void Delete3D_Click(object sender, RoutedEventArgs e)
    {
        if (_selected3D is null) return;
        _timeline.Delete3D(_selected3D);
        _selected3D = null;
        RebuildTimeline();
    }

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedZoom is null || PreviewMedia.NaturalVideoWidth <= 0 || PreviewMedia.NaturalVideoHeight <= 0) return;
        var mouse = e.GetPosition(PreviewHost);
        var point = PreviewToVideo(mouse);
        _timeline.SetZoomFocus(_selectedZoom, point.X, point.Y);
        ApplyPreviewTransform();
        RebuildTimeline();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_playing)
        {
            PreviewMedia.Pause();
            _playTimer.Stop();
            _playing = false;
        }
        else
        {
            PreviewMedia.Play();
            _playTimer.Start();
            _playing = true;
        }
    }

    private void PlayheadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _playing) return;
        PreviewMedia.Position = TimeSpan.FromMilliseconds(e.NewValue);
        TimeText.Text = FormatTime(e.NewValue);
        ApplyPreviewTransform();
    }

    private void UpdatePlayheadFromMedia()
    {
        if (!_playing) return;
        var ms = PreviewMedia.Position.TotalMilliseconds;
        PlayheadSlider.Value = Math.Clamp(ms, 0, _durationMs);
        TimeText.Text = FormatTime(ms);
        ApplyPreviewTransform();
        if (ms >= _durationMs - 50)
        {
            PreviewMedia.Pause();
            _playTimer.Stop();
            _playing = false;
        }
    }

    private void ZoomProperty_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingInspector || _selectedZoom is null) return;
        _selectedZoom.Scale = ZoomScaleSlider.Value;
        _selectedZoom.Easing = ComboText(ZoomEasingCombo, _selectedZoom.Easing);
        _selectedZoom.Style = ComboText(ZoomStyleCombo, _selectedZoom.Style);
        if (long.TryParse(ZoomInText.Text, out var zin)) _selectedZoom.ZoomInMs = Math.Clamp(zin, 20, 10_000);
        if (long.TryParse(ZoomHoldText.Text, out var hold)) _selectedZoom.HoldMs = Math.Clamp(hold, 0, 60_000);
        if (long.TryParse(ZoomOutText.Text, out var zout)) _selectedZoom.ZoomOutMs = Math.Clamp(zout, 20, 10_000);
        _selectedZoom.SmartFrame = SmartFrameCheck.IsChecked == true;
        _selectedZoom.EndMs = _selectedZoom.StartMs + _selectedZoom.ZoomInMs + _selectedZoom.HoldMs + _selectedZoom.ZoomOutMs;
        _timeline.Touch();
        ApplyPreviewTransform();
        RebuildTimeline();
    }

    private void ThreeDProperty_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingInspector || _selected3D is null) return;
        _selected3D.RotateX = RotateXSlider.Value;
        _selected3D.RotateY = RotateYSlider.Value;
        _selected3D.RotateZ = RotateZSlider.Value;
        _selected3D.Depth = DepthSlider.Value;
        _selected3D.PanX = PanXSlider.Value;
        _selected3D.PanY = PanYSlider.Value;
        _selected3D.Perspective = PerspectiveSlider.Value;
        _selected3D.Speed = SpeedSlider.Value;
        _selected3D.Intensity = IntensitySlider.Value;
        _selected3D.Shadow = ShadowCheck.IsChecked == true;
        _selected3D.Reflection = ReflectionCheck.IsChecked == true;
        _timeline.Touch();
        ApplyPreviewTransform();
    }

    private void CursorProperty_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingInspector || !IsLoaded) return;
        var c = _project.Cursor;
        c.Style = ComboText(CursorStyleCombo, c.Style);
        c.ReplaceOriginalCursor = ReplaceCursorCheck.IsChecked == true;
        c.HideCursor = HideCursorCheck.IsChecked == true;
        c.Shadow = CursorShadowCheck.IsChecked == true;
        c.Size = CursorSizeSlider.Value;
        c.Opacity = CursorOpacitySlider.Value;
        c.Smoothing = CursorSmoothingSlider.Value;
        c.MotionBlur = CursorBlurSlider.Value;
        c.ClickRingStyle = ComboText(ClickRingCombo, c.ClickRingStyle);
        c.ClickSoundEnabled = ClickSoundCheck.IsChecked == true;
        c.CursorFollowZoom = CursorFollowCheck.IsChecked == true;
        _timeline.Touch();
    }

    private void PrivacyProperty_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingInspector) return;
        if (_selectedSpotlight is not null)
        {
            _selectedSpotlight.Darkness = SpotlightDarknessSlider.Value;
            _selectedSpotlight.Feather = SpotlightFeatherSlider.Value;
            _selectedSpotlight.FollowCursor = SpotlightFollowCheck.IsChecked == true;
        }
        if (_selectedBlur is not null)
        {
            _selectedBlur.Intensity = BlurIntensitySlider.Value;
            _selectedBlur.TrackCursor = BlurTrackCheck.IsChecked == true;
        }
        _timeline.Touch();
    }

    private void SelectZoom(ZoomSegment zoom)
    {
        _selectedZoom = zoom;
        _selected3D = null;
        _selectedSpotlight = null;
        _selectedBlur = null;
        _loadingInspector = true;
        ZoomScaleSlider.Value = zoom.Scale;
        SelectCombo(ZoomEasingCombo, zoom.Easing);
        SelectCombo(ZoomStyleCombo, zoom.Style);
        ZoomInText.Text = zoom.ZoomInMs.ToString();
        ZoomHoldText.Text = zoom.HoldMs.ToString();
        ZoomOutText.Text = zoom.ZoomOutMs.ToString();
        SmartFrameCheck.IsChecked = zoom.SmartFrame;
        _loadingInspector = false;
        InspectorTabs.SelectedIndex = 0;
        ApplyPreviewTransform();
    }

    private void Select3D(Motion3DSegment motion)
    {
        _selected3D = motion;
        _selectedZoom = null;
        _selectedSpotlight = null;
        _selectedBlur = null;
        _loadingInspector = true;
        RotateXSlider.Value = motion.RotateX;
        RotateYSlider.Value = motion.RotateY;
        RotateZSlider.Value = motion.RotateZ;
        DepthSlider.Value = motion.Depth;
        PanXSlider.Value = motion.PanX;
        PanYSlider.Value = motion.PanY;
        PerspectiveSlider.Value = motion.Perspective;
        SpeedSlider.Value = motion.Speed;
        IntensitySlider.Value = motion.Intensity;
        ShadowCheck.IsChecked = motion.Shadow;
        ReflectionCheck.IsChecked = motion.Reflection;
        _loadingInspector = false;
        InspectorTabs.SelectedIndex = 1;
        ApplyPreviewTransform();
    }

    private void LoadCursorInspector()
    {
        _loadingInspector = true;
        var c = _project.Cursor;
        SelectCombo(CursorStyleCombo, c.Style);
        ReplaceCursorCheck.IsChecked = c.ReplaceOriginalCursor;
        HideCursorCheck.IsChecked = c.HideCursor;
        CursorShadowCheck.IsChecked = c.Shadow;
        CursorSizeSlider.Value = c.Size;
        CursorOpacitySlider.Value = c.Opacity;
        CursorSmoothingSlider.Value = c.Smoothing;
        CursorBlurSlider.Value = c.MotionBlur;
        SelectCombo(ClickRingCombo, c.ClickRingStyle);
        ClickSoundCheck.IsChecked = c.ClickSoundEnabled;
        CursorFollowCheck.IsChecked = c.CursorFollowZoom;
        _loadingInspector = false;
    }

    private void ApplyPreviewTransform()
    {
        var scale = 1.0;
        var tx = 0.0;
        var ty = 0.0;
        var now = CurrentMs;

        var zoom = _project.Zooms.LastOrDefault(z => z.Enabled && now >= z.StartMs && now <= z.EndMs);
        if (zoom is not null)
        {
            var t = ProgressForSegment(now, zoom.StartMs, zoom.EndMs, zoom.ZoomInMs, zoom.ZoomOutMs, zoom.Easing);
            scale = 1 + (zoom.Scale - 1) * t;
            if (PreviewMedia.NaturalVideoWidth > 0 && PreviewMedia.NaturalVideoHeight > 0)
            {
                var nx = zoom.FocusX / (double)PreviewMedia.NaturalVideoWidth - .5;
                var ny = zoom.FocusY / (double)PreviewMedia.NaturalVideoHeight - .5;
                tx = -nx * PreviewHost.ActualWidth * (scale - 1);
                ty = -ny * PreviewHost.ActualHeight * (scale - 1);
            }
        }

        var motion = _project.Motions3D.LastOrDefault(m => m.Enabled && now >= m.StartMs && now <= m.EndMs);
        var angle = 0.0;
        if (motion is not null)
        {
            var p = MotionPresetService.Ease(motion.Easing, (now - motion.StartMs) / (double)Math.Max(1, motion.EndMs - motion.StartMs));
            angle = motion.RotateZ * motion.Intensity * p;
            scale *= 1 + (motion.Depth / 100.0) * motion.Intensity * p;
            tx += motion.PanX * motion.Intensity * p;
            ty += motion.PanY * motion.Intensity * p;
        }

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(scale, scale, PreviewHost.ActualWidth / 2, PreviewHost.ActualHeight / 2));
        group.Children.Add(new RotateTransform(angle, PreviewHost.ActualWidth / 2, PreviewHost.ActualHeight / 2));
        group.Children.Add(new TranslateTransform(tx, ty));
        PreviewMedia.RenderTransform = group;
    }

    private void RebuildTimeline()
    {
        if (!IsLoaded) return;
        _pxPerMs = Math.Clamp(1200.0 / Math.Max(_durationMs, 1), 0.006, 0.08);
        TimelineCanvas.Width = Math.Max(1200, _durationMs * _pxPerMs + 120);
        TimelineCanvas.Children.Clear();
        DrawRow("ZOOM", 8, "#7C5CFF");
        DrawRow("3D", 50, "#38A6FF");
        DrawRow("SPOT", 92, "#FFC857");
        DrawRow("BLUR", 134, "#FF5D78");
        foreach (var z in _project.Zooms) AddBlock(z, z.StartMs, z.EndMs, 8, "#6E51E8", $"{z.Scale:0.00}× {z.Style}");
        foreach (var m in _project.Motions3D) AddBlock(m, m.StartMs, m.EndMs, 50, "#177CB8", m.Preset);
        foreach (var s in _project.Spotlights) AddBlock(s, s.StartMs, s.EndMs, 92, "#B98A24", s.Shape);
        foreach (var b in _project.Blurs) AddBlock(b, b.StartMs, b.EndMs, 134, "#A83A50", "Privacy Blur");
        var playhead = new Rectangle { Width = 2, Height = 170, Fill = Brushes.White, Opacity = .65, IsHitTestVisible = false };
        Canvas.SetLeft(playhead, 72 + CurrentMs * _pxPerMs);
        TimelineCanvas.Children.Add(playhead);
    }

    private void DrawRow(string label, double top, string color)
    {
        var text = new TextBlock { Text = label, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), FontSize = 10, FontWeight = FontWeights.Bold };
        Canvas.SetLeft(text, 8); Canvas.SetTop(text, top + 10); TimelineCanvas.Children.Add(text);
        var line = new Rectangle { Height = 1, Width = TimelineCanvas.Width - 70, Fill = new SolidColorBrush(Color.FromRgb(35, 42, 55)), IsHitTestVisible = false };
        Canvas.SetLeft(line, 70); Canvas.SetTop(line, top + 38); TimelineCanvas.Children.Add(line);
    }

    private void AddBlock(object item, long startMs, long endMs, double top, string color, string text)
    {
        var block = new Border
        {
            Tag = item,
            Width = Math.Max(24, (endMs - startMs) * _pxPerMs),
            Height = 32,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 10, Margin = new Thickness(7, 7, 7, 0), TextTrimming = TextTrimming.CharacterEllipsis }
        };
        Canvas.SetLeft(block, 72 + startMs * _pxPerMs);
        Canvas.SetTop(block, top + 3);
        TimelineCanvas.Children.Add(block);
    }

    private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var border = FindTaggedBorder(e.OriginalSource as DependencyObject);
        if (border?.Tag is null) return;
        _dragBlock = border;
        _dragItem = border.Tag;
        var p = e.GetPosition(border);
        _dragMode = p.X <= 8 ? "left" : p.X >= border.ActualWidth - 8 ? "right" : "move";
        _dragStartPoint = e.GetPosition(TimelineCanvas);
        (_dragOriginalStart, _dragOriginalEnd) = GetTimes(_dragItem);
        border.CaptureMouse();
        SelectTimelineItem(_dragItem);
        e.Handled = true;
    }

    private void TimelineCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragBlock is null || _dragItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        var deltaMs = (long)((e.GetPosition(TimelineCanvas).X - _dragStartPoint.X) / _pxPerMs);
        var start = _dragOriginalStart;
        var end = _dragOriginalEnd;
        if (_dragMode == "move") { var d = Math.Max(-start, deltaMs); start += d; end += d; }
        else if (_dragMode == "left") start = Math.Clamp(start + deltaMs, 0, end - 80);
        else end = Math.Clamp(end + deltaMs, start + 80, (long)_durationMs);
        SetTimes(_dragItem, start, end);
        RebuildTimeline();
    }

    private void TimelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragBlock is not null) _dragBlock.ReleaseMouseCapture();
        _dragBlock = null; _dragItem = null; _timeline.Touch();
    }

    private void SelectTimelineItem(object item)
    {
        switch (item)
        {
            case ZoomSegment z: SelectZoom(z); break;
            case Motion3DSegment m: Select3D(m); break;
            case SpotlightEffect s:
                _selectedSpotlight = s; _selectedBlur = null; InspectorTabs.SelectedIndex = 3;
                _loadingInspector = true; SpotlightDarknessSlider.Value = s.Darkness; SpotlightFeatherSlider.Value = s.Feather; SpotlightFollowCheck.IsChecked = s.FollowCursor; _loadingInspector = false;
                break;
            case BlurEffect b:
                _selectedBlur = b; _selectedSpotlight = null; InspectorTabs.SelectedIndex = 3;
                _loadingInspector = true; BlurIntensitySlider.Value = b.Intensity; BlurTrackCheck.IsChecked = b.TrackCursor; _loadingInspector = false;
                break;
        }
    }

    private static Border? FindTaggedBorder(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Border b && b.Tag is not null) return b;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static (long Start, long End) GetTimes(object item) => item switch
    {
        ZoomSegment z => (z.StartMs, z.EndMs), Motion3DSegment m => (m.StartMs, m.EndMs),
        SpotlightEffect s => (s.StartMs, s.EndMs), BlurEffect b => (b.StartMs, b.EndMs), _ => (0, 1)
    };

    private static void SetTimes(object item, long start, long end)
    {
        switch (item)
        {
            case ZoomSegment z: z.StartMs = start; z.EndMs = end; break;
            case Motion3DSegment m: m.StartMs = start; m.EndMs = end; break;
            case SpotlightEffect s: s.StartMs = start; s.EndMs = end; break;
            case BlurEffect b: b.StartMs = start; b.EndMs = end; break;
        }
    }

    private long CurrentMs => (long)Math.Clamp(PlayheadSlider.Value, 0, _durationMs);

    private (int X, int Y) PreviewCenterVideoPoint() => PreviewMedia.NaturalVideoWidth > 0
        ? (PreviewMedia.NaturalVideoWidth / 2, PreviewMedia.NaturalVideoHeight / 2)
        : (_project.CanvasWidth / 2, _project.CanvasHeight / 2);

    private (int X, int Y) PreviewToVideo(Point p)
    {
        var vw = PreviewMedia.NaturalVideoWidth;
        var vh = PreviewMedia.NaturalVideoHeight;
        if (vw <= 0 || vh <= 0) return PreviewCenterVideoPoint();
        var scale = Math.Min(PreviewHost.ActualWidth / vw, PreviewHost.ActualHeight / vh);
        var drawnW = vw * scale; var drawnH = vh * scale;
        var ox = (PreviewHost.ActualWidth - drawnW) / 2; var oy = (PreviewHost.ActualHeight - drawnH) / 2;
        return ((int)Math.Clamp((p.X - ox) / scale, 0, vw - 1), (int)Math.Clamp((p.Y - oy) / scale, 0, vh - 1));
    }

    private static double ProgressForSegment(long now, long start, long end, long zoomIn, long zoomOut, string easing)
    {
        if (now < start || now > end) return 0;
        var inEnd = start + zoomIn;
        var outStart = end - zoomOut;
        if (now < inEnd) return MotionPresetService.Ease(easing, (now - start) / (double)Math.Max(1, zoomIn));
        if (now <= outStart) return 1;
        return MotionPresetService.Ease(easing, (end - now) / (double)Math.Max(1, zoomOut));
    }

    private static string ComboText(ComboBox combo, string fallback) => combo.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? fallback : fallback;
    private static void SelectCombo(ComboBox combo, string value)
    {
        foreach (var obj in combo.Items)
            if (obj is ComboBoxItem item && string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase)) { combo.SelectedItem = item; return; }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }
    private static string FormatTime(double ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"mm\:ss\.fff");
}
