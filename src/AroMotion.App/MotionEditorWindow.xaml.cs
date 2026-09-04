using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AroMotion.App.Models;
using AroMotion.App.Services;

namespace AroMotion.App;

public partial class MotionEditorWindow : Window
{
    private readonly string _projectDirectory;
    private readonly string _eventsPath;
    private readonly string _motionProjectPath;
    private readonly MotionProjectStore _store = new();
    private readonly SmartMotionEngine _engine = new();

    public MotionEditorWindow(string projectDirectory, string eventsPath)
    {
        InitializeComponent();
        _projectDirectory = projectDirectory;
        _eventsPath = eventsPath;
        _motionProjectPath = Path.Combine(projectDirectory, "motion-project.json");
        ProjectPathText.Text = projectDirectory;

        FillEnum<EasingKind>(AutoEasingCombo, EasingKind.SpringSoft);
        FillEnum<EasingKind>(ZoomEditEasing, EasingKind.SpringSoft);
        FillEnum<ZoomStyle>(AutoZoomStyleCombo, ZoomStyle.Focus);
        FillEnum<Motion3DPreset>(Auto3DPresetCombo, Motion3DPreset.Focus);
        FillEnum<Motion3DPreset>(ThreeDPresetEdit, Motion3DPreset.Focus);
        FillEnum<CursorStyle>(CursorStyleCombo, CursorStyle.WindowsArrow);
        FillEnum<ClickRingStyle>(LeftClickStyleCombo, ClickRingStyle.Ripple);
        FillEnum<ClickRingStyle>(RightClickStyleCombo, ClickRingStyle.Pulse);
        FillEnum<SpotlightShape>(SpotlightShapeCombo, SpotlightShape.Circle);
        FillEnum<BlurShape>(BlurShapeCombo, BlurShape.RoundedRectangle);

        _store.Changed += RefreshGrids;
        Loaded += async (_, _) => await LoadOrGenerateAsync();
    }

    private async Task LoadOrGenerateAsync()
    {
        try
        {
            if (File.Exists(_motionProjectPath))
            {
                await _store.LoadAsync(_motionProjectPath);
                StatusText.Text = "Loaded motion-project.json";
            }
            else
            {
                await GenerateAsync();
                await _store.SaveAsync(_motionProjectPath);
                StatusText.Text = "Generated editable motion from recorded interaction metadata";
            }
            LoadCursorFields(_store.Project.Cursor);
            RefreshGrids();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "AROMOTION Motion Editor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AutoGenerate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Generating smart motion…";
            await GenerateAsync();
            await _store.SaveAsync(_motionProjectPath);
            StatusText.Text = "Auto motion regenerated and saved";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Auto motion", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = "Generation failed";
        }
    }

    private async Task GenerateAsync()
    {
        var currentCursor = _store.Project.Cursor;
        var options = new SmartMotionOptions
        {
            ZoomOnClicks = ZoomClicksCheck.IsChecked == true,
            ZoomOnShortcuts = ZoomShortcutsCheck.IsChecked == true,
            CursorFollow = CursorFollowCheck.IsChecked == true,
            SmartFrame = SmartFrameCheck.IsChecked == true,
            Auto3DFromClicks = Auto3DCheck.IsChecked == true,
            DefaultScale = D(DefaultScaleBox.Text, 1.75),
            ZoomInMs = L(ZoomInBox.Text, 260),
            HoldMs = L(HoldBox.Text, 1100),
            ZoomOutMs = L(ZoomOutBox.Text, 360),
            MergeWindowMs = L(MergeBox.Text, 520),
            Easing = Selected<EasingKind>(AutoEasingCombo, EasingKind.SpringSoft),
            ZoomStyle = Selected<ZoomStyle>(AutoZoomStyleCombo, ZoomStyle.Focus),
            ThreeDPreset = Selected<Motion3DPreset>(Auto3DPresetCombo, Motion3DPreset.Focus),
            ThreeDIntensity = D(Auto3DIntensityBox.Text, 0.55)
        };
        var generated = await _engine.GenerateAsync(_eventsPath, options);
        _store.Replace(generated with { Cursor = currentCursor });
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _store.SaveAsync(_motionProjectPath);
            StatusText.Text = $"Saved {_motionProjectPath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save motion", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_motionProjectPath)) return;
        await _store.LoadAsync(_motionProjectPath);
        LoadCursorFields(_store.Project.Cursor);
        StatusText.Text = "Reloaded from disk";
    }

    private void RefreshGrids()
    {
        Dispatcher.Invoke(() =>
        {
            ZoomGrid.ItemsSource = null;
            ZoomGrid.ItemsSource = _store.Project.Zooms;
            Motion3DGrid.ItemsSource = null;
            Motion3DGrid.ItemsSource = _store.Project.Motions3D;
            SpotlightGrid.ItemsSource = null;
            SpotlightGrid.ItemsSource = _store.Project.Spotlights;
            BlurGrid.ItemsSource = null;
            BlurGrid.ItemsSource = _store.Project.Blurs;
        });
    }

    private void ZoomGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ZoomGrid.SelectedItem is not ZoomClip z) return;
        ZoomEditStart.Text = z.StartMs.ToString(CultureInfo.InvariantCulture);
        ZoomEditScale.Text = z.Scale.ToString("0.###", CultureInfo.InvariantCulture);
        ZoomEditX.Text = z.FocusX.ToString("0.##", CultureInfo.InvariantCulture);
        ZoomEditY.Text = z.FocusY.ToString("0.##", CultureInfo.InvariantCulture);
        ZoomEditIn.Text = z.ZoomInMs.ToString(CultureInfo.InvariantCulture);
        ZoomEditHold.Text = z.HoldMs.ToString(CultureInfo.InvariantCulture);
        ZoomEditOut.Text = z.ZoomOutMs.ToString(CultureInfo.InvariantCulture);
        ZoomEditEasing.SelectedItem = z.Easing;
    }

    private void AddZoom_Click(object sender, RoutedEventArgs e)
    {
        var clip = _engine.CreateManualZoom(
            L(ZoomEditStart.Text, 0),
            D(ZoomEditX.Text, 960),
            D(ZoomEditY.Text, 540),
            D(ZoomEditScale.Text, 1.75),
            L(ZoomEditIn.Text, 260),
            L(ZoomEditHold.Text, 1100),
            L(ZoomEditOut.Text, 360),
            Selected<EasingKind>(ZoomEditEasing, EasingKind.SpringSoft));
        _store.AddZoom(clip);
        ZoomGrid.SelectedItem = clip;
        StatusText.Text = "Manual zoom added";
    }

    private void ApplyZoom_Click(object sender, RoutedEventArgs e)
    {
        if (ZoomGrid.SelectedItem is not ZoomClip z) return;
        var updated = z with
        {
            StartMs = Math.Max(0, L(ZoomEditStart.Text, z.StartMs)),
            Scale = Math.Clamp(D(ZoomEditScale.Text, z.Scale), 1.01, 4.0),
            FocusX = D(ZoomEditX.Text, z.FocusX),
            FocusY = D(ZoomEditY.Text, z.FocusY),
            ZoomInMs = Math.Max(40, L(ZoomEditIn.Text, z.ZoomInMs)),
            HoldMs = Math.Max(0, L(ZoomEditHold.Text, z.HoldMs)),
            ZoomOutMs = Math.Max(40, L(ZoomEditOut.Text, z.ZoomOutMs)),
            Easing = Selected<EasingKind>(ZoomEditEasing, z.Easing),
            Source = "edited"
        };
        _store.UpdateZoom(updated);
        ZoomGrid.SelectedItem = updated;
        StatusText.Text = "Zoom block updated";
    }

    private void DeleteZoom_Click(object sender, RoutedEventArgs e)
    {
        if (ZoomGrid.SelectedItem is ZoomClip z) _store.RemoveZoom(z.Id);
    }

    private void Motion3DGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Motion3DGrid.SelectedItem is not Motion3DClip m) return;
        ThreeDPresetEdit.SelectedItem = m.Preset;
        ThreeDStart.Text = m.StartMs.ToString(CultureInfo.InvariantCulture);
        ThreeDDuration.Text = m.DurationMs.ToString(CultureInfo.InvariantCulture);
        ThreeDHold.Text = m.HoldMs.ToString(CultureInfo.InvariantCulture);
        ThreeDIntensity.Text = m.Intensity.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDRotX.Text = m.RotateX.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDRotY.Text = m.RotateY.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDRotZ.Text = m.RotateZ.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDDepth.Text = m.DepthZ.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDPerspective.Text = m.Perspective.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDShadow.IsChecked = m.Shadow;
        ThreeDReflection.IsChecked = m.Reflection;
    }

    private void ThreeDPresetEdit_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var preset = Selected<Motion3DPreset>(ThreeDPresetEdit, Motion3DPreset.Focus);
        var values = _engine.PresetValues(preset, D(ThreeDIntensity.Text, 0.6));
        ThreeDRotX.Text = values.RotateX.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDRotY.Text = values.RotateY.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDRotZ.Text = values.RotateZ.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDDepth.Text = values.DepthZ.ToString("0.###", CultureInfo.InvariantCulture);
        ThreeDPerspective.Text = values.Perspective.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void Add3D_Click(object sender, RoutedEventArgs e)
    {
        var preset = Selected<Motion3DPreset>(ThreeDPresetEdit, Motion3DPreset.Focus);
        var clip = _engine.CreateManual3D(L(ThreeDStart.Text, 0), L(ThreeDDuration.Text, 900), preset, D(ThreeDIntensity.Text, 0.6));
        clip = clip with
        {
            HoldMs = Math.Max(0, L(ThreeDHold.Text, 0)),
            RotateX = D(ThreeDRotX.Text, clip.RotateX),
            RotateY = D(ThreeDRotY.Text, clip.RotateY),
            RotateZ = D(ThreeDRotZ.Text, clip.RotateZ),
            DepthZ = D(ThreeDDepth.Text, clip.DepthZ),
            Perspective = D(ThreeDPerspective.Text, clip.Perspective),
            Shadow = ThreeDShadow.IsChecked == true,
            Reflection = ThreeDReflection.IsChecked == true
        };
        _store.Add3D(clip);
        Motion3DGrid.SelectedItem = clip;
    }

    private void Apply3D_Click(object sender, RoutedEventArgs e)
    {
        if (Motion3DGrid.SelectedItem is not Motion3DClip m) return;
        var preset = Selected<Motion3DPreset>(ThreeDPresetEdit, m.Preset);
        var applied = _engine.ApplyPreset(m, preset, D(ThreeDIntensity.Text, m.Intensity));
        var updated = applied with
        {
            StartMs = Math.Max(0, L(ThreeDStart.Text, m.StartMs)),
            DurationMs = Math.Max(120, L(ThreeDDuration.Text, m.DurationMs)),
            HoldMs = Math.Max(0, L(ThreeDHold.Text, m.HoldMs)),
            RotateX = D(ThreeDRotX.Text, applied.RotateX),
            RotateY = D(ThreeDRotY.Text, applied.RotateY),
            RotateZ = D(ThreeDRotZ.Text, applied.RotateZ),
            DepthZ = D(ThreeDDepth.Text, applied.DepthZ),
            Perspective = D(ThreeDPerspective.Text, applied.Perspective),
            Shadow = ThreeDShadow.IsChecked == true,
            Reflection = ThreeDReflection.IsChecked == true,
            Source = "edited"
        };
        _store.Update3D(updated);
        Motion3DGrid.SelectedItem = updated;
    }

    private void Delete3D_Click(object sender, RoutedEventArgs e)
    {
        if (Motion3DGrid.SelectedItem is Motion3DClip m) _store.Remove3D(m.Id);
    }

    private void ApplyCursor_Click(object sender, RoutedEventArgs e)
    {
        var cursor = new CursorEffectSettings(
            Selected<CursorStyle>(CursorStyleCombo, CursorStyle.WindowsArrow),
            EmptyToNull(CustomCursorPathBox.Text),
            CursorColorBox.Text.Trim(),
            Math.Clamp(D(CursorSizeBox.Text, 1.0), 0.25, 4.0),
            Math.Clamp(D(CursorOpacityBox.Text, 1.0), 0.0, 1.0),
            CursorShadowCheck.IsChecked == true,
            Math.Clamp(D(CursorShadowOpacityBox.Text, 0.35), 0.0, 1.0),
            CursorSmoothCheck.IsChecked == true,
            Math.Clamp(D(CursorSmoothingBox.Text, 0.72), 0.0, 1.0),
            CursorMotionBlurCheck.IsChecked == true,
            Math.Clamp(D(CursorMotionBlurBox.Text, 0.35), 0.0, 2.0),
            Selected<ClickRingStyle>(LeftClickStyleCombo, ClickRingStyle.Ripple),
            LeftClickColorBox.Text.Trim(),
            Selected<ClickRingStyle>(RightClickStyleCombo, ClickRingStyle.Pulse),
            RightClickColorBox.Text.Trim(),
            (int)Math.Clamp(L(ClickDurationBox.Text, 420), 50, 3000),
            ClickSoundCheck.IsChecked == true,
            EmptyToNull(LeftClickSoundBox.Text),
            EmptyToNull(RightClickSoundBox.Text));
        _store.UpdateCursor(cursor);
        StatusText.Text = "Cursor and click effects updated";
    }

    private void LoadCursorFields(CursorEffectSettings c)
    {
        CursorStyleCombo.SelectedItem = c.Style;
        CustomCursorPathBox.Text = c.CustomCursorPath ?? "";
        CursorColorBox.Text = c.Color;
        CursorSizeBox.Text = c.Size.ToString("0.###", CultureInfo.InvariantCulture);
        CursorOpacityBox.Text = c.Opacity.ToString("0.###", CultureInfo.InvariantCulture);
        CursorShadowCheck.IsChecked = c.Shadow;
        CursorShadowOpacityBox.Text = c.ShadowOpacity.ToString("0.###", CultureInfo.InvariantCulture);
        CursorSmoothCheck.IsChecked = c.SmoothMovement;
        CursorSmoothingBox.Text = c.Smoothing.ToString("0.###", CultureInfo.InvariantCulture);
        CursorMotionBlurCheck.IsChecked = c.MotionBlur;
        CursorMotionBlurBox.Text = c.MotionBlurStrength.ToString("0.###", CultureInfo.InvariantCulture);
        LeftClickStyleCombo.SelectedItem = c.LeftClickStyle;
        LeftClickColorBox.Text = c.LeftClickColor;
        RightClickStyleCombo.SelectedItem = c.RightClickStyle;
        RightClickColorBox.Text = c.RightClickColor;
        ClickDurationBox.Text = c.ClickAnimationMs.ToString(CultureInfo.InvariantCulture);
        ClickSoundCheck.IsChecked = c.ClickSound;
        LeftClickSoundBox.Text = c.LeftClickSoundPath ?? "";
        RightClickSoundBox.Text = c.RightClickSoundPath ?? "";
    }

    private void SpotlightGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpotlightGrid.SelectedItem is not SpotlightClip s) return;
        SpotlightShapeCombo.SelectedItem = s.Shape;
        SpotlightStart.Text = s.StartMs.ToString(CultureInfo.InvariantCulture);
        SpotlightDuration.Text = s.DurationMs.ToString(CultureInfo.InvariantCulture);
        SpotlightX.Text = s.X.ToString("0.##", CultureInfo.InvariantCulture);
        SpotlightY.Text = s.Y.ToString("0.##", CultureInfo.InvariantCulture);
        SpotlightW.Text = s.Width.ToString("0.##", CultureInfo.InvariantCulture);
        SpotlightH.Text = s.Height.ToString("0.##", CultureInfo.InvariantCulture);
        SpotlightDarkness.Text = s.Darkness.ToString("0.###", CultureInfo.InvariantCulture);
        SpotlightFeather.Text = s.Feather.ToString("0.###", CultureInfo.InvariantCulture);
        SpotlightFollowCursor.IsChecked = s.FollowCursor;
    }

    private SpotlightClip ReadSpotlight(Guid? id = null)
        => new(
            id ?? Guid.NewGuid(),
            Math.Max(0, L(SpotlightStart.Text, 0)),
            Math.Max(50, L(SpotlightDuration.Text, 1500)),
            Selected<SpotlightShape>(SpotlightShapeCombo, SpotlightShape.Circle),
            D(SpotlightX.Text, 960),
            D(SpotlightY.Text, 540),
            Math.Max(10, D(SpotlightW.Text, 460)),
            Math.Max(10, D(SpotlightH.Text, 260)),
            Math.Clamp(D(SpotlightDarkness.Text, 0.72), 0.0, 1.0),
            Math.Clamp(D(SpotlightFeather.Text, 0.18), 0.0, 1.0),
            SpotlightFollowCursor.IsChecked == true,
            true);

    private void AddSpotlight_Click(object sender, RoutedEventArgs e) => _store.AddSpotlight(ReadSpotlight());
    private void ApplySpotlight_Click(object sender, RoutedEventArgs e)
    {
        if (SpotlightGrid.SelectedItem is SpotlightClip s) _store.UpdateSpotlight(ReadSpotlight(s.Id));
    }
    private void DeleteSpotlight_Click(object sender, RoutedEventArgs e)
    {
        if (SpotlightGrid.SelectedItem is SpotlightClip s) _store.RemoveSpotlight(s.Id);
    }

    private void BlurGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BlurGrid.SelectedItem is not BlurClip b) return;
        BlurShapeCombo.SelectedItem = b.Shape;
        BlurStart.Text = b.StartMs.ToString(CultureInfo.InvariantCulture);
        BlurDuration.Text = b.DurationMs.ToString(CultureInfo.InvariantCulture);
        BlurX.Text = b.X.ToString("0.##", CultureInfo.InvariantCulture);
        BlurY.Text = b.Y.ToString("0.##", CultureInfo.InvariantCulture);
        BlurW.Text = b.Width.ToString("0.##", CultureInfo.InvariantCulture);
        BlurH.Text = b.Height.ToString("0.##", CultureInfo.InvariantCulture);
        BlurIntensity.Text = b.Intensity.ToString("0.###", CultureInfo.InvariantCulture);
        BlurFeather.Text = b.Feather.ToString("0.###", CultureInfo.InvariantCulture);
        BlurTrackCursor.IsChecked = b.TrackCursor;
    }

    private BlurClip ReadBlur(Guid? id = null)
        => new(
            id ?? Guid.NewGuid(),
            Math.Max(0, L(BlurStart.Text, 0)),
            Math.Max(50, L(BlurDuration.Text, 1500)),
            Selected<BlurShape>(BlurShapeCombo, BlurShape.RoundedRectangle),
            D(BlurX.Text, 720),
            D(BlurY.Text, 360),
            Math.Max(10, D(BlurW.Text, 480)),
            Math.Max(10, D(BlurH.Text, 220)),
            Math.Clamp(D(BlurIntensity.Text, 18), 0.1, 80),
            Math.Clamp(D(BlurFeather.Text, 0.12), 0.0, 1.0),
            BlurTrackCursor.IsChecked == true,
            true);

    private void AddBlur_Click(object sender, RoutedEventArgs e) => _store.AddBlur(ReadBlur());
    private void ApplyBlur_Click(object sender, RoutedEventArgs e)
    {
        if (BlurGrid.SelectedItem is BlurClip b) _store.UpdateBlur(ReadBlur(b.Id));
    }
    private void DeleteBlur_Click(object sender, RoutedEventArgs e)
    {
        if (BlurGrid.SelectedItem is BlurClip b) _store.RemoveBlur(b.Id);
    }

    private static void FillEnum<T>(ComboBox box, T selected) where T : struct, Enum
    {
        box.ItemsSource = Enum.GetValues<T>();
        box.SelectedItem = selected;
    }

    private static T Selected<T>(ComboBox box, T fallback) where T : struct, Enum
        => box.SelectedItem is T item ? item : fallback;

    private static double D(string? value, double fallback)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static long L(string? value, long fallback)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
