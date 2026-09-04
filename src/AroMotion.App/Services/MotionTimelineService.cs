using System.Collections.ObjectModel;
using System.Text.Json;
using AroMotion.App.Models;

namespace AroMotion.App.Services;

public sealed class MotionTimelineService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MotionProjectState Project { get; private set; } = new();

    public async Task<MotionProjectState> OpenAsync(string projectDirectory)
    {
        var statePath = Path.Combine(projectDirectory, "motion-project.json");
        var sourceVideo = ResolveFirstExisting(projectDirectory,
            "screen_master.mkv", "master.mkv", "recording_with_audio.mkv");
        var eventsPath = ResolveFirstExisting(projectDirectory,
            "events.jsonl", "capture-events.jsonl", "mouse_events.jsonl", "mouse_events.csv");

        if (File.Exists(statePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(statePath);
                Project = JsonSerializer.Deserialize<MotionProjectState>(json, JsonOptions) ?? new MotionProjectState();
            }
            catch
            {
                Project = new MotionProjectState();
            }
        }
        else
        {
            Project = new MotionProjectState();
        }

        Project.SourceVideo ??= sourceVideo;
        Project.EventsPath ??= eventsPath;
        Project.Zooms ??= new ObservableCollection<ZoomSegment>();
        Project.Motions3D ??= new ObservableCollection<Motion3DSegment>();
        Project.Spotlights ??= new ObservableCollection<SpotlightEffect>();
        Project.Blurs ??= new ObservableCollection<BlurEffect>();
        Project.Cursor ??= new CursorEffectProfile();
        Project.IsDirty = false;
        return Project;
    }

    public async Task SaveAsync(string projectDirectory)
    {
        Directory.CreateDirectory(projectDirectory);
        var statePath = Path.Combine(projectDirectory, "motion-project.json");
        var json = JsonSerializer.Serialize(Project, JsonOptions);
        await File.WriteAllTextAsync(statePath, json);
        Project.IsDirty = false;
    }

    public void ReplaceAutoZooms(IEnumerable<ZoomSegment> segments)
    {
        var manual = Project.Zooms.Where(x => x.Source == "manual").Select(x => x.Clone()).ToList();
        Project.Zooms.Clear();
        foreach (var item in segments.OrderBy(x => x.StartMs)) Project.Zooms.Add(item);
        foreach (var item in manual.OrderBy(x => x.StartMs)) Project.Zooms.Add(item);
        SortZooms();
        Touch();
    }

    public ZoomSegment AddManualZoom(long atMs, int x, int y, double scale = 1.75)
    {
        var segment = new AutoZoomGenerator().CreateManual(atMs, x, y, scale);
        Project.Zooms.Add(segment);
        SortZooms();
        Touch();
        return segment;
    }

    public void MoveZoom(ZoomSegment segment, long newStartMs)
    {
        var duration = Math.Max(1, segment.EndMs - segment.StartMs);
        segment.StartMs = Math.Max(0, newStartMs);
        segment.EndMs = segment.StartMs + duration;
        SortZooms();
        Touch();
    }

    public void ResizeZoom(ZoomSegment segment, long newStartMs, long newEndMs)
    {
        segment.StartMs = Math.Max(0, Math.Min(newStartMs, newEndMs - 1));
        segment.EndMs = Math.Max(segment.StartMs + 1, newEndMs);
        var duration = segment.EndMs - segment.StartMs;
        segment.ZoomInMs = Math.Min(segment.ZoomInMs, duration);
        segment.ZoomOutMs = Math.Min(segment.ZoomOutMs, duration);
        segment.HoldMs = Math.Max(0, duration - segment.ZoomInMs - segment.ZoomOutMs);
        SortZooms();
        Touch();
    }

    public void SetZoomFocus(ZoomSegment segment, int x, int y)
    {
        segment.FocusX = x;
        segment.FocusY = y;
        segment.SmartFrame = false;
        segment.FrameX = segment.FrameY = segment.FrameWidth = segment.FrameHeight = null;
        Touch();
    }

    public void DeleteZoom(ZoomSegment segment)
    {
        Project.Zooms.Remove(segment);
        Touch();
    }

    public Motion3DSegment Add3D(long startMs, long endMs, string preset)
    {
        var segment = MotionPresetService.CreatePreset(preset, startMs, endMs);
        Project.Motions3D.Add(segment);
        Touch();
        return segment;
    }

    public void Delete3D(Motion3DSegment segment)
    {
        Project.Motions3D.Remove(segment);
        Touch();
    }

    public SpotlightEffect AddSpotlight(long startMs, long endMs, int x, int y, string shape = "Circle")
    {
        var effect = new SpotlightEffect { StartMs = startMs, EndMs = endMs, X = x, Y = y, Shape = shape };
        Project.Spotlights.Add(effect);
        Touch();
        return effect;
    }

    public BlurEffect AddBlur(long startMs, long endMs, int x, int y)
    {
        var effect = new BlurEffect { StartMs = startMs, EndMs = endMs, X = x, Y = y };
        Project.Blurs.Add(effect);
        Touch();
        return effect;
    }

    public void Touch() => Project.IsDirty = true;

    private void SortZooms()
    {
        var sorted = Project.Zooms.OrderBy(x => x.StartMs).ThenBy(x => x.EndMs).ToList();
        Project.Zooms.Clear();
        foreach (var item in sorted) Project.Zooms.Add(item);
    }

    private static string? ResolveFirstExisting(string directory, params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
